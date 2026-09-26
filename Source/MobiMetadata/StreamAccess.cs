using System.Runtime.CompilerServices;

namespace MobiMetadata;

internal static class StreamAccess
{
    private static readonly ConditionalWeakTable<Stream, SemaphoreSlim> Locks = new();
    internal const int MaxRecordLength = 64 * 1024 * 1024;

    internal static SemaphoreSlim GetLock(Stream stream, SemaphoreSlim? preferred = null) =>
        Locks.GetValue(stream, _ => preferred ?? new SemaphoreSlim(1, 1));

    internal static void ValidateRange(Stream stream, long offset, long length)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
            throw new ArgumentException("A readable, seekable stream is required.", nameof(stream));
        if (offset < 0 || length < 0 || offset > stream.Length || length > stream.Length - offset)
            throw new MobiMetadataException("Record range is outside the stream.");
    }

    internal static async Task ReadExactlyAtAsync(Stream stream, long offset, Memory<byte> buffer)
    {
        var gate = GetLock(stream);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ValidateRange(stream, offset, buffer.Length);
            stream.Position = offset;
            await stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    internal static async Task<byte[]> ReadRecordAsync(Stream stream, PDBRecordInfo[] records, int index,
        int maxLength = MaxRecordLength)
    {
        if ((uint)index >= records.Length)
            throw new MobiMetadataException("Record index is outside the directory.");
        long start = records[index].DataOffset;
        long end = index + 1 < records.Length ? records[index + 1].DataOffset : stream.Length;
        ValidateRange(stream, start, end - start);
        if (end - start > maxLength)
            throw new MobiMetadataException("Record exceeds the supported allocation limit.");
        var data = new byte[(int)(end - start)];
        await ReadExactlyAtAsync(stream, start, data).ConfigureAwait(false);
        return data;
    }
}
