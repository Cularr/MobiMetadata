using System.Buffers.Binary;
using Xunit;

namespace MobiMetadata.Tests;

public class InlineContainerRegressionTests
{
    [Fact]
    public async Task ExtractsInlineCoverAndImagesAcrossBoundary()
    {
        byte[] cover = [0xff, 0xd8, 0xff, 1], image = [0xff, 0xd8, 0xff, 2];
        var (stream, records) = Records(Container("application/image"), Cres(cover),
            new byte[] { 0xa0, 0xa0, 0xa0, 0xa0 }, Cres(image), "CONTBOUNDARY"u8.ToArray(), image);
        using (stream)
        {
            var pages = new PageRecords(stream, records, 0, 0, ushort.MaxValue, 1, uint.MaxValue);
            await pages.AnalyzeAsync();
            Assert.Equal(cover, await pages.GetCoverBytesAsync());
            Assert.Equal(2, pages.Images.Count);
            Assert.Equal(image, await pages.GetImageBytesAsync(0));
            using var payload = pages.OpenImageStream(0);
            using var copy = new MemoryStream();
            await payload.CopyToAsync(copy);
            Assert.Equal(image, copy.ToArray());
            Assert.Equal(image, await pages.GetImageBytesAsync(1));
            await pages.AnalyzeAsync();
            Assert.Equal(cover, await pages.GetCoverBytesAsync());
            Assert.Equal(2, pages.Images.Count);
        }
    }

    [Fact]
    public async Task DoesNotInterpretOtherContainersOrOrphanCresAsImages()
    {
        byte[] image = [0xff, 0xd8, 0xff, 2];
        var (stream, records) = Records(Cres(image), Container("application/font"), Cres(image), image,
            "CONTBOUNDARY"u8.ToArray(), image);
        using (stream)
        {
            var pages = new PageRecords(stream, records, 0, 0, ushort.MaxValue, uint.MaxValue, uint.MaxValue);
            await pages.AnalyzeAsync();
            Assert.Single(pages.Images);
            Assert.Equal(image, await pages.GetImageBytesAsync(0));
        }
    }

    [Fact]
    public async Task RejectsContainerMetadataBeyondRecord()
    {
        var container = Container("application/image");
        BinaryPrimitives.WriteUInt32BigEndian(container.AsSpan(52), uint.MaxValue);
        var (stream, records) = Records(container);
        using (stream)
        {
            var pages = new PageRecords(stream, records, 0, 0, ushort.MaxValue, uint.MaxValue, uint.MaxValue);
            await Assert.ThrowsAsync<MobiMetadataException>(() => pages.AnalyzeAsync());
        }
    }

    private static byte[] Container(string mime)
    {
        var value = System.Text.Encoding.ASCII.GetBytes(mime);
        var bytes = new byte[68 + value.Length];
        "CONT"u8.CopyTo(bytes);
        "EXTH"u8.CopyTo(bytes.AsSpan(48));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(52), (uint)(20 + value.Length));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(60), 539);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(64), (uint)(8 + value.Length));
        value.CopyTo(bytes, 68);
        return bytes;
    }

    private static byte[] Cres(byte[] payload)
    {
        var bytes = new byte[12 + payload.Length];
        "CRES"u8.CopyTo(bytes); payload.CopyTo(bytes, 12); return bytes;
    }

    private static (MemoryStream, PDBRecordInfo[]) Records(params byte[][] records)
    {
        var directory = new byte[records.Length * 8];
        int offset = 0;
        for (int i = 0; i < records.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(directory.AsSpan(i * 8), (uint)offset);
            offset += records[i].Length;
        }
        return (new MemoryStream(records.SelectMany(r => r).ToArray()),
            Enumerable.Range(0, records.Length).Select(i => new PDBRecordInfo(directory, i * 8)).ToArray());
    }
}
