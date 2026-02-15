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

    public override async Task ReadHeaderAsync(Stream stream)
    {
        var headerOffset = stream.Position;

        var headerLength = _attrs.Sum(a => a.Length);
        _headerData = new byte[headerLength];
        await stream.ReadAsync(_headerData).ConfigureAwait(false);

        if (Identifier != "MOBI")
        {
            throw new MobiMetadataException("Invalid MOBI header: expected 'MOBI' identifier");
        }

        if (!SkipExthHeader)
        {
            await ReadExthHeaderAsync(stream, headerOffset).ConfigureAwait(false);
        }

        if (!SkipProperties)
        {
            await ReadFullNameAsync(stream).ConfigureAwait(false);
        }
    }

    private async Task ReadExthHeaderAsync(Stream stream, long headerOffset)
    {
        if (!HasExthHeader) return;

        stream.Position = headerOffset + HeaderLength;
        ExthHeader = new EXTHHead();
        await ExthHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
    }

    private async Task ReadFullNameAsync(Stream stream)
    {
        stream.Position = PreviousHeaderPosition + FullNameOffset;
        _fullNameData = new byte[(int)FullNameLength];
        await stream.ReadAsync(_fullNameData).ConfigureAwait(false);
    }

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

    public string FullName => Encoding.UTF8.GetString(_fullNameData.Span);

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

    public ushort FirstContentRecord => GetPropAsUshort(_firstContentRecord);
    public ushort LastContentRecord => GetPropAsUshort(_lastContentRecord);

    public uint DrmOffset => GetPropAsUint(_drmOffset);
    public uint DrmCount => GetPropAsUint(_drmCount);
    public uint DrmSize => GetPropAsUint(_drmSize);
    public uint DrmFlags => GetPropAsUint(_drmFlags);
    public bool HasDrm => DrmOffset != 0 && DrmCount != 0;

    public uint ExthFlags => GetPropAsUint(_exthFlags);
    public bool HasExthHeader => (ExthFlags & 0x40) != 0;
    public int ExthHeaderSize => ExthHeader?.Size ?? -1;
}
