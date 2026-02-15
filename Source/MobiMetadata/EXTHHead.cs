using System.Text;

namespace MobiMetadata;

public class EXTHHead : BaseHead
{
    private static readonly List<Attr> _attrs = [];
    private static readonly Attr _identifier = new(4, _attrs);
    private static readonly Attr _headerLength = new(4, _attrs);
    private static readonly Attr _recordCount = new(4, _attrs);

    private EXTHRecord[] _records = [];

    public override async Task ReadHeaderAsync(Stream stream)
    {
        int attrLen = _attrs.Sum(a => a.Length);
        await SkipOrReadHeaderDataAsync(stream, attrLen).ConfigureAwait(false);

        if (Identifier != "EXTH")
        {
            throw new MobiMetadataException("Invalid EXTH header: expected 'EXTH' identifier");
        }

        byte[] recordsData = new byte[HeaderLength - attrLen];
        await stream.ReadAsync(recordsData).ConfigureAwait(false);

        _records = new EXTHRecord[RecordCount];
        int position = 0;

        for (int i = 0; i < RecordCount; i++)
        {
            EXTHRecord record = new EXTHRecord(recordsData, position);
            _records[i] = record;
            position += record.TotalSize;
        }
    }

    // Header properties
    public string Identifier => GetPropAsUtf8Trimmed(_identifier);
    public uint HeaderLength => GetPropAsUint(_headerLength);
    public uint RecordCount => GetPropAsUint(_recordCount);

    public int Size
    {
        get
        {
            int dataSize = _records.Sum(r => r.TotalSize);
            return 12 + dataSize + GetPadding(dataSize);
        }
    }

    private static int GetPadding(int dataSize)
    {
        int remainder = dataSize % 4;
        return remainder == 0 ? 0 : 4 - remainder;
    }

    // Record accessors
    private string GetString(uint type) => FindRecord(type) is { } r ? Encoding.UTF8.GetString(r.RecordData.Span) : "";
    private uint GetUint(uint type) => FindRecord(type) is { } r ? Converter.ToUInt32(r.RecordData.Span) : uint.MaxValue;
    private EXTHRecord? FindRecord(uint type) => Array.Find(_records, r => r.RecordType == type);

    // Standard metadata (type 100+)
    public string Author => GetString(100);
    public string Publisher => GetString(101);
    public string Imprint => GetString(102);
    public string Description => GetString(103);
    public string Isbn => GetString(104);
    public string Subject => GetString(105);
    public string PublishedDate => GetString(106);
    public string Review => GetString(107);
    public string Contributor => GetString(108);
    public string Rights => GetString(109);
    public string SubjectCode => GetString(110);
    public string Type => GetString(111);
    public string Source => GetString(112);
    public string Asin => GetString(113);
    public string VersionNumber => GetString(114);
    public uint Sample => GetUint(115);
    public uint StartOffset => GetUint(116);
    public string Adult => GetString(117);
    public string RetailPrice => GetString(118);
    public string RetailPriceCurrency => GetString(119);

    // KF8 specific (type 120+)
    public uint Kf8BoundaryOffset => GetUint(121);
    public string FixedLayout => GetString(122);
    public string BookType => GetString(123);
    public string OrientationLock => GetString(124);
    public uint ResourcesCount => GetUint(125);
    public string OriginalResolution => GetString(126);
    public string ZeroGutter => GetString(127);
    public string ZeroMargin => GetString(128);
    public string MastheadCoverImage => GetString(129);
    public uint RescOffset => GetUint(131);

    // Dictionary (type 200+)
    public string DictionaryShortName => GetString(200);

    // Images (type 200+)
    public uint CoverOffset => GetUint(201);
    public uint ThumbOffset => GetUint(202);
    public uint HasFakeCover => GetUint(203);

    // Creator info (type 200+)
    public uint CreatorSoftware => GetUint(204);
    public uint CreatorMajorVersion => GetUint(205);
    public uint CreatorMinorVersion => GetUint(206);
    public uint CreatorBuildNumber => GetUint(207);
    public string Watermark => GetString(208);
    public string TamperProofKeys => GetString(209);

    // Font (type 300+)
    public string FontSignature => GetString(300);

    // Restrictions (type 400+)
    public uint ClippingLimit => GetUint(401);
    public uint PublisherLimit => GetUint(402);
    public uint TextToSpeechDisabled => GetUint(404);
    public uint RentalIndicator => GetUint(406);

    // CDE/Update (type 500+)
    public string CdeType => GetString(501);
    public string LastUpdateTime => GetString(502);
    public string UpdatedTitle => GetString(503);
    public string Asin2 => GetString(504);

    // Japanese (type 508+)
    public string TitleKatakana => GetString(508);
    public string CreatorKatakana => GetString(517);
    public string PublisherKatakana => GetString(522);
    public string Language => GetString(524);

    // Layout (type 527+)
    public string PageProgressionDirection => GetString(527);
    public string OverrideKindleFonts => GetString(528);

    // Container (type 538+)
    public string InputSourceType => GetString(534);
    public string CreatorBuildString => GetString(535);
    public string ContainerResolution => GetString(538);
    public string ContainerMimeType => GetString(539);
    public string ContainerId => GetString(543);

    // DRM (type 1-3)
    public string DrmServerId => GetString(1);
    public string DrmCommerceId => GetString(2);
    public string DrmBookId => GetString(3);

    // Backward-compatible aliases
    public string ASIN => Asin;
    public string ASIN2 => Asin2;
    public string IBSN => Isbn;
    public string CDEType => CdeType;
    public uint K8ResourcesCount => ResourcesCount;
    public string K8MastheadCoverImage => MastheadCoverImage;
    public string WatermarkHexString => Watermark;
    public string TamperProofKeysHexString => TamperProofKeys;
    public string FontSignatureHexString => FontSignature;
    public string TitleKatagana => TitleKatakana;
    public string CreatorKatagana => CreatorKatakana;
    public string PublisherKatagana => PublisherKatakana;
}
