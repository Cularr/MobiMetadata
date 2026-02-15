namespace MobiMetadata;

public sealed class PDBRecordInfo(Memory<byte> data, int position) : BaseRecord(data, position)
{
    public const int RecordSize = 8;
    private const int _offsetPos = 0;

    public uint DataOffset => GetUint(_offsetPos, 4);
}
