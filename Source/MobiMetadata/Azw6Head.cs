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

    public override async Task ReadHeaderAsync(Stream stream)
    {
        await SkipOrReadHeaderDataAsync(stream, _attrs.Sum(a => a.Length)).ConfigureAwait(false);

        if (!SkipExthHeader)
        {
            ExthHeader = new EXTHHead();
            await ExthHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
        }

        await ReadTitleAsync(stream).ConfigureAwait(false);
    }

    private async Task ReadTitleAsync(Stream stream)
    {
        var headerSize = HeaderData.Length;
        if (ExthHeader != null)
            headerSize += (int)ExthHeader.HeaderLength;

        stream.Position += TitleOffset - headerSize;

        _titleData = new byte[(int)TitleLength];
        await stream.ReadAsync(_titleData).ConfigureAwait(false);
    }

    public string Identifier => GetPropAsUtf8Trimmed(_identifier);
    public uint HeaderLength => GetPropAsUint(_headerLength);
    public string Title => Encoding.UTF8.GetString(_titleData.Span);
    public uint TitleOffset => GetPropAsUint(_titleOffset);
    public uint TitleLength => GetPropAsUint(_titleLength);
    public uint Codepage => GetPropAsUint(_codepage);
    public uint RescCount => GetPropAsUint(_rescCount);
    public uint RescWithoutPlaceholders => GetPropAsUint(_rescWithoutPlaceholders);
}
