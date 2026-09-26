namespace MobiMetadata;

public class TextContent
{
    private const int MaximumTextBytes = 256 * 1024 * 1024;
    private readonly Stream _stream;
    private readonly PDBRecordInfo[] _records;
    private readonly PalmDOCHead _palmDocHeader;
    private readonly MobiHead _mobiHeader;
    private readonly int _firstTextRecord;
    private HuffCdicDecompressor? _huffCdic;

    public TextContent(Stream stream, PDBRecordInfo[] records, PalmDOCHead palmDocHeader, MobiHead mobiHeader)
        : this(stream, records, palmDocHeader, mobiHeader, 1) { }

    public TextContent(Stream stream, PDBRecordInfo[] records, PalmDOCHead palmDocHeader, MobiHead mobiHeader, int firstTextRecord)
    {
        _stream = stream;
        _records = records;
        _palmDocHeader = palmDocHeader;
        _mobiHeader = mobiHeader;
        _firstTextRecord = firstTextRecord;
    }

    public string CompressionName => _palmDocHeader.CompressionName;
    public string TextEncoding => _mobiHeader.TextEncodingName;
    public uint TextLength => _palmDocHeader.TextLength;
    public ushort RecordCount => _palmDocHeader.RecordCount;

    public async Task InitializeHuffCdicAsync()
    {
        if (_palmDocHeader.Compression != PalmDOCHead.CompressionHuffCdic) return;
        var offset = (long)_firstTextRecord - 1 + _mobiHeader.HuffmanRecordOffset;
        if (offset < 0 || offset > uint.MaxValue) throw new MobiMetadataException("Invalid HUFF record offset");
        var decoder = new HuffCdicDecompressor();
        await decoder.ReadRecordsAsync(_stream, _records, (uint)offset, _mobiHeader.HuffmanRecordCount).ConfigureAwait(false);
        _huffCdic = decoder;
    }

    public async Task<string> ExtractTextAsync()
    {
        if (_palmDocHeader.EncryptionType != 0) throw new MobiMetadataException("Encrypted MOBI text is not supported");
        if (TextLength > MaximumTextBytes) throw new MobiMetadataException("Text extraction size limit exceeded");
        if (_firstTextRecord < 0 || _firstTextRecord > _records.Length || RecordCount > _records.Length - _firstTextRecord)
            throw new MobiMetadataException("Invalid text record range");
        if (_palmDocHeader.RecordSize == 0 && RecordCount != 0) throw new MobiMetadataException("Invalid text record size");
        if (_palmDocHeader.Compression == PalmDOCHead.CompressionHuffCdic && _huffCdic == null)
            await InitializeHuffCdicAsync().ConfigureAwait(false);

        using var output = new MemoryStream((int)Math.Min(TextLength, 65536));
        for (var i = 0; i < RecordCount; i++)
        {
            var record = await StreamAccess.ReadRecordAsync(_stream, _records, _firstTextRecord + i).ConfigureAwait(false);
            var payloadLength = GetPayloadLength(record, _mobiHeader.ExtraDataFlags);
            var data = _palmDocHeader.Compression switch
            {
                PalmDOCHead.CompressionNone => record.AsSpan(0, payloadLength).ToArray(),
                PalmDOCHead.CompressionPalmDoc => PalmDocDecompressor.Decompress(record.AsSpan(0, payloadLength), _palmDocHeader.RecordSize),
                PalmDOCHead.CompressionHuffCdic => _huffCdic!.Decompress(record.AsSpan(0, payloadLength), _palmDocHeader.RecordSize),
                _ => throw new MobiMetadataException($"Unknown compression: {_palmDocHeader.Compression}")
            };
            if (data.Length > _palmDocHeader.RecordSize) throw new MobiMetadataException("Text record exceeds declared size");
            var remaining = (int)(TextLength - output.Length);
            if (data.Length > remaining && i != RecordCount - 1)
                throw new MobiMetadataException("Text exceeds declared length before final record");
            output.Write(data, 0, Math.Min(data.Length, remaining));
        }
        if (output.Length != TextLength) throw new MobiMetadataException("Text is shorter than its declared length");
        return MobiEncoding.GetEncoding(_mobiHeader.TextEncoding).GetString(output.GetBuffer(), 0, (int)output.Length);
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

