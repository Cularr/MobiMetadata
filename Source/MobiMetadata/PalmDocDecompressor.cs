namespace MobiMetadata;

public static class PalmDocDecompressor
{
    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        var inPos = 0;
        var outPos = 0;

        while (inPos < input.Length && outPos < output.Length)
        {
            var control = input[inPos++];

            if (control == 0x00)
            {
                output[outPos++] = 0x00;
            }
            else if (control <= 0x08)
            {
                var count = Math.Min(control, Math.Min(input.Length - inPos, output.Length - outPos));
                for (int i = 0; i < count; i++)
                {
                    output[outPos++] = input[inPos++];
                }
            }
            else if (control <= 0x7F)
            {
                output[outPos++] = control;
            }
            else if (control <= 0xBF)
            {
                if (inPos >= input.Length) break;

                var next = input[inPos++];
                var value = ((control & 0x3F) << 8) | next;
                var length = Math.Min((value >> 11) + 3, output.Length - outPos);
                var distance = (value & 0x7FF) + 1;

                var sourcePos = outPos - distance;
                if (sourcePos < 0) continue;

                for (int i = 0; i < length; i++)
                {
                    output[outPos++] = output[sourcePos + i];
                }
            }
            else
            {
                if (outPos + 1 < output.Length)
                {
                    output[outPos++] = 0x20;
                    output[outPos++] = (byte)(control ^ 0x80);
                }
                else if (outPos < output.Length)
                {
                    output[outPos++] = 0x20;
                }
            }
        }
    }

    public static byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        var output = new byte[outputLength];
        Decompress(input, output);
        return output;
    }

    public static async Task<byte[]> DecompressAsync(Memory<byte> input, int outputLength)
        => await Task.Run(() => Decompress(input.Span, outputLength)).ConfigureAwait(false);
}
