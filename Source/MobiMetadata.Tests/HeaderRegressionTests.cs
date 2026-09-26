using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace MobiMetadata.Tests;

public class HeaderRegressionTests
{
    [Fact]
    public async Task PdbUsesDeclaredFirstRecordWithShortReads()
    {
        var bytes = new byte[110];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76), 2);
        Put(bytes, 78, 104);
        Put(bytes, 86, 108);
        using var stream = new ShortReadStream(bytes);
        var header = new PDBHead();
        await header.ReadHeaderAsync(stream);
        Assert.Equal(104, stream.Position);
        Assert.Equal(2, header.Records.Length);
    }

    [Fact]
    public async Task PdbRejectsDescendingRecordOffsets()
    {
        var bytes = new byte[110];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(76), 2);
        Put(bytes, 78, 108);
        Put(bytes, 86, 104);
        await Assert.ThrowsAsync<MobiMetadataException>(() => new PDBHead().ReadHeaderAsync(new MemoryStream(bytes)));
    }

    [Theory]
    [InlineData(12u, 100000u)]
    [InlineData(uint.MaxValue, 1u)]
    [InlineData(11u, 0u)]
    public async Task ExthRejectsImpossibleSizes(uint length, uint count)
    {
        var bytes = new byte[12];
        "EXTH"u8.CopyTo(bytes);
        Put(bytes, 4, length);
        Put(bytes, 8, count);
        await Assert.ThrowsAsync<MobiMetadataException>(() => new EXTHHead().ReadHeaderAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task ExthPreservesRepeatedLegacyMetadata()
    {
        var bytes = new byte[30];
        "EXTH"u8.CopyTo(bytes);
        Put(bytes, 4, 30); Put(bytes, 8, 2);
        Put(bytes, 12, 100); Put(bytes, 16, 9); bytes[20] = 0x80;
        Put(bytes, 21, 100); Put(bytes, 25, 9); bytes[29] = 0xe9;
        var exth = new EXTHHead { TextEncoding = 1252 };
        await exth.ReadHeaderAsync(new ShortReadStream(bytes));
        Assert.Equal(new[] { "€", "é" }, exth.Authors);
        Assert.Equal("€", exth.Author);
        Assert.Equal(uint.MaxValue, exth.Kf8BoundaryOffset);
        Assert.Equal(2, exth.Records.Count);
    }

    [Fact]
    public void ExthRejectsTruncatedRecordPayload()
    {
        var bytes = new byte[8]; Put(bytes, 4, 9);
        Assert.Throws<MobiMetadataException>(() => new EXTHRecord(bytes, 0));
    }

    [Fact]
    public async Task MobiReadsExtendedFieldsAndLegacyTitle()
    {
        var bytes = Mobi(264);
        Put(bytes, 12, 1252); Put(bytes, 20, 8);
        Put(bytes, 68, 264); Put(bytes, 72, 1);
        Put(bytes, 0xb0, 12); Put(bytes, 0xb4, 2);
        Put(bytes, 0xe4, 4); Put(bytes, 0xe8, 5); Put(bytes, 0xec, 6);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0xe2), 3);
        Array.Resize(ref bytes, 265); bytes[264] = 0x80;
        var header = new MobiHead();
        await header.ReadHeaderAsync(new ShortReadStream(bytes));
        Assert.Equal("€", header.FullName);
        Assert.Equal(12u, header.FdstIndex);
        Assert.Equal(4u, header.NcxIndex);
        Assert.Equal(5u, header.FragmentIndex);
        Assert.Equal(6u, header.SkeletonIndex);
        Assert.Equal(3, header.ExtraDataFlags);
    }

    [Fact]
    public async Task MobiRejectsTitleBeyondStream()
    {
        var bytes = Mobi(180); Put(bytes, 68, 500); Put(bytes, 72, 16);
        await Assert.ThrowsAsync<MobiMetadataException>(() => new MobiHead().ReadHeaderAsync(new MemoryStream(bytes)));
    }

    [Fact]
    public async Task MobiReloadClearsExth()
    {
        var bytes = Mobi(180); Array.Resize(ref bytes, 192);
        Put(bytes, 112, 0x40); "EXTH"u8.CopyTo(bytes.AsSpan(180)); Put(bytes, 184, 12);
        var header = new MobiHead();
        await header.ReadHeaderAsync(new MemoryStream(bytes));
        Assert.NotNull(header.ExthHeader);
        await header.ReadHeaderAsync(new MemoryStream(Mobi(180)));
        Assert.Null(header.ExthHeader);
    }

    [Fact]
    public async Task Azw6UsesFixedPrefixAndRelativeTitle()
    {
        var bytes = new byte[61]; "CONT"u8.CopyTo(bytes); Put(bytes, 4, 61);
        Put(bytes, 12, 1252); Put(bytes, 40, 60); Put(bytes, 44, 1);
        "EXTH"u8.CopyTo(bytes.AsSpan(48)); Put(bytes, 52, 12); bytes[60] = 0x80;
        var header = new Azw6Head();
        await header.ReadHeaderAsync(new ShortReadStream(bytes));
        Assert.Equal("€", header.Title);
    }

    private static byte[] Mobi(int length)
    {
        var bytes = new byte[length]; "MOBI"u8.CopyTo(bytes);
        Put(bytes, 4, (uint)length); Put(bytes, 12, 65001); Put(bytes, 20, 6);
        return bytes;
    }
    private static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
}
