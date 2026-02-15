namespace MobiMetadata;

public sealed class EXTHRecord(Memory<byte> data, int position) : BaseRecord(data, position)
{
    private const int _typeOffset = 0;
    private const int _lengthOffset = 4;
    private const int _dataOffset = 8;

    private readonly int _dataLength = ValidateAndGetLength(data, position);

    private static int ValidateAndGetLength(Memory<byte> data, int position)
    {
        int length = (int)Converter.ToUInt32(data.Slice(position + _lengthOffset, 4).Span);
        if (length < 8)
            throw new MobiMetadataException("Invalid EXTH record: length < 8");
        return length - 8;
    }

    public uint RecordType => GetUint(_typeOffset, 4);
    public uint RecordLength => GetUint(_lengthOffset, 4);
    public int DataLength => _dataLength;
    public int TotalSize => _dataLength + 8;
    public Memory<byte> RecordData => GetData(_dataOffset, _dataLength);
}
