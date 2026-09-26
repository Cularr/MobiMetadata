using System.Buffers.Binary;
using Xunit;

namespace MobiMetadata.Tests;

public class PageRecordBoundaryTests
{
    [Theory]
    [InlineData(4, 2)]
    [InlineData(3, 2)]
    [InlineData(3, ushort.MaxValue)]
    public async Task should_include_the_last_image_record(int recordCount, ushort lastContentRecord)
    {
        // Distinct JPEG-signature payloads; range selection needs no image decoding.
        var bytes = CreateImageBytes(recordCount);
        using var stream = new MemoryStream(bytes);
        var records = CreateRecords(recordCount);
        var pages = new PageRecords(stream, records, 0, 0, lastContentRecord, uint.MaxValue, uint.MaxValue);

        await pages.AnalyzeAsync();

        Assert.Equal(3, pages.Images.Count);
        for (var i = 0; i < 3; i++)
            Assert.Equal(bytes.Skip(i * 8).Take(8).ToArray(), await pages.GetImageBytesAsync(i));
    }

    [Fact]
    public async Task should_handle_an_empty_record_table()
    {
        using var stream = new MemoryStream();
        var pages = new PageRecords(stream, [], 0, 0, ushort.MaxValue, uint.MaxValue, uint.MaxValue);

        await pages.AnalyzeAsync();

        Assert.Empty(pages.Images);
    }

    [Fact]
    public void should_reject_records_outside_the_stream()
    {
        using var stream = new MemoryStream(new byte[9]);
        var records = CreateRecords(3);

        Assert.Throws<MobiMetadataException>(() => new PageRecords(stream, records, 0, 0, 2, uint.MaxValue, uint.MaxValue));
    }

    [Theory]
    [InlineData("FDST")]
    [InlineData("FLIS")]
    [InlineData("FCIS")]
    public async Task should_not_count_trailing_structural_records_as_images(string signature)
    {
        var bytes = CreateImageBytes(4);
        System.Text.Encoding.ASCII.GetBytes(signature).CopyTo(bytes, 24);
        using var stream = new MemoryStream(bytes);
        var pages = new PageRecords(stream, CreateRecords(4), 0, 0, 3, uint.MaxValue, uint.MaxValue);

        await pages.AnalyzeAsync();

        Assert.Equal(3, pages.Images.Count);
        Assert.Single(pages.Rest);
        Assert.Equal(bytes.Skip(16).Take(8).ToArray(), await pages.GetImageBytesAsync(2));
    }

    private static PDBRecordInfo[] CreateRecords(int count)
    {
        var table = new byte[count * 8];
        for (var i = 0; i < count; i++)
            BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(i * 8, 4), (uint)(i * 8));
        return Enumerable.Range(0, count).Select(i => new PDBRecordInfo(table, i * 8)).ToArray();
    }

    private static byte[] CreateImageBytes(int count)
    {
        var bytes = Enumerable.Range(0, count * 8).Select(i => (byte)i).ToArray();
        for (int i = 0; i < count; i++)
            new byte[] { 0xff, 0xd8, 0xff }.CopyTo(bytes, i * 8);
        return bytes;
    }
}
