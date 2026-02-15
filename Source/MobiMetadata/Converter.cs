using System.Buffers.Binary;

namespace MobiMetadata;

public static class Converter
{
    public static ushort ToUInt16(ReadOnlySpan<byte> bytes) =>
        BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt16(bytes))
            : BitConverter.ToUInt16(bytes);

    public static uint ToUInt32(ReadOnlySpan<byte> bytes) =>
        BitConverter.IsLittleEndian
            ? BinaryPrimitives.ReverseEndianness(BitConverter.ToUInt32(bytes))
            : BitConverter.ToUInt32(bytes);
}
