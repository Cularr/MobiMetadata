using System.Text;

namespace MobiMetadata;

public abstract class BaseHead
{
    protected Memory<byte> _headerData;

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

    internal long? RecordEndPosition { get; set; }
    internal virtual void ResetReadState() => _headerData = Memory<byte>.Empty;

    protected void ValidateRange(Stream stream, long position, long length, int maximumLength = 16 * 1024 * 1024)
    {
        if (!stream.CanRead || !stream.CanSeek)
            throw new MobiMetadataException("Header parsing requires a readable, seekable stream");
        long end = Math.Min(RecordEndPosition ?? stream.Length, stream.Length);
        if (position < 0 || length < 0 || length > maximumLength || position > end || length > end - position)
            throw new MobiMetadataException("Header data exceeds its enclosing record or size limit");
    }

    protected async Task ReadExactlyAsync(Stream stream, Memory<byte> destination)
    {
        ValidateRange(stream, stream.Position, destination.Length);
        try { await stream.ReadExactlyAsync(destination).ConfigureAwait(false); }
        catch (EndOfStreamException) { throw new MobiMetadataException("Truncated header data"); }
    }

    protected async Task SkipOrReadHeaderDataAsync(Stream stream, int length)
    {
        ValidateRange(stream, stream.Position, length);
        _headerData = new byte[length];
        await ReadExactlyAsync(stream, _headerData).ConfigureAwait(false);
    }

    protected Memory<byte> GetPropData(Attr attr) => attr.GetData(_headerData);
    protected string GetPropAsUtf8(Attr attr) => Encoding.UTF8.GetString(GetPropData(attr).Span);
    protected string GetPropAsUtf8Trimmed(Attr attr) => GetPropAsUtf8(attr).TrimEnd('\0');
    protected uint GetPropAsUint(Attr attr) => Converter.ToUInt32(GetPropData(attr).Span);
    protected ushort GetPropAsUshort(Attr attr) => Converter.ToUInt16(GetPropData(attr).Span);
}
