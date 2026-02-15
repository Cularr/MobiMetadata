namespace MobiMetadata;

public class PDBHead : BaseHead
{
    private static readonly List<Attr> _attrs = [];
    private static readonly Attr _name = new(32, _attrs);
    private static readonly Attr _attributes = new(2, _attrs);
    private static readonly Attr _version = new(2, _attrs);
    private static readonly Attr _creationDate = new(4, _attrs);
    private static readonly Attr _modificationDate = new(4, _attrs);
    private static readonly Attr _lastBackupDate = new(4, _attrs);
    private static readonly Attr _modificationNumber = new(4, _attrs);
    private static readonly Attr _appInfoId = new(4, _attrs);
    private static readonly Attr _sortInfoId = new(4, _attrs);
    private static readonly Attr _type = new(4, _attrs);
    private static readonly Attr _creator = new(4, _attrs);
    private static readonly Attr _uniqueIdSeed = new(4, _attrs);
    private static readonly Attr _nextRecordListId = new(4, _attrs);
    private static readonly Attr _numRecords = new(2);
    private static readonly Attr _gapToData = new(2);

    private PDBRecordInfo[] _records = [];
    private Memory<byte> _numRecordsData;

    public PDBRecordInfo[] Records => _records;

    public PDBHead(bool skipProperties = false, bool skipRecords = false)
    {
        SkipProperties = skipProperties;
        SkipRecords = skipRecords;
    }

    public override async Task ReadHeaderAsync(Stream stream)
    {
        await SkipOrReadHeaderDataAsync(stream, _attrs.Sum(a => a.Length)).ConfigureAwait(false);

        _numRecordsData = new byte[_numRecords.Length];
        await stream.ReadAsync(_numRecordsData).ConfigureAwait(false);

        var recordCount = NumRecords;
        var recordDataSize = recordCount * PDBRecordInfo.RecordSize;

        if (SkipRecords)
        {
            stream.Position += recordDataSize;
        }
        else
        {
            var recordData = new byte[recordDataSize];
            await stream.ReadAsync(recordData).ConfigureAwait(false);

            _records = new PDBRecordInfo[recordCount];
            for (int i = 0; i < recordCount; i++)
            {
                _records[i] = new PDBRecordInfo(recordData, i * PDBRecordInfo.RecordSize);
            }
        }

        stream.Position += _gapToData.Length;
    }

    public string Name => GetPropAsUtf8Trimmed(_name);
    public ushort Attributes => GetPropAsUshort(_attributes);
    public ushort Version => GetPropAsUshort(_version);
    public uint CreationDate => GetPropAsUint(_creationDate);
    public uint ModificationDate => GetPropAsUint(_modificationDate);
    public uint LastBackupDate => GetPropAsUint(_lastBackupDate);
    public uint ModificationNumber => GetPropAsUint(_modificationNumber);
    public uint AppInfoId => GetPropAsUint(_appInfoId);
    public uint SortInfoId => GetPropAsUint(_sortInfoId);
    public uint Type => GetPropAsUint(_type);
    public string TypeName => GetPropAsUtf8Trimmed(_type);
    public uint Creator => GetPropAsUint(_creator);
    public string CreatorName => GetPropAsUtf8Trimmed(_creator);
    public uint UniqueIdSeed => GetPropAsUint(_uniqueIdSeed);
    public ushort NumRecords => Converter.ToUInt16(_numRecordsData.Span);

    public bool IsHdImageContainer => TypeName == "RBIN" && CreatorName == "CONT";

    public uint MobiHeaderSize => _records.Length > 1
        ? _records[1].DataOffset - _records[0].DataOffset
        : 0;
}
