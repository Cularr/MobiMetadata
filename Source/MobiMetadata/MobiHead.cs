using System.Text;

namespace MobiMetadata;

public class MobiHead : BaseHead
{
    private static readonly List<Attr> _attrs = [];

    private static readonly Attr _identifier = new(4, _attrs);
    private static readonly Attr _headerLength = new(4, _attrs);
    private static readonly Attr _mobiType = new(4, _attrs);
    private static readonly Attr _textEncoding = new(4, _attrs);
    private static readonly Attr _uniqueId = new(4, _attrs);
    private static readonly Attr _fileVersion = new(4, _attrs);

    private static readonly Attr _orthographicIndex = new(4, _attrs);
    private static readonly Attr _inflectionIndex = new(4, _attrs);
    private static readonly Attr _indexNames = new(4, _attrs);
    private static readonly Attr _indexKeys = new(4, _attrs);
    private static readonly Attr _extraIndex0 = new(4, _attrs);
    private static readonly Attr _extraIndex1 = new(4, _attrs);
    private static readonly Attr _extraIndex2 = new(4, _attrs);
    private static readonly Attr _extraIndex3 = new(4, _attrs);
    private static readonly Attr _extraIndex4 = new(4, _attrs);
    private static readonly Attr _extraIndex5 = new(4, _attrs);
    private static readonly Attr _firstNonBookIndex = new(4, _attrs);

    private static readonly Attr _fullNameOffset = new(4, _attrs);
    private static readonly Attr _fullNameLength = new(4, _attrs);

    private static readonly Attr _locale = new(4, _attrs);
    private static readonly Attr _inputLanguage = new(4, _attrs);
    private static readonly Attr _outputLanguage = new(4, _attrs);
    private static readonly Attr _minVersion = new(4, _attrs);
    private static readonly Attr _firstImageIndex = new(4, _attrs);

    private static readonly Attr _huffmanRecordOffset = new(4, _attrs);
    private static readonly Attr _huffmanRecordCount = new(4, _attrs);
    private static readonly Attr _huffmanTableOffset = new(4, _attrs);
    private static readonly Attr _huffmanTableLength = new(4, _attrs);

    private static readonly Attr _exthFlags = new(4, _attrs);

    private static readonly Attr _unknown1 = new(32, _attrs);
    private static readonly Attr _unknown2 = new(4, _attrs);

    private static readonly Attr _drmOffset = new(4, _attrs);
    private static readonly Attr _drmCount = new(4, _attrs);
    private static readonly Attr _drmSize = new(4, _attrs);
    private static readonly Attr _drmFlags = new(4, _attrs);
    private static readonly Attr _unknown3 = new(8, _attrs);

    private static readonly Attr _firstContentRecord = new(2, _attrs);
    private static readonly Attr _lastContentRecord = new(2, _attrs);

    private Memory<byte> _fullNameData;

    internal long PreviousHeaderPosition { get; set; }
    public bool SkipExthHeader { get; set; }
    public EXTHHead? ExthHeader { get; private set; }

    public MobiHead(bool skipProperties = false, bool skipRecords = false, bool skipExthHeader = false)
    {
        SkipProperties = skipProperties;
        SkipRecords = skipRecords;
        SkipExthHeader = skipExthHeader;

        if (!skipExthHeader)
        {
            SkipProperties = false;
        }
    }

    internal override void ResetReadState()
    {
        base.ResetReadState();
        _fullNameData = Memory<byte>.Empty;
        ExthHeader = null;
    }

    public override async Task ReadHeaderAsync(Stream stream)
    {
        ResetReadState();
        long headerOffset = stream.Position;
        await SkipOrReadHeaderDataAsync(stream, 8).ConfigureAwait(false);
        if (Identifier != "MOBI")
            throw new MobiMetadataException("Invalid MOBI header: expected 'MOBI' identifier");
        uint length = HeaderLength;
        if (length < 116) throw new MobiMetadataException("MOBI header is too short");
        ValidateRange(stream, headerOffset, length);
        var data = new byte[(int)length];
        _headerData.CopyTo(data);
        await ReadExactlyAsync(stream, data.AsMemory(8)).ConfigureAwait(false);
        _headerData = data;
        if (!SkipExthHeader && HasExthHeader)
        {
            var exth = new EXTHHead { RecordEndPosition = RecordEndPosition, TextEncoding = TextEncoding };
            await exth.ReadHeaderAsync(stream).ConfigureAwait(false);
            ExthHeader = exth;
        }
        if (!SkipProperties && FullNameLength != 0)
        {
            long position = PreviousHeaderPosition + FullNameOffset;
            ValidateRange(stream, position, FullNameLength);
            stream.Position = position;
            _fullNameData = new byte[(int)FullNameLength];
            await ReadExactlyAsync(stream, _fullNameData).ConfigureAwait(false);
        }
    }

    private uint OptionalUint(int offset, uint missing = uint.MaxValue) =>
        _headerData.Length >= offset + 4 ? Converter.ToUInt32(_headerData.Span.Slice(offset, 4)) : missing;

    public ushort ExtraDataFlags => _headerData.Length >= 0xe4
        ? Converter.ToUInt16(_headerData.Span.Slice(0xe2, 2)) : (ushort)0;
    public uint NcxIndex => OptionalUint(0xe4);
    public uint FragmentIndex => FileVersion == 8 ? OptionalUint(0xe8) : uint.MaxValue;
    public uint SkeletonIndex => FileVersion == 8 ? OptionalUint(0xec) : uint.MaxValue;
    public uint FdstCount => FileVersion == 8 ? OptionalUint(0xb4, 0) : 0;
    public uint FdstIndex => FdstCount > 1 ? OptionalUint(0xb0) : uint.MaxValue;

    public string Identifier => GetPropAsUtf8Trimmed(_identifier);
    public uint HeaderLength => GetPropAsUint(_headerLength);
    public uint MobiType => GetPropAsUint(_mobiType);
    public string MobiTypeName => MobiType switch
    {
        2 => "Mobipocket Book",
        3 => "PalmDoc Book",
        4 => "Audio",
        257 => "News",
        258 => "News Feed",
        259 => "News Magazine",
        513 => "PICS",
        514 => "WORD",
        515 => "XLS",
        516 => "PPT",
        517 => "TEXT",
        518 => "HTML",
        _ => "Unknown"
    };

    public uint TextEncoding => GetPropAsUint(_textEncoding);
    public string TextEncodingName => TextEncoding switch
    {
        1252 => "Windows-1252",
        65001 => "UTF-8",
        _ => "Unknown"
    };

    public uint UniqueId => GetPropAsUint(_uniqueId);
    public uint FileVersion => GetPropAsUint(_fileVersion);

    public string FullName => MobiEncoding.GetEncoding(TextEncoding).GetString(_fullNameData.Span);

    public uint OrthographicIndex => GetPropAsUint(_orthographicIndex);
    public uint InflectionIndex => GetPropAsUint(_inflectionIndex);
    public uint IndexNames => GetPropAsUint(_indexNames);
    public uint IndexKeys => GetPropAsUint(_indexKeys);
    public uint ExtraIndex0 => GetPropAsUint(_extraIndex0);
    public uint ExtraIndex1 => GetPropAsUint(_extraIndex1);
    public uint ExtraIndex2 => GetPropAsUint(_extraIndex2);
    public uint ExtraIndex3 => GetPropAsUint(_extraIndex3);
    public uint ExtraIndex4 => GetPropAsUint(_extraIndex4);
    public uint ExtraIndex5 => GetPropAsUint(_extraIndex5);
    public uint FirstNonBookIndex => GetPropAsUint(_firstNonBookIndex);
    public uint FullNameOffset => GetPropAsUint(_fullNameOffset);
    public uint FullNameLength => GetPropAsUint(_fullNameLength);

    public uint Locale => GetPropAsUint(_locale);
    public uint InputLanguage => GetPropAsUint(_inputLanguage);
    public uint OutputLanguage => GetPropAsUint(_outputLanguage);
    public uint MinVersion => GetPropAsUint(_minVersion);
    public uint FirstImageIndex => GetPropAsUint(_firstImageIndex);

    public uint HuffmanRecordOffset => GetPropAsUint(_huffmanRecordOffset);
    public uint HuffmanRecordCount => GetPropAsUint(_huffmanRecordCount);
    public uint HuffmanTableOffset => GetPropAsUint(_huffmanTableOffset);
    public uint HuffmanTableLength => GetPropAsUint(_huffmanTableLength);
    public bool HasHuffCdic => HuffmanRecordCount > 0;

    public ushort FirstContentRecord => _headerData.Length >= 178 ? GetPropAsUshort(_firstContentRecord) : (ushort)0;
    public ushort LastContentRecord => _headerData.Length >= 180 ? GetPropAsUshort(_lastContentRecord) : ushort.MaxValue;

    public uint DrmOffset => OptionalUint(_drmOffset.Position, 0);
    public uint DrmCount => OptionalUint(_drmCount.Position, 0);
    public uint DrmSize => OptionalUint(_drmSize.Position, 0);
    public uint DrmFlags => OptionalUint(_drmFlags.Position, 0);
    public bool HasDrm => DrmOffset != 0 && DrmCount != 0;

    public uint ExthFlags => GetPropAsUint(_exthFlags);
    public bool HasExthHeader => (ExthFlags & 0x40) != 0;
    public int ExthHeaderSize => ExthHeader?.Size ?? -1;
}
