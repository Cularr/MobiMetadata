using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace MobiMetadata.Tests;

public class Kf8IndexRegressionTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task ReadsNcxTagsAndCncxWithRenditionBase()
    {
        var meta = Header(224, 1); Put(meta, 52, 1);
        Encoding.ASCII.GetBytes("TAGX").CopyTo(meta, 192); Put(meta, 196, 24); Put(meta, 200, 1);
        new byte[] { 1, 1, 1, 0, 3, 1, 2, 0, 4, 1, 4, 0 }.CopyTo(meta, 204);
        
        // Include byte-count encoded tag 2 and a terminating control descriptor.
        new byte[] { 2, 1, 24, 0, 0, 0, 0, 1 }.CopyTo(meta, 216); Put(meta, 196, 32);
        var entry = Header(210, 1); Put(entry, 20, 204);
        new byte[] { 1, (byte)'0', 31, 0x81, 0xAA, 0x80, 0x81, 0x85 }.CopyTo(entry, 192);
        Encoding.ASCII.GetBytes("IDXT").CopyTo(entry, 204); BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(208), 192);
        var (stream, records) = Container(new byte[8], new byte[8], meta, entry, new byte[] { 0x85, (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' });
        using (stream)
        {
            var head = await MobiHeader(1);
            var parser = new KF8Parser(stream, records, head, null, 1);
            await parser.ParseAsync();
            var item = Assert.Single(parser.Ncx!);
            Assert.Equal("Hello", item.Title); Assert.Equal(42u, item.Offset); Assert.Equal(1u, item.Level); Assert.Equal(5u, item.Length);
        }
    }
    [Fact]
    public async Task RejectsHugeEntryCountBeforeAllocating()
    {
        var meta = Header(208, 1); Encoding.ASCII.GetBytes("TAGX").CopyTo(meta, 192); Put(meta, 196, 16); Put(meta, 200, 1);
        new byte[] { 1, 1, 1, 0 }.CopyTo(meta, 204);
        var entry = Header(196, uint.MaxValue); Put(entry, 20, 192); Encoding.ASCII.GetBytes("IDXT").CopyTo(entry, 192);
        var (stream, records) = Container(new byte[8], meta, entry);
        using (stream) await Assert.ThrowsAsync<MobiMetadataException>(() => new KF8Parser(stream, records, MobiHeader(1).GetAwaiter().GetResult(), null).ParseAsync());
    }
    [Fact]
    public void HierarchyRebuildIsIdempotentAndFlattenHasNoDuplicates()
    {
        NcxEntry[] entries = [new() { Level = 0 }, new() { Level = 1 }, new() { Level = 2 }, new() { Level = 0 }, new() { Level = 2 }];
        var navigation = new NcxNavigation(entries);
        navigation.BuildHierarchy(); var roots = navigation.BuildHierarchy();
        Assert.Equal(2, roots.Count); Assert.Single(entries[0].Children); Assert.Single(entries[3].Children);
        Assert.Equal(5, navigation.GetAllChapters().Count);
        entries[4].Children.Add(entries[3]);
        Assert.Equal(5, navigation.GetAllChapters().Count);
    }
    [Fact]
    public async Task AvailableLocalKf8BooksHaveDecodedSkeletons()
    {
        foreach (var name in LocalSampleFileTests.Azw3Files)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", name);
            if (!File.Exists(path)) continue;
            using var stream = File.OpenRead(path);
            var book = new MobiMetadata();
            await book.ReadMetadataAsync(stream);
            await book.ParseKf8Async();
            Assert.NotNull(book.Kf8Parser);
            Assert.NotEmpty(book.Kf8Parser.Skeletons!);
            output.WriteLine($"{name}: skeletons={book.Kf8Parser.Skeletons?.Count}, chunks={book.Kf8Parser.Chunks?.Count}, navigation={book.Kf8Parser.Ncx?.Count}");
        }
    }
    [Fact]
    public async Task ReadsFdstDeclaredTableOffset()
    {
        var fdst = new byte[32]; Encoding.ASCII.GetBytes("FDST").CopyTo(fdst, 0);
        Put(fdst, 4, 16); Put(fdst, 8, 2); Put(fdst, 16, 0); Put(fdst, 20, 100); Put(fdst, 24, 100); Put(fdst, 28, 120);
        var (stream, records) = Container(new byte[8], fdst);
        var bytes = new byte[256]; Encoding.ASCII.GetBytes("MOBI").CopyTo(bytes, 0);
        Put(bytes, 4, 256); Put(bytes, 12, 65001); Put(bytes, 20, 8); Put(bytes, 0xb0, 1); Put(bytes, 0xb4, 2);
        using (stream)
        {
            var head = new MobiHead(skipExthHeader: true); await head.ReadHeaderAsync(new MemoryStream(bytes));
            var parser = new KF8Parser(stream, records, head, null); await parser.ParseAsync();
            Assert.Equal(new FdstEntry(1, 100, 120), parser.Fdst![1]);
            Put(fdst, 28, 50); stream.Position = 8; stream.Write(fdst);
            await Assert.ThrowsAsync<MobiMetadataException>(() => parser.ParseAsync());
        }
    }
    private static byte[] Header(int size, uint count)
    {
        var data = new byte[size]; Encoding.ASCII.GetBytes("INDX").CopyTo(data, 0);
        Put(data, 4, 192); Put(data, 24, count); Put(data, 28, 65001); return data;
    }
    private static async Task<MobiHead> MobiHeader(uint index)
    {
        var data = new byte[256]; Encoding.ASCII.GetBytes("MOBI").CopyTo(data, 0);
        Put(data, 4, 256); Put(data, 12, 65001); Put(data, 20, 8); Put(data, 0xe4, index);
        Put(data, 0xe8, uint.MaxValue); Put(data, 0xec, uint.MaxValue);
        var head = new MobiHead(skipExthHeader: true); await head.ReadHeaderAsync(new MemoryStream(data)); return head;
    }
    private static (MemoryStream, PDBRecordInfo[]) Container(params byte[][] pieces)
    {
        var stream = new MemoryStream(); var records = new List<PDBRecordInfo>();
        foreach (var piece in pieces) { var data = new byte[8]; Put(data, 0, (uint)stream.Position); records.Add(new PDBRecordInfo(data, 0)); stream.Write(piece); }
        return (stream, records.ToArray());
    }
    private static void Put(byte[] data, int position, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(position), value);
}




