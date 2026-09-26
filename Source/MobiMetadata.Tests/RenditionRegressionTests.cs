using System.Buffers.Binary;
using Xunit;

namespace MobiMetadata.Tests;

public class RenditionRegressionTests
{
    [Fact]
    public async Task DualMobiSelectsSecondHeaderTextWithShortReads()
    {
        using var stream = new ShortReadStream(Book(
            Header(6, 6, 3), "legacy"u8.ToArray(), "BOUNDARY"u8.ToArray(),
            Header(8, 3), "KF8"u8.ToArray()));
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(stream);
        Assert.True(metadata.IsDualMobi);
        Assert.NotNull(metadata.Kf8Parser);
        Assert.True(metadata.Kf8Parser.IsKf8);
        Assert.Equal(3u, metadata.TextContent!.TextLength);
        Assert.Equal("KF8", await metadata.TextContent.ExtractTextAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public async Task MissingOrNullBoundaryIsNotDual(uint? boundary)
    {
        using var stream = new MemoryStream(Book(Header(6, 6, boundary), "legacy"u8.ToArray()));
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(stream);
        Assert.False(metadata.IsDualMobi);
        Assert.Null(metadata.Kf8Parser);
        Assert.Equal("legacy", await metadata.TextContent!.ExtractTextAsync());
    }

    [Theory]
    [InlineData("INVALID!", 8u, 3u)]
    [InlineData("BOUNDARY!", 8u, 3u)]
    [InlineData("BOUNDARY", 6u, 3u)]
    [InlineData("BOUNDARY", 8u, 99u)]
    [InlineData("BOUNDARY", 8u, 1u)]
    public async Task RejectsInvalidBoundaryAndSecondHeader(string marker, uint version, uint boundary)
    {
        using var stream = new MemoryStream(Book(Header(6, 6, boundary), "legacy"u8.ToArray(),
            System.Text.Encoding.ASCII.GetBytes(marker), Header(version, 3), "KF8"u8.ToArray()));
        using var metadata = new MobiMetadata();
        await Assert.ThrowsAsync<MobiMetadataException>(() => metadata.ReadMetadataAsync(stream));
        Assert.False(metadata.IsDualMobi);
        Assert.Null(metadata.TextContent);
        Assert.Null(metadata.Kf8Parser);
    }

    [Fact]
    public async Task RejectsEncryptionInTheSelectedRendition()
    {
        using var stream = new MemoryStream(Book(Header(6, 6, 3), "legacy"u8.ToArray(), "BOUNDARY"u8.ToArray(),
            Header(8, 3, encryption: 2), "KF8"u8.ToArray()));
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(stream);
        await Assert.ThrowsAsync<MobiMetadataException>(() => metadata.TextContent!.ExtractTextAsync());
    }

    [Fact]
    public async Task DualImageScanIncludesBothRenditionResourceRanges()
    {
        byte[] image = [0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4];
        byte[] secondImage = [0xff, 0xd8, 0xff, 0xe0, 5, 6, 7, 8];
        using var stream = new MemoryStream(Book(Header(6, 6, 4, firstImage: 2), "legacy"u8.ToArray(), image,
            "BOUNDARY"u8.ToArray(), Header(8, 3, firstImage: 2), "KF8"u8.ToArray(), secondImage));
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(stream);
        await metadata.LoadImagesAsync();
        Assert.Equal(2, metadata.Images.Count);
        Assert.Equal(image, await metadata.GetImageBytesAsync(0));
        Assert.Equal(secondImage, await metadata.GetImageBytesAsync(1));
    }

    [Fact]
    public async Task ReloadingOrdinaryMobiClearsDualSelection()
    {
        using var dual = new MemoryStream(Book(Header(6, 6, 3), "legacy"u8.ToArray(), "BOUNDARY"u8.ToArray(),
            Header(8, 3), "KF8"u8.ToArray()));
        using var ordinary = new MemoryStream(Book(Header(6, 6), "legacy"u8.ToArray()));
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(dual);
        await metadata.ReadMetadataAsync(ordinary);
        Assert.False(metadata.IsDualMobi);
        Assert.Null(metadata.Kf8Parser);
        Assert.Equal("legacy", await metadata.TextContent!.ExtractTextAsync());
    }

    public static IEnumerable<object[]> LocalBooks => LocalSampleFileTests.Azw3Files
        .Concat(LocalSampleFileTests.MobiFiles).Select(name => new object[] { name });

    [SkippableTheory]
    [MemberData(nameof(LocalBooks))]
    public async Task LocalTextMatchesDeclaredByteLengthWithoutArtificialNulls(string filename)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", filename);
        Skip.IfNot(File.Exists(path), "Optional local fixture is not installed");
        using var stream = File.OpenRead(path);
        using var metadata = new MobiMetadata();
        await metadata.ReadMetadataAsync(stream);
        var content = metadata.TextContent!;
        var text = await content.ExtractTextAsync();
        Assert.True(!text.Contains('\0'), $"{filename}: NUL count={text.Count(c => c == '\0')}, first={text.IndexOf('\0')}, chars={text.Length}");
        Assert.Equal("UTF-8", content.TextEncoding);
        Assert.Equal((long)content.TextLength, System.Text.Encoding.UTF8.GetByteCount(text));
        if (filename.Contains("Alices Adventures", StringComparison.Ordinal))
            Assert.Contains("Alice was beginning to get very tired", text);
    }

    private static byte[] Header(uint version, uint textLength, uint? boundary = null, ushort encryption = 0,
        uint firstImage = uint.MaxValue)
    {
        const int mobiLength = 264;
        var bytes = new byte[16 + mobiLength + (boundary.HasValue ? 24 : 0)];
        W16(bytes, 0, 1); W32(bytes, 4, textLength); W16(bytes, 8, 1); W16(bytes, 10, 4096); W16(bytes, 12, encryption);
        "MOBI"u8.CopyTo(bytes.AsSpan(16)); W32(bytes, 20, mobiLength); W32(bytes, 24, 2);
        W32(bytes, 28, 65001); W32(bytes, 36, version); W32(bytes, 108, firstImage);
        W32(bytes, 16 + 0xe4, uint.MaxValue); W32(bytes, 16 + 0xe8, uint.MaxValue); W32(bytes, 16 + 0xec, uint.MaxValue);
        if (boundary.HasValue)
        {
            W32(bytes, 128, 0x40);
            var start = 16 + mobiLength;
            "EXTH"u8.CopyTo(bytes.AsSpan(start)); W32(bytes, start + 4, 24); W32(bytes, start + 8, 1);
            W32(bytes, start + 12, 121); W32(bytes, start + 16, 12); W32(bytes, start + 20, boundary.Value);
        }
        return bytes;
    }

    private static byte[] Book(params byte[][] records)
    {
        var start = 78 + records.Length * 8 + 10; // Nonstandard directory padding is intentional.
        var bytes = new byte[start + records.Sum(record => record.Length)];
        "BOOKMOBI"u8.CopyTo(bytes.AsSpan(60)); W16(bytes, 76, (ushort)records.Length);
        for (var i = 0; i < records.Length; i++)
        {
            W32(bytes, 78 + i * 8, (uint)start);
            records[i].CopyTo(bytes, start);
            start += records[i].Length;
        }
        return bytes;
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
