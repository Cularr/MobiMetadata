using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MobiMetadata;

public class KF8Parser
{
    private readonly Stream _stream;
    private readonly PDBRecordInfo[] _records;
    private readonly MobiHead _mobiHeader;
    private readonly EXTHHead? _exthHeader;
    private readonly int _recordBase;
    private const int MaxIndexEntries = 1_000_000;
    private const int MaxIndexBytes = 64 * 1024 * 1024;
    private List<SkeletonEntry>? _skeletons;
    private List<ChunkEntry>? _chunks;
    private List<FdstEntry>? _fdst;
    private List<NcxEntry>? _ncx;
    public bool IsKf8 => _mobiHeader.FileVersion == 8;
    public uint Kf8BoundaryOffset => _exthHeader?.Kf8BoundaryOffset ?? uint.MaxValue;
    public bool IsDualMobi => _recordBase > 0;
    public IReadOnlyList<SkeletonEntry>? Skeletons => _skeletons;
    public IReadOnlyList<ChunkEntry>? Chunks => _chunks;
    public IReadOnlyList<FdstEntry>? Fdst => _fdst;
    public IReadOnlyList<NcxEntry>? Ncx => _ncx;

    public KF8Parser(Stream stream, PDBRecordInfo[] records, MobiHead mobiHeader, EXTHHead? exthHeader)
        : this(stream, records, mobiHeader, exthHeader, 0) { }

    public KF8Parser(Stream stream, PDBRecordInfo[] records, MobiHead mobiHeader, EXTHHead? exthHeader, int recordBase)
    {
        _stream = stream;
        _records = records;
        _mobiHeader = mobiHeader;
        _exthHeader = exthHeader;
        if (recordBase < 0 || recordBase >= records.Length) throw Invalid("Invalid rendition record base");
        _recordBase = recordBase;
    }

    public async Task ParseAsync()
    {
        _skeletons = null; _chunks = null; _fdst = null; _ncx = null;
        if (IsKf8)
        {
            if (Present(_mobiHeader.SkeletonIndex))
            {
                var index = await ReadIndexAsync(_mobiHeader.SkeletonIndex).ConfigureAwait(false);
                _skeletons = index.Entries.Select((e, i) => new SkeletonEntry { FileNumber = (uint)i, Name = e.Name, DivCount = Required(e, 1), StartPosition = Required(e, 6), Length = Required(e, 6, 1) }).ToList();
            }
            if (Present(_mobiHeader.FragmentIndex))
            {
                var index = await ReadIndexAsync(_mobiHeader.FragmentIndex).ConfigureAwait(false);
                _chunks = index.Entries.Select(e => new ChunkEntry { InsertPosition = ParsePosition(e.Name), FileNumber = Required(e, 3), SequenceNumber = Required(e, 4), StartPosition = Required(e, 6), Length = Required(e, 6, 1), TocText = StringAt(index, Required(e, 2)) }).ToList();
            }
            if (Present(_mobiHeader.FdstIndex)) _fdst = await ReadFdstAsync(_mobiHeader.FdstIndex).ConfigureAwait(false);
        }
        if (Present(_mobiHeader.NcxIndex))
        {
            var index = await ReadIndexAsync(_mobiHeader.NcxIndex).ConfigureAwait(false);
            _ncx = index.Entries.Select(e => new NcxEntry { Level = Optional(e, 4), Offset = Optional(e, 1), Length = Optional(e, 2), Title = StringAt(index, Required(e, 3)), FragmentNumber = e.Tags.ContainsKey(6) ? Required(e, 6) : null, FragmentOffset = e.Tags.ContainsKey(6) ? Required(e, 6, 1) : null }).ToList();
        }
    }

    private static bool Present(uint index) => index != uint.MaxValue && index != 0;
    private static MobiMetadataException Invalid(string message) => new("Invalid MOBI index: " + message);
    private static uint ParsePosition(string name) => uint.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : throw Invalid("Non-numeric fragment position");
    private static uint Required(IndexEntry e, byte tag, int position = 0) => e.Tags.TryGetValue(tag, out var values) && position < values.Count ? values[position] : throw Invalid($"Missing tag {tag}");
    private static uint Optional(IndexEntry e, byte tag) => e.Tags.TryGetValue(tag, out var values) && values.Count > 0 ? values[0] : 0;
    private static string StringAt(IndexData index, uint offset) => index.Strings.TryGetValue(offset, out var text) ? text : throw Invalid("CNCX string offset outside string table");
    private Task<byte[]> ReadRecordAsync(uint index)
    {
        long absolute = (long)_recordBase + index;
        if (absolute >= _records.Length) throw Invalid("Record index outside container");
        return StreamAccess.ReadRecordAsync(_stream, _records, (int)absolute, MaxIndexBytes);
    }
    private static uint U32(byte[] data, int offset)
    {
        if (offset < 0 || offset > data.Length - 4) throw Invalid("Truncated integer");
        return BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
    }
    private static int Size(uint value, int maximum)
    {
        if (value > maximum) throw Invalid("Count or offset exceeds enclosing record");
        return (int)value;
    }
    private static void Signature(byte[] data, int offset, string signature)
    {
        if (offset < 0 || offset > data.Length - 4 || !data.AsSpan(offset, 4).SequenceEqual(Encoding.ASCII.GetBytes(signature))) throw Invalid("Expected " + signature);
    }
    private static int Header(byte[] data)
    {
        Signature(data, 0, "INDX");
        int length = Size(U32(data, 4), data.Length);
        if (length < 56) throw Invalid("Short INDX header");
        return length;
    }
    private async Task<IndexData> ReadIndexAsync(uint first)
    {
        var meta = await ReadRecordAsync(first).ConfigureAwait(false);
        int headerLength = Header(meta);
        int recordCount = Size(U32(meta, 24), _records.Length);
        int stringCount = Size(U32(meta, 52), _records.Length);
        if ((long)_recordBase + first + recordCount + stringCount >= _records.Length) throw Invalid("Index records exceed container");
        var encoding = GetEncoding(U32(meta, 28));
        int tagStart = headerLength;
        if (headerLength >= 184 && U32(meta, 180) > 0) tagStart = Size(U32(meta, 180), meta.Length);
        Signature(meta, tagStart, "TAGX");
        int tagLength = Size(U32(meta, tagStart + 4), meta.Length - tagStart);
        int controls = Size(U32(meta, tagStart + 8), 256);
        if (tagLength < 12 || tagLength > 12 + 512 * 4 || (tagLength - 12) % 4 != 0 || controls == 0) throw Invalid("Invalid TAGX layout");
        var tags = new List<TagxEntry>();
        for (int p = tagStart + 12; p < tagStart + tagLength; p += 4) tags.Add(new(meta[p], meta[p + 1], meta[p + 2], meta[p + 3]));
        var result = new IndexData();
        long bytes = meta.Length;
        for (int r = 0; r < recordCount; r++)
        {
            var data = await ReadRecordAsync(checked(first + 1u + (uint)r)).ConfigureAwait(false);
            bytes += data.Length;
            if (bytes > MaxIndexBytes) throw Invalid("Index exceeds byte budget");
            int head = Header(data);
            int idxt = Size(U32(data, 20), data.Length);
            Signature(data, idxt, "IDXT");
            int count = Size(U32(data, 24), (data.Length - idxt - 4) / 2);
            if (count > MaxIndexEntries - result.Entries.Count) throw Invalid("Too many index entries");
            for (int i = 0; i < count; i++)
            {
                int start = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idxt + 4 + i * 2));
                int end = i + 1 == count ? idxt : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(idxt + 6 + i * 2));
                if (start < head || end <= start || end > idxt) throw Invalid("Invalid IDXT entry bounds");
                int nameLength = data[start];
                int position = start + 1 + nameLength;
                if (position > end - controls) throw Invalid("Truncated index entry");
                var name = encoding.GetString(data, start + 1, nameLength);
                result.Entries.Add(new(name, ReadTags(data, position, end, controls, tags)));
            }
        }
        for (int r = 0; r < stringCount; r++)
        {
            var data = await ReadRecordAsync(checked(first + 1u + (uint)recordCount + (uint)r)).ConfigureAwait(false);
            bytes += data.Length;
            if (bytes > MaxIndexBytes || data.Length > 65536) throw Invalid("CNCX exceeds byte budget");
            int p = 0;
            while (p < data.Length)
            {
                // Records may end with zero padding, which is not a string.
                if (data[p] == 0 && data.AsSpan(p).IndexOfAnyExcept((byte)0) < 0) break;
                int offset = p;
                int length = Size(VarInt(data, ref p, data.Length), data.Length - p);
                if (result.Strings.Count >= MaxIndexEntries) throw Invalid("Too many CNCX strings");
                result.Strings.Add(checked((uint)r * 65536 + (uint)offset), encoding.GetString(data, p, length));
                p += length;
            }
        }
        return result;
    }
    private static Dictionary<byte, List<uint>> ReadTags(byte[] data, int start, int end, int controls, List<TagxEntry> tags)
    {
        int p = start + controls, control = 0;
        var descriptors = new List<(TagxEntry Tag, uint Count, bool Bytes)>();
        foreach (var tag in tags)
        {
            if (tag.ControlByte == 1) { control++; continue; }
            if (tag.ControlByte != 0 || control >= controls || tag.Bitmask == 0 || tag.ValueCount == 0) throw Invalid("Invalid TAGX control");
            uint masked = (uint)(data[start + control] & tag.Bitmask);
            if (masked == 0) continue;
            bool byteCount = masked == tag.Bitmask && (tag.Bitmask & (tag.Bitmask - 1)) != 0;
            uint count;
            if (byteCount) count = VarInt(data, ref p, end);
            else
            {
                int shift = System.Numerics.BitOperations.TrailingZeroCount((uint)tag.Bitmask);
                count = (masked >> shift) * tag.ValueCount;
            }
            descriptors.Add((tag, count, byteCount));
        }
        var result = new Dictionary<byte, List<uint>>();
        foreach (var d in descriptors)
        {
            int count = Size(d.Count, end - p);
            var values = new List<uint>();
            if (d.Bytes)
            {
                int stop = p + count;
                while (p < stop) values.Add(VarInt(data, ref p, stop));
                if (values.Count % d.Tag.ValueCount != 0) throw Invalid("Invalid TAGX value group");
            }
            else for (int i = 0; i < count; i++) values.Add(VarInt(data, ref p, end));
            if (!result.TryAdd(d.Tag.Tag, values)) throw Invalid("Duplicate TAGX tag");
        }
        if (data.AsSpan(p, end - p).IndexOfAnyExcept((byte)0) >= 0) throw Invalid("Unconsumed index entry data");
        return result;
    }
    private static uint VarInt(byte[] data, ref int p, int end)
    {
        uint value = 0;
        for (int i = 0; i < 5 && p < end; i++)
        {
            byte b = data[p++];
            if (value > (uint.MaxValue >> 7)) throw Invalid("Variable integer overflow");
            value = (value << 7) | (uint)(b & 127);
            if ((b & 128) != 0) return value;
        }
        throw Invalid("Unterminated variable integer");
    }
    private static Encoding GetEncoding(uint code)
    {
        if (code == 65001 || code == uint.MaxValue || code == 0) return Encoding.UTF8;
        if (code == 1252) return MobiEncoding.GetEncoding(1252);
        throw new NotSupportedException($"Unsupported INDX encoding {code}");
    }
    private async Task<List<FdstEntry>> ReadFdstAsync(uint index)
    {
        var data = await ReadRecordAsync(index).ConfigureAwait(false);
        Signature(data, 0, "FDST");
        int start = Size(U32(data, 4), data.Length);
        if (start < 12) throw Invalid("Invalid FDST header");
        int count = Size(U32(data, 8), (data.Length - start) / 8);
        var result = new List<FdstEntry>();
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            uint from = U32(data, start + i * 8), to = U32(data, start + i * 8 + 4);
            if (from < previous || to < from) throw Invalid("Invalid FDST flow range");
            result.Add(new(i, from, to)); previous = to;
        }
        return result;
    }
    private sealed record IndexEntry(string Name, Dictionary<byte, List<uint>> Tags);
    private sealed class IndexData
    {
        public List<IndexEntry> Entries { get; } = [];
        public Dictionary<uint, string> Strings { get; } = [];
    }
}

// Retained public data types for compatibility with existing callers.
public record IndxRecord
{
    public uint HeaderLength { get; init; }
    public int EntryCount { get; init; }
    public IndxRecord(uint headerLength, int entryCount)
    {
        HeaderLength = headerLength;
        EntryCount = entryCount;
    }
    public List<TagxEntry> TagxEntries { get; } = [];
    public List<IndxEntry> Entries { get; } = [];
    public bool HasMoreEntries => EntryCount > 0 && Entries.Count > 0;
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
    public uint? FragmentNumber { get; set; }
    public uint? FragmentOffset { get; set; }
    public List<NcxEntry> Children { get; } = [];
}



