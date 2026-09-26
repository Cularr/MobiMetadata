using System.Buffers.Binary;
using Xunit;

namespace MobiMetadata.Tests;

public class NcxParentEncodingRegressionTests
{
    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public async Task UnspecifiedIndexEncodingInheritsBookCodepage(uint indexEncoding)
    {
        var parser = await Parse(1252, Meta([[3, 1, 1, 0]], indexEncoding),
            Entries([1, (byte)'0', 1, 0x80]), [0x84, (byte)'C', (byte)'a', (byte)'f', 0xe9]);
        Assert.Equal("Café", Assert.Single(parser.Ncx!).Title);
    }

    [Fact]
    public async Task ExplicitIndexEncodingStillTakesPrecedence()
    {
        var parser = await Parse(1252, Meta([[3, 1, 1, 0]], 65001),
            Entries([1, (byte)'0', 1, 0x80]), [0x85, (byte)'C', (byte)'a', (byte)'f', 0xc3, 0xa9]);
        Assert.Equal("Café", Assert.Single(parser.Ncx!).Title);
    }

    [Fact]
    public async Task ParsedParentOverridesNearestShallowerEntry()
    {
        var parser = await Parse(65001, Meta([[3, 1, 1, 0], [4, 1, 2, 0], [21, 1, 4, 0]], 65001),
            Entries([1, (byte)'0', 3, 0x80, 0x80], [1, (byte)'1', 3, 0x82, 0x80],
                [1, (byte)'2', 7, 0x84, 0x81, 0x80]),
            [0x81, (byte)'A', 0x81, (byte)'B', 0x81, (byte)'C']);
        Assert.Equal(0u, parser.Ncx![2].ParentIndex);
        var navigation = new NcxNavigation(parser.Ncx);
        var roots = navigation.BuildHierarchy();
        Assert.Equal("C", Assert.Single(roots[0].Children).Title);
        Assert.Empty(roots[1].Children);
        navigation.BuildHierarchy();
        Assert.Equal(3, navigation.GetAllChapters().Count);
    }

    [Fact]
    public void ExplicitRootAndForwardParentPreserveRelationships()
    {
        var child = new NcxEntry { Level = 1, ParentIndex = 2 };
        var fallback = new NcxEntry { Level = 2 };
        var root = new NcxEntry { Level = 7, ParentIndex = uint.MaxValue };
        var navigation = new NcxNavigation([child, fallback, root]);
        Assert.Same(root, Assert.Single(navigation.BuildHierarchy()));
        Assert.Same(child, Assert.Single(root.Children));
        Assert.Same(fallback, Assert.Single(child.Children));
    }

    [Fact]
    public void RejectsOutOfRangeAndCyclicParentsBeforeMutatingChildren()
    {
        var preserved = new NcxEntry();
        var entry = new NcxEntry { ParentIndex = 4 };
        entry.Children.Add(preserved);
        Assert.Throws<MobiMetadataException>(() => new NcxNavigation([entry]).BuildHierarchy());
        Assert.Same(preserved, Assert.Single(entry.Children));
        entry.ParentIndex = 0;
        Assert.Throws<MobiMetadataException>(() => new NcxNavigation([entry]).BuildHierarchy());
        Assert.Throws<MobiMetadataException>(() => new NcxNavigation([
            new() { ParentIndex = 1 }, new() { ParentIndex = 0 }]).BuildHierarchy());
    }

    [Fact]
    public async Task ParserRejectsCyclicNcxParents()
    {
        await Assert.ThrowsAsync<MobiMetadataException>(() => Parse(65001,
            Meta([[3, 1, 1, 0], [21, 1, 2, 0]], 65001),
            Entries([1, (byte)'0', 3, 0x80, 0x80]), [0x81, (byte)'A']));
    }

    [Fact]
    public void DeepHierarchyUsesIterativeValidation()
    {
        var entries = Enumerable.Range(0, 10000).Select(i => new NcxEntry {
            ParentIndex = i == 9999 ? uint.MaxValue : (uint)(i + 1)
        }).ToArray();
        var navigation = new NcxNavigation(entries);
        Assert.Same(entries[^1], Assert.Single(navigation.BuildHierarchy()));
        Assert.Equal(entries.Length, navigation.GetAllChapters().Count);
    }

    private static async Task<KF8Parser> Parse(uint codepage, byte[] meta, byte[] entries, byte[] strings)
    {
        var bytes = new byte[256]; "MOBI"u8.CopyTo(bytes);
        Put(bytes, 4, 256); Put(bytes, 12, codepage); Put(bytes, 20, 8);
        Put(bytes, 0xe4, 1); Put(bytes, 0xe8, uint.MaxValue); Put(bytes, 0xec, uint.MaxValue);
        var head = new MobiHead(skipExthHeader: true);
        using (var header = new MemoryStream(bytes)) await head.ReadHeaderAsync(header);
        using var stream = new MemoryStream();
        var records = new List<PDBRecordInfo>();
        foreach (var data in new[] { new byte[8], meta, entries, strings })
        {
            var record = new byte[8]; Put(record, 0, (uint)stream.Position);
            records.Add(new(record, 0)); stream.Write(data);
        }
        var parser = new KF8Parser(stream, records.ToArray(), head, null);
        await parser.ParseAsync();
        return parser;
    }
    private static byte[] Meta(byte[][] tags, uint code)
    {
        var data = new byte[204 + tags.Length * 4]; "INDX"u8.CopyTo(data);
        Put(data, 4, 192); Put(data, 24, 1); Put(data, 28, code); Put(data, 52, 1);
        "TAGX"u8.CopyTo(data.AsSpan(192)); Put(data, 196, (uint)(12 + tags.Length * 4)); Put(data, 200, 1);
        for (int i = 0; i < tags.Length; i++) tags[i].CopyTo(data, 204 + i * 4);
        return data;
    }
    private static byte[] Entries(params byte[][] entries)
    {
        int idxt = 192 + entries.Sum(e => e.Length);
        var data = new byte[idxt + 4 + entries.Length * 2]; "INDX"u8.CopyTo(data);
        Put(data, 4, 192); Put(data, 20, (uint)idxt); Put(data, 24, (uint)entries.Length);
        "IDXT"u8.CopyTo(data.AsSpan(idxt));
        int position = 192;
        for (int i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(idxt + 4 + i * 2), (ushort)position);
            entries[i].CopyTo(data, position); position += entries[i].Length;
        }
        return data;
    }
    private static void Put(byte[] data, int position, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(position), value);
}
