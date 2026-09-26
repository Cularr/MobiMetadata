namespace MobiMetadata;

public static class PalmDocDecompressor
{
    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
        => Decode(input, output);

    private static int Decode(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var written = 0;
        for (var cursor = 0; cursor < input.Length;)
        {
            var token = input[cursor++];
            if (token is >= 1 and <= 8)
            {
                if (token > input.Length - cursor || token > output.Length - written)
                    throw new MobiMetadataException("Invalid PalmDOC literal run or output limit exceeded");
                input.Slice(cursor, token).CopyTo(output[written..]);
                cursor += token;
                written += token;
            }
            else if (token is >= 0x80 and <= 0xbf)
            {
                if (cursor == input.Length)
                    throw new MobiMetadataException("Truncated PalmDOC back-reference");
                var packed = ((token & 0x3f) << 8) | input[cursor++];
                var distance = packed >> 3;
                var count = (packed & 7) + 3;
                if (distance == 0 || distance > written || count > output.Length - written)
                    throw new MobiMetadataException("Invalid PalmDOC back-reference or output limit exceeded");
                for (var i = 0; i < count; i++)
                {
                    output[written] = output[written - distance];
                    written++;
                }
            }
            else
            {
                var count = token >= 0xc0 ? 2 : 1;
                if (count > output.Length - written)
                    throw new MobiMetadataException("PalmDOC output limit exceeded");
                if (count == 2) output[written++] = 0x20;
                output[written++] = count == 2 ? (byte)(token ^ 0x80) : token;
            }
        }
        return written;
    }

    public static byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        if (outputLength < 0 || outputLength > 64 * 1024 * 1024)
            throw new MobiMetadataException("PalmDOC output limit exceeded");
        var output = new byte[outputLength];
        var written = Decode(input, output);
        return written == output.Length ? output : output.AsSpan(0, written).ToArray();
    }

    public static Task<byte[]> DecompressAsync(Memory<byte> input, int outputLength)
        => Task.Run(() => Decompress(input.Span, outputLength));
}
