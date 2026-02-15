namespace MobiMetadata;

public class BaseRecord(Memory<byte> data, int position)
{
    private readonly int _position = position;
    private readonly Memory<byte> _data = data;

    protected Memory<byte> GetData(int offset, int length) => _data.Slice(_position + offset, length);
    protected uint GetUint(int offset, int length) => Converter.ToUInt32(GetData(offset, length).Span);
}
