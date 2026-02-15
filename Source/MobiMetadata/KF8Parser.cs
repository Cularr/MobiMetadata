using System.Buffers.Binary;
using System.Text;

namespace MobiMetadata;

public class KF8Parser
{
    private readonly Stream _stream;
    private readonly PDBRecordInfo[] _records;
    private readonly MobiHead _mobiHeader;
    private readonly EXTHHead? _exthHeader;

    private List<SkeletonEntry>? _skeletons;
    private List<ChunkEntry>? _chunks;
    private List<FdstEntry>? _fdst;
    private List<NcxEntry>? _ncx;

    public bool IsKf8 => _mobiHeader.MobiType == 248;
    public uint Kf8BoundaryOffset => _exthHeader?.Kf8BoundaryOffset ?? 0;
    public bool IsDualMobi => Kf8BoundaryOffset > 0;

    public IReadOnlyList<SkeletonEntry>? Skeletons => _skeletons;
    public IReadOnlyList<ChunkEntry>? Chunks => _chunks;
    public IReadOnlyList<FdstEntry>? Fdst => _fdst;
    public IReadOnlyList<NcxEntry>? Ncx => _ncx;

    public KF8Parser(Stream stream, PDBRecordInfo[] records, MobiHead mobiHeader, EXTHHead? exthHeader)
    {
        _stream = stream;
        _records = records;
        _mobiHeader = mobiHeader;
        _exthHeader = exthHeader;
    }

    public async Task ParseAsync()
    {
        if (!IsKf8 && !IsDualMobi) return;

        if (_mobiHeader.ExtraIndex0 > 0)
            _skeletons = await ParseIndexAsync(_mobiHeader.ExtraIndex0, ParseSkeleton).ConfigureAwait(false);

        if (_mobiHeader.ExtraIndex1 > 0)
            _chunks = await ParseIndexAsync(_mobiHeader.ExtraIndex1, ParseChunk).ConfigureAwait(false);

        if (_mobiHeader.ExtraIndex2 > 0)
            _fdst = await ParseFdstAsync(_mobiHeader.ExtraIndex2).ConfigureAwait(false);

        if (_mobiHeader.ExtraIndex3 > 0)
            _ncx = await ParseIndexAsync(_mobiHeader.ExtraIndex3, ParseNcx).ConfigureAwait(false);
    }

    private async Task<List<T>?> ParseIndexAsync<T>(uint recordIndex, Func<IndxRecord, int, T> parser)
    {
        if (recordIndex >= _records.Length) return null;

        var result = new List<T>();
        var records = await ReadIndxRecordsAsync(recordIndex).ConfigureAwait(false);

        foreach (var indx in records)
        {
            for (int i = 0; i < indx.Entries.Count; i++)
            {
                result.Add(parser(indx, i));
            }
        }

        return result;
    }

    private async Task<List<IndxRecord>> ReadIndxRecordsAsync(uint startIndex)
    {
        var records = new List<IndxRecord>();
        var currentOffset = startIndex;

        while (currentOffset < _records.Length)
        {
            var record = _records[currentOffset];
            _stream.Position = record.DataOffset;

            var nextOffset = currentOffset + 1 < _records.Length
                ? _records[currentOffset + 1].DataOffset
                : record.DataOffset + 4096;

            var data = new byte[nextOffset - record.DataOffset];
            await _stream.ReadAsync(data).ConfigureAwait(false);

            var indx = ParseIndxRecord(data);
            if (indx == null) break;

            records.Add(indx);
            if (!indx.HasMoreEntries) break;

            currentOffset++;
        }

        return records;
    }

    private IndxRecord? ParseIndxRecord(byte[] data)
    {
        if (data.Length < 192) return null;
        if (data[0] != 'I' || data[1] != 'N' || data[2] != 'D' || data[3] != 'X') return null;

        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
        var entryCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));

        var indx = new IndxRecord(headerLength, entryCount);

        ParseTagxSection(data, indx);
        ParseEntries(data, indx);

        return indx;
    }

    private static void ParseTagxSection(byte[] data, IndxRecord indx)
    {
        const int tagxOffset = 0xE4;
        if (tagxOffset + 4 >= data.Length) return;
        if (data[tagxOffset] != 'T' || data[tagxOffset + 1] != 'A' || data[tagxOffset + 2] != 'G' || data[tagxOffset + 3] != 'X') return;

        var entryCount = data[tagxOffset + 8];
        for (int i = 0; i < entryCount; i++)
        {
            var offset = tagxOffset + 12 + i * 4;
            if (offset + 3 < data.Length)
            {
                indx.TagxEntries.Add(new TagxEntry(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]));
            }
        }
    }

    private static void ParseEntries(byte[] data, IndxRecord indx)
    {
        var entryOffset = 192;

        for (int i = 0; i < indx.EntryCount && entryOffset + 8 < data.Length; i++)
        {
            var entryLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entryOffset));

            if (entryOffset + entryLength > data.Length) break;

            byte[]? entryData = null;
            if (entryLength >= 8)
            {
                entryData = new byte[entryLength - 4];
                Buffer.BlockCopy(data, entryOffset + 4, entryData, 0, entryData.Length);
            }

            indx.Entries.Add(new IndxEntry(entryOffset, (int)entryLength, entryData));
            entryOffset += (int)entryLength;
        }
    }

    private SkeletonEntry ParseSkeleton(IndxRecord indx, int index)
    {
        var entry = indx.Entries[index];
        var skeleton = new SkeletonEntry();

        if (entry.Data is { Length: >= 12 })
        {
            skeleton.FileNumber = ReadUInt32(entry.Data, 0);
            skeleton.DivCount = ReadUInt32(entry.Data, 4);
            skeleton.StartPosition = ReadUInt32(entry.Data, 8);

            if (entry.Data.Length >= 16)
                skeleton.Length = ReadUInt32(entry.Data, 12);

            if (entry.Data.Length > 16)
                skeleton.Name = Encoding.UTF8.GetString(entry.Data, 16, entry.Data.Length - 16).TrimEnd('\0');
        }

        return skeleton;
    }

    private ChunkEntry ParseChunk(IndxRecord indx, int index)
    {
        var entry = indx.Entries[index];
        var chunk = new ChunkEntry();

        if (entry.Data is { Length: >= 16 })
        {
            chunk.InsertPosition = ReadUInt32(entry.Data, 0);
            chunk.FileNumber = ReadUInt32(entry.Data, 4);
            chunk.SequenceNumber = ReadUInt32(entry.Data, 8);
            chunk.StartPosition = ReadUInt32(entry.Data, 12);

            if (entry.Data.Length >= 20)
                chunk.Length = ReadUInt32(entry.Data, 16);

            if (entry.Data.Length > 20)
                chunk.TocText = Encoding.UTF8.GetString(entry.Data, 20, entry.Data.Length - 20).TrimEnd('\0');
        }

        return chunk;
    }

    private NcxEntry ParseNcx(IndxRecord indx, int index)
    {
        var entry = indx.Entries[index];
        var ncx = new NcxEntry();

        if (entry.Data is { Length: >= 8 })
        {
            ncx.Level = ReadUInt32(entry.Data, 0);
            ncx.Offset = ReadUInt32(entry.Data, 4);

            if (entry.Data.Length >= 12)
                ncx.Length = ReadUInt32(entry.Data, 8);

            if (entry.Data.Length > 12)
                ncx.Title = Encoding.UTF8.GetString(entry.Data, 12, entry.Data.Length - 12).TrimEnd('\0');
        }

        return ncx;
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private async Task<List<FdstEntry>?> ParseFdstAsync(uint recordIndex)
    {
        if (recordIndex >= _records.Length) return null;

        var record = _records[recordIndex];
        _stream.Position = record.DataOffset;

        var nextOffset = recordIndex + 1 < _records.Length
            ? _records[recordIndex + 1].DataOffset
            : record.DataOffset + 4096;

        var data = new byte[nextOffset - record.DataOffset];
        var bytesRead = await _stream.ReadAsync(data).ConfigureAwait(false);

        if (bytesRead < 12) return null;
        if (data[0] != 'F' || data[1] != 'D' || data[2] != 'S' || data[3] != 'T') return null;

        var sectionCount = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        var result = new List<FdstEntry>();
        var offset = 12;

        for (int i = 0; i < sectionCount && offset + 8 <= data.Length; i++)
        {
            var start = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
            var end = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4));
            result.Add(new FdstEntry(i, start, end));
            offset += 8;
        }

        return result;
    }

}

public record IndxRecord
{
    public uint HeaderLength { get; init; }
    public int EntryCount { get; init; }
    public List<TagxEntry> TagxEntries { get; } = [];
    public List<IndxEntry> Entries { get; } = [];

    public bool HasMoreEntries => EntryCount > 0 && Entries.Count > 0;

    public IndxRecord(uint headerLength, int entryCount)
    {
        HeaderLength = headerLength;
        EntryCount = entryCount;
    }
}

public record TagxEntry(byte Tag, byte ValueCount, byte Bitmask, byte ControlByte);

public record IndxEntry(int Offset, int Length, byte[]? Data);

public record SkeletonEntry
{
    public uint FileNumber { get; set; }
    public uint DivCount { get; set; }
    public uint StartPosition { get; set; }
    public uint Length { get; set; }
    public string? Name { get; set; }
}

public record ChunkEntry
{
    public uint InsertPosition { get; set; }
    public uint FileNumber { get; set; }
    public uint SequenceNumber { get; set; }
    public uint StartPosition { get; set; }
    public uint Length { get; set; }
    public string? TocText { get; set; }
}

public record FdstEntry(int Index, uint StartOffset, uint EndOffset);

public record NcxEntry
{
    public uint Level { get; set; }
    public uint Offset { get; set; }
    public uint Length { get; set; }
    public string? Title { get; set; }
    public List<NcxEntry> Children { get; } = [];
}
