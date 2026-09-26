using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace MobiMetadata.Tests;

public class ResourceRegressionTests
{
    [Fact]
    public async Task CoverOffsetZeroSelectsFirstResource()
    {
        byte[] cover = Jpeg(1), page = Jpeg(2);
        using var stream = new MemoryStream(Book(true, cover, page));
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(stream);
        await book.LoadImagesAsync();
        Assert.True(book.HasSdCover());
        Assert.Equal(cover, await book.GetCoverBytesAsync());
        Assert.Single(book.Images);
        Assert.Equal(page, await book.GetImageBytesAsync(0));
    }

    [Fact]
    public async Task ImagesWithoutExthExcludeUnknownAndStructuralRecords()
    {
        var image = Jpeg(3);
        using var stream = new MemoryStream(Book(false, image, "BOUNDARY"u8.ToArray(), "unknown resource"u8.ToArray()));
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(stream);
        await book.LoadImagesAsync();
        Assert.Null(book.MobiHeader.ExthHeader);
        Assert.Single(book.Images);
        Assert.Equal(image, await book.GetImageBytesAsync(0));
        Assert.Equal(2, book.PageRecords!.Rest.Count);
    }

    [Fact]
    public async Task ReloadAndFailedReadClearPreviousBookState()
    {
        using var first = new MemoryStream(Book(true, Jpeg(1), Jpeg(2)));
        using var second = new MemoryStream(Book(false, Jpeg(3)));
        using var invalid = new MemoryStream(new byte[4]);
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(first);
        await book.LoadImagesAsync();
        Assert.NotNull(book.Cover);
        await book.ReadMetadataAsync(second);
        Assert.Null(book.MobiHeader.ExthHeader);
        Assert.Null(book.Cover);
        Assert.Null(book.PageRecords);
        Assert.Empty(book.Images);
        await book.LoadImagesAsync();
        Assert.Equal(Jpeg(3), await book.GetImageBytesAsync(0));
        await Assert.ThrowsAsync<MobiMetadataException>(() => book.ReadMetadataAsync(invalid));
        Assert.Null(book.TextContent);
        Assert.Null(book.Kf8Parser);
        Assert.Null(book.Navigation);
        Assert.Null(book.PageRecords);
        Assert.Null(book.HdContainer);
        Assert.Null(book.Cover);
        Assert.Empty(book.Images);
        await Assert.ThrowsAsync<MobiMetadataException>(() => book.LoadImagesAsync());
        await book.ReadMetadataAsync(second);
        await book.LoadImagesAsync();
        Assert.Equal(Jpeg(3), await book.GetImageBytesAsync(0));
    }

    [Fact]
    public async Task HdMatchingRetainsResourcePositionsAcrossFontsAndPlaceholders()
    {
        var sdCover = Jpeg(1); var sdFirst = Jpeg(2); var sdSecond = Jpeg(3);
        var hdCover = Jpeg(11); var hdSecond = Jpeg(13);
        using var sd = new MemoryStream(Book(true, sdCover, "FONT"u8.ToArray(), sdFirst, sdSecond));
        using var hd = new MemoryStream(HdBook(Cres(hdCover), "FONT"u8.ToArray(), "CRES"u8.ToArray(), Cres(hdSecond)));
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(sd);
        await book.LoadImagesAsync(hd);
        Assert.Single(book.PageRecords!.Fonts);
        Assert.Equal(2, book.Images.Count);
        Assert.False(book.IsHdImage(0));
        Assert.True(book.IsHdImage(1));
        Assert.Equal(sdFirst, await book.GetImageBytesAsync(0));
        Assert.Equal(hdSecond, await book.GetImageBytesAsync(1));
        using (var image = book.OpenImageStream(1)) Assert.Equal(hdSecond, await ReadAll(image));
        Assert.Equal(hdCover, await book.GetCoverBytesAsync());
        using (var cover = book.OpenCoverStream()) Assert.Equal(hdCover, await ReadAll(cover!));
        await book.LoadImagesAsync();
        Assert.Null(book.HdContainer);
        Assert.Null(book.Azw6Header);
        Assert.False(book.IsHdImage(1));
        Assert.False(book.HasHdCover());
        Assert.Equal(sdSecond, await book.GetImageBytesAsync(1));
        Assert.Equal(sdCover, await book.GetCoverBytesAsync());
    }

    [Fact]
    public async Task SubstreamAndPageRecordSerializeSharedSeekAndRead()
    {
        var firstBytes = Jpeg(21); var secondBytes = Jpeg(22);
        using var stream = new PausedReadStream(firstBytes.Concat(secondBytes).ToArray());
        var first = new PageRecord(stream, 0, (uint)firstBytes.Length);
        // Distinct PageRecord instances must share the same stream coordinator.
        var second = new PageRecord(stream, firstBytes.Length, (uint)secondBytes.Length);
        using var substream = first.OpenImageStream();
        var firstRead = ReadAll(substream);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<byte[]> secondRead;
        try { secondRead = second.GetImageBytesAsync(); }
        finally { stream.Resume.TrySetResult(); }
        await Task.WhenAll(firstRead, secondRead).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(firstBytes, await firstRead);
        Assert.Equal(secondBytes, await secondRead);
    }

    [Fact]
    public async Task HdJpegXrPayloadIsPreservedWithoutDecoding()
    {
        byte[] jxr = [0x49, 0x49, 0xbc, 0x01, 8, 0, 0, 0];
        using var sd = new MemoryStream(Book(false, Jpeg(1)));
        using var hd = new MemoryStream(HdBook(Cres(jxr)));
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(sd);
        await book.LoadImagesAsync(hd);
        Assert.True(book.IsHdImage(0));
        Assert.Equal(jxr, await book.GetImageBytesAsync(0));
    }

    [Fact]
    public async Task RescSpineCountDoesNotTruncateImages()
    {
        var resc = Encoding.UTF8.GetBytes("RESC<opf:spine xmlns:opf='urn:test'><opf:itemref idref='chapter' skelid='0'/></opf:spine>");
        using var stream = new MemoryStream(Book(false, Jpeg(1), Jpeg(2), resc));
        using var book = new MobiMetadata();
        await book.ReadMetadataAsync(stream);
        await book.LoadImagesAsync();
        Assert.Equal(2, book.Images.Count);
        Assert.Equal(1, book.PageRecords!.Resc!.PageCount);
    }

    private static byte[] Book(bool cover, params byte[][] resources)
    {
        int exthLength = cover ? 24 : 0;
        var header = new byte[16 + 180 + exthLength + 4];
        BinaryPrimitives.WriteUInt16BigEndian(header, 1);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(10), 4096);
        "MOBI"u8.CopyTo(header.AsSpan(16)); Put(header, 20, 180);
        Put(header, 28, 65001); Put(header, 36, 6);
        Put(header, 84, (uint)(196 + exthLength)); Put(header, 88, 4);
        Put(header, 108, 1);
        if (cover)
        {
            Put(header, 128, 0x40);
            "EXTH"u8.CopyTo(header.AsSpan(196)); Put(header, 200, 24); Put(header, 204, 1);
            Put(header, 208, 201); Put(header, 212, 12); Put(header, 216, 0);
        }
        "Book"u8.CopyTo(header.AsSpan(196 + exthLength));
        return Pdb("BOOK", "MOBI", new[] { header }.Concat(resources).ToArray());
    }

    private static byte[] HdBook(params byte[][] resources)
    {
        var header = new byte[52];
        "CONT"u8.CopyTo(header); Put(header, 4, 52); Put(header, 12, 65001);
        Put(header, 40, 48); Put(header, 44, 4); "Book"u8.CopyTo(header.AsSpan(48));
        return Pdb("RBIN", "CONT", new[] { header }.Concat(resources).ToArray());
    }

    private static byte[] Pdb(string type, string creator, byte[][] records)
    {
        int start = 80 + records.Length * 8;
        var bytes = new byte[start + records.Sum(r => r.Length)];
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 60); Encoding.ASCII.GetBytes(creator).CopyTo(bytes, 64);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76), (ushort)records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            Put(bytes, 78 + i * 8, (uint)start); records[i].CopyTo(bytes, start); start += records[i].Length;
        }
        return bytes;
    }
    private static byte[] Jpeg(byte marker) => [0xff, 0xd8, 0xff, marker, marker, marker, 0xff, 0xd9];
    private static byte[] Cres(byte[] payload)
    {
        var result = new byte[12 + payload.Length]; "CRES"u8.CopyTo(result); payload.CopyTo(result, 12); return result;
    }
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private static async Task<byte[]> ReadAll(Stream stream)
    {
        using var output = new MemoryStream(); await stream.CopyToAsync(output); return output.ToArray();
    }
    private sealed class PausedReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        private int _reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(cancellationToken);
            }
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
