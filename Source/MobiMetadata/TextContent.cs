using System.Text;

namespace MobiMetadata;

public class TextContent
{
    private readonly Stream _stream;
    private readonly PDBRecordInfo[] _records;
    private readonly PalmDOCHead _palmDocHeader;
    private readonly MobiHead _mobiHeader;
    private HuffCdicDecompressor? _huffCdic;

    public TextContent(Stream stream, PDBRecordInfo[] records, PalmDOCHead palmDocHeader, MobiHead mobiHeader)
    {
        _stream = stream;
        _records = records;
        _palmDocHeader = palmDocHeader;
        _mobiHeader = mobiHeader;
    }

    public string CompressionName => _palmDocHeader.CompressionName;
    public string TextEncoding => _mobiHeader.TextEncodingName;
    public uint TextLength => _palmDocHeader.TextLength;
    public ushort RecordCount => _palmDocHeader.RecordCount;

    public async Task InitializeHuffCdicAsync()
    {
        if (_palmDocHeader.Compression != PalmDOCHead.CompressionHuffCdic) return;

        _huffCdic = new HuffCdicDecompressor();
        await _huffCdic.ReadRecordsAsync(
            _stream, _records,
            _mobiHeader.HuffmanRecordOffset,
            _mobiHeader.HuffmanRecordCount).ConfigureAwait(false);
    }

    public async Task<string> ExtractTextAsync()
    {
        if (_palmDocHeader.Compression == PalmDOCHead.CompressionHuffCdic && _huffCdic == null)
        {
            await InitializeHuffCdicAsync().ConfigureAwait(false);
        }

        var firstRecord = _mobiHeader.FirstContentRecord;
        var lastRecord = _mobiHeader.LastContentRecord;

        if (firstRecord == 0) firstRecord = 1;
        if (lastRecord == 0) lastRecord = (ushort)(_palmDocHeader.RecordCount - 1);

        var textRecords = new List<byte[]>();

        for (ushort i = firstRecord; i <= lastRecord && i < _records.Length; i++)
        {
            var recordData = await ReadTextRecordAsync(i).ConfigureAwait(false);
            if (recordData != null) textRecords.Add(recordData);
        }

        var combined = CombineRecords(textRecords);
        return DecodeText(combined);
    }

    private static byte[] CombineRecords(List<byte[]> records)
    {
        var totalLength = records.Sum(r => r.Length);
        var combined = new byte[totalLength];
        var offset = 0;

        foreach (var record in records)
        {
            Buffer.BlockCopy(record, 0, combined, offset, record.Length);
            offset += record.Length;
        }

        return combined;
    }

    private async Task<byte[]?> ReadTextRecordAsync(ushort index)
    {
        if (index >= _records.Length) return null;

        var record = _records[index];
        var nextRecord = index + 1 < _records.Length ? _records[index + 1] : null;

        var offset = record.DataOffset;
        var length = nextRecord != null
            ? (int)(nextRecord.DataOffset - offset)
            : _palmDocHeader.RecordSize;

        _stream.Position = offset;
        var recordData = new byte[length];
        await _stream.ReadAsync(recordData).ConfigureAwait(false);

        if (length < 2) return recordData;

        // Record format: 2-byte trailing count + data + trailing bytes
        var trailBytes = recordData[1];
        var dataStart = 2;
        var dataEnd = length - trailBytes;

        if (dataEnd <= dataStart) return [];

        var compressedData = new byte[dataEnd - dataStart];
        Buffer.BlockCopy(recordData, dataStart, compressedData, 0, compressedData.Length);

        var decompressedLength = _palmDocHeader.RecordSize - 2 - trailBytes;

        return _palmDocHeader.Compression switch
        {
            PalmDOCHead.CompressionNone => compressedData,
            PalmDOCHead.CompressionPalmDoc => PalmDocDecompressor.Decompress(compressedData, decompressedLength),
            PalmDOCHead.CompressionHuffCdic => DecompressHuffCdic(compressedData, decompressedLength),
            _ => throw new MobiMetadataException($"Unknown compression: {_palmDocHeader.Compression}")
        };
    }

    private byte[] DecompressHuffCdic(byte[] data, int length)
    {
        if (_huffCdic == null)
            throw new MobiMetadataException("HUFF/CDIC not initialized");
        return _huffCdic.Decompress(data, length);
    }

    private string DecodeText(byte[] data)
    {
        return _mobiHeader.TextEncoding switch
        {
            65001 => Encoding.UTF8.GetString(data),
            1252 => Encoding.GetEncoding(1252).GetString(data),
            _ => Encoding.UTF8.GetString(data)
        };
    }
}
