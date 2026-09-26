namespace MobiMetadata;

public class PalmDOCHead : BaseHead
{
    public const ushort CompressionNone = 1;
    public const ushort CompressionPalmDoc = 2;
    public const ushort CompressionHuffCdic = 17480;

    private static readonly List<Attr> _attrs = [];
    private static readonly Attr _compression = new(2, _attrs);
    private static readonly Attr _unused0 = new(2, _attrs);
    private static readonly Attr _textLength = new(4, _attrs);
    private static readonly Attr _recordCount = new(2, _attrs);
    private static readonly Attr _recordSize = new(2, _attrs);
    private static readonly Attr _encryptionType = new(2, _attrs);
    private static readonly Attr _unused1 = new(2, _attrs);

    public long StreamPosition { get; private set; }

    public PalmDOCHead(bool skipProperties = false, bool skipRecords = false)
    {
        SkipProperties = skipProperties;
        SkipRecords = skipRecords;
    }

    internal override void ResetReadState()
    {
        base.ResetReadState();
        StreamPosition = 0;
    }

    public override async Task ReadHeaderAsync(Stream stream)
    {
        ResetReadState();
        StreamPosition = stream.Position;
        await SkipOrReadHeaderDataAsync(stream, _attrs.Sum(a => a.Length)).ConfigureAwait(false);
    }

    public ushort Compression => GetPropAsUshort(_compression);
    public string CompressionName => Compression switch
    {
        CompressionNone => "None",
        CompressionPalmDoc => "PalmDOC",
        CompressionHuffCdic => "HUFF/CDIC",
        _ => "Unknown"
    };

    public uint TextLength => GetPropAsUint(_textLength);
    public ushort RecordCount => GetPropAsUshort(_recordCount);
    public ushort RecordSize => GetPropAsUshort(_recordSize);
    public ushort EncryptionType => GetPropAsUshort(_encryptionType);
    public string EncryptionTypeName => EncryptionType switch
    {
        0 => "None",
        1 => "Old Mobipocket",
        2 => "Mobipocket",
        _ => "Unknown"
    };
}
