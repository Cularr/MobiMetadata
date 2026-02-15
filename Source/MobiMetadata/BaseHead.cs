using System.Text;

namespace MobiMetadata;

public abstract class BaseHead
{
    protected Memory<byte> _headerData;

    protected Memory<byte> HeaderData => _headerData;

    public FileInfo? Path { get; set; }

    public bool SkipProperties { get; set; }

    public bool SkipRecords { get; set; }

    public abstract Task ReadHeaderAsync(Stream stream);

    protected sealed class Attr
    {
        public int Position { get; }
        public int Length { get; }

        public Attr(int length, List<Attr>? attrs = null)
        {
            Position = attrs?.Sum(a => a.Length) ?? 0;
            Length = length;
            attrs?.Add(this);
        }

        public Memory<byte> GetData(Memory<byte> headerData) => headerData.Slice(Position, Length);
    }

    protected async Task SkipOrReadHeaderDataAsync(Stream stream, int length)
    {
        if (SkipProperties)
        {
            stream.Position += length;
        }
        else
        {
            _headerData = new byte[length];
            await stream.ReadAsync(_headerData).ConfigureAwait(false);
        }
    }

    protected Memory<byte> GetPropData(Attr attr) => attr.GetData(_headerData);
    protected string GetPropAsUtf8(Attr attr) => Encoding.UTF8.GetString(GetPropData(attr).Span);
    protected string GetPropAsUtf8Trimmed(Attr attr) => GetPropAsUtf8(attr).TrimEnd('\0');
    protected uint GetPropAsUint(Attr attr) => Converter.ToUInt32(GetPropData(attr).Span);
    protected ushort GetPropAsUshort(Attr attr) => Converter.ToUInt16(GetPropData(attr).Span);
}
