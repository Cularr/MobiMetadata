namespace MobiMetadata;

public class TextContent
{
    private const int MaximumTextBytes = 256 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly PDBRecordInfo[] _records;
    private readonly ushort _compression;
    private readonly ushort _encryptionType;
    private readonly ushort _recordSize;
    private readonly ushort _extraDataFlags;
    private readonly uint _textEncoding;
    private readonly uint _huffmanRecordOffset;
    private readonly uint _huffmanRecordCount;
    private readonly int _firstTextRecord;
    private HuffCdicDecompressor? _huffCdic;

    public TextContent(Stream stream, PDBRecordInfo[] records, PalmDOCHead palmDocHeader, MobiHead mobiHeader)
        : this(stream, records, palmDocHeader, mobiHeader, 1) { }

    public TextContent(Stream stream, PDBRecordInfo[] records, PalmDOCHead palmDocHeader, MobiHead mobiHeader, int firstTextRecord)
    {
        _stream = stream;
        _records = records;
        _compression = palmDocHeader.Compression;
        _encryptionType = palmDocHeader.EncryptionType;
        _recordSize = palmDocHeader.RecordSize;
        _extraDataFlags = mobiHeader.ExtraDataFlags;
        _textEncoding = mobiHeader.TextEncoding;
        _huffmanRecordOffset = mobiHeader.HuffmanRecordOffset;
        _huffmanRecordCount = mobiHeader.HuffmanRecordCount;
        CompressionName = palmDocHeader.CompressionName;
        TextEncoding = mobiHeader.TextEncodingName;
        TextLength = palmDocHeader.TextLength;
        RecordCount = palmDocHeader.RecordCount;
        _firstTextRecord = firstTextRecord;
    }

    public string CompressionName { get; }
    public string TextEncoding { get; }
    public uint TextLength { get; }
    public ushort RecordCount { get; }

    public async Task InitializeHuffCdicAsync()
    {
        if (_compression != PalmDOCHead.CompressionHuffCdic) return;
        var offset = (long)_firstTextRecord - 1 + _huffmanRecordOffset;
        if (offset < 0 || offset > uint.MaxValue) throw new MobiMetadataException("Invalid HUFF record offset");
        var decoder = new HuffCdicDecompressor();
        await decoder.ReadRecordsAsync(_stream, _records, (uint)offset, _huffmanRecordCount).ConfigureAwait(false);
        _huffCdic = decoder;
    }

    public async Task<string> ExtractTextAsync()
    {
        if (_encryptionType != 0) throw new MobiMetadataException("Encrypted MOBI text is not supported");
        if (TextLength > MaximumTextBytes) throw new MobiMetadataException("Text extraction size limit exceeded");
        if (_firstTextRecord < 0 || _firstTextRecord > _records.Length || RecordCount > _records.Length - _firstTextRecord)
            throw new MobiMetadataException("Invalid text record range");
        if (_recordSize == 0 && RecordCount != 0) throw new MobiMetadataException("Invalid text record size");
        if (_compression == PalmDOCHead.CompressionHuffCdic && _huffCdic == null)
            await InitializeHuffCdicAsync().ConfigureAwait(false);

        // One budget covers the entire extraction, including recursive dictionary expansion.
        long remainingHuffmanWork = 16_000_000L;
        using var output = new MemoryStream((int)Math.Min(TextLength, 65536));
        for (var i = 0; i < RecordCount; i++)
        {
            var record = await StreamAccess.ReadRecordAsync(_stream, _records, _firstTextRecord + i).ConfigureAwait(false);
            var payloadLength = GetPayloadLength(record, _extraDataFlags);
            var data = _compression switch
            {
                PalmDOCHead.CompressionNone => record.AsSpan(0, payloadLength).ToArray(),
                PalmDOCHead.CompressionPalmDoc => PalmDocDecompressor.Decompress(record.AsSpan(0, payloadLength), _recordSize),
                PalmDOCHead.CompressionHuffCdic => _huffCdic!.Decompress(record.AsSpan(0, payloadLength), _recordSize, ref remainingHuffmanWork),
                _ => throw new MobiMetadataException($"Unknown compression: {_compression}")
            };
            if (data.Length > _recordSize) throw new MobiMetadataException("Text record exceeds declared size");
            var remaining = (int)(TextLength - output.Length);
            if (data.Length > remaining && i != RecordCount - 1)
                throw new MobiMetadataException("Text exceeds declared length before final record");
            var written = Math.Min(data.Length, remaining);
            output.Write(data, 0, written);
            // Earn further work only from actual output, never from an untrusted length claim.
            remainingHuffmanWork += 4L * written;
        }
        if (output.Length != TextLength) throw new MobiMetadataException("Text is shorter than its declared length");
        return MobiEncoding.GetEncoding(_textEncoding).GetString(output.GetBuffer(), 0, (int)output.Length);
    }

    private static int GetPayloadLength(ReadOnlySpan<byte> record, ushort flags)
    {
        var end = record.Length;
        for (var bit = 15; bit >= 1; bit--)
        {
            if ((flags & (1 << bit)) == 0) continue;
            var size = 0;
            var encodedBytes = 0;
            var terminated = false;
            for (var n = 0; n < 4 && n < end; n++)
            {
                var value = record[end - n - 1];
                size |= (value & 127) << (n * 7);
                encodedBytes++;
                if ((value & 128) != 0) { terminated = true; break; }
            }
            if (!terminated || size < encodedBytes || size > end)
                throw new MobiMetadataException("Invalid trailing text entry");
            end -= size;
        }
        if ((flags & 1) != 0)
        {
            if (end == 0) throw new MobiMetadataException("Missing text overlap trailer");
            var size = (record[end - 1] & 3) + 1;
            if (size > end) throw new MobiMetadataException("Invalid text overlap trailer");
            end -= size;
        }
        return end;
    }
}
