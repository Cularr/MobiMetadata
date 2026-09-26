using System.Text;

namespace MobiMetadata;

public class Azw6Head : BaseHead
{
    private static readonly List<Attr> _attrs = [];
    private static readonly Attr _identifier = new(4, _attrs);
    private static readonly Attr _headerLength = new(4, _attrs);
    private static readonly Attr _type = new(2, _attrs);
    private static readonly Attr _count = new(2, _attrs);
    private static readonly Attr _codepage = new(4, _attrs);
    private static readonly Attr _unknown0 = new(4, _attrs);
    private static readonly Attr _unknown1 = new(4, _attrs);
    private static readonly Attr _rescCount = new(4, _attrs);
    private static readonly Attr _rescWithoutPlaceholders = new(4, _attrs);
    private static readonly Attr _hrefOffset = new(4, _attrs);
    private static readonly Attr _unknown2 = new(4, _attrs);
    private static readonly Attr _titleOffset = new(4, _attrs);
    private static readonly Attr _titleLength = new(4, _attrs);

    private Memory<byte> _titleData;

    public bool SkipExthHeader { get; set; }
    public EXTHHead? ExthHeader { get; private set; }

    public Azw6Head(bool skipProperties = false, bool skipRecords = false, bool skipExthHeader = false)
    {
        SkipProperties = skipProperties;
        SkipRecords = skipRecords;
        SkipExthHeader = skipExthHeader;
    }

    internal override void ResetReadState()
    {
        base.ResetReadState();
        _titleData = Memory<byte>.Empty;
        ExthHeader = null;
    }

    public override async Task ReadHeaderAsync(Stream stream)
    {
        ResetReadState();
        long start = stream.Position;
        await SkipOrReadHeaderDataAsync(stream, 48).ConfigureAwait(false);
        if (Identifier != "CONT") throw new MobiMetadataException("Invalid AZW6 header identifier");
        if (HeaderLength < 48) throw new MobiMetadataException("Invalid AZW6 header length");
        ValidateRange(stream, start, HeaderLength);
        // CONT uses a fixed 48-byte prefix; its length field is the record size.
        if (!SkipExthHeader)
        {
            var exth = new EXTHHead { RecordEndPosition = RecordEndPosition, TextEncoding = Codepage };
            await exth.ReadHeaderAsync(stream).ConfigureAwait(false);
            ExthHeader = exth;
        }
        if (!SkipProperties && TitleLength != 0)
        {
            ValidateRange(stream, start + TitleOffset, TitleLength);
            stream.Position = start + TitleOffset;
            _titleData = new byte[(int)TitleLength];
            await ReadExactlyAsync(stream, _titleData).ConfigureAwait(false);
        }
    }

    public string Identifier => GetPropAsUtf8Trimmed(_identifier);
    public uint HeaderLength => GetPropAsUint(_headerLength);
    public string Title => MobiEncoding.GetEncoding(Codepage).GetString(_titleData.Span);
    public uint TitleOffset => GetPropAsUint(_titleOffset);
    public uint TitleLength => GetPropAsUint(_titleLength);
    public uint Codepage => GetPropAsUint(_codepage);
    public uint RescCount => GetPropAsUint(_rescCount);
    public uint RescWithoutPlaceholders => GetPropAsUint(_rescWithoutPlaceholders);
}
