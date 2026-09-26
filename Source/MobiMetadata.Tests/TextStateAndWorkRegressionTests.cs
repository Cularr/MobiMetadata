using System.Buffers.Binary;
using System.Reflection;
using Xunit;

namespace MobiMetadata.Tests;

public class TextStateAndWorkRegressionTests
{
    [Fact]
    public async Task RetainedTextKeepsOriginalHeaderValuesAfterReload()
    {
        var palm = new PalmDOCHead();
        var mobi = new MobiHead();
        await palm.ReadHeaderAsync(new MemoryStream(Palm(8, 1, 1)));
        await mobi.ReadHeaderAsync(new MemoryStream(Mobi()));
        var (stream, records) = Container([[], "ésecret"u8.ToArray()]);
        using (stream)
        {
            var text = new TextContent(stream, records, palm, mobi);
            await palm.ReadHeaderAsync(new MemoryStream(Palm(3, 0, 17480, 1, 1)));
            await mobi.ReadHeaderAsync(new MemoryStream(Mobi(1252, 7, 99, 9)));
            Assert.Equal("None", text.CompressionName);
            Assert.Equal("UTF-8", text.TextEncoding);
            Assert.Equal(8u, text.TextLength);
            Assert.Equal((ushort)1, text.RecordCount);
            Assert.Equal("ésecret", await text.ExtractTextAsync());
        }
    }

    [Fact]
    public async Task HuffInitializationAndMultipleRecordsUseSnapshot()
    {
        var (huff, cdic) = Dictionary(["AB"u8.ToArray()], [true]);
        var (stream, records) = Container([[], [255], [255], huff, cdic]);
        using (stream)
        {
            var palm = new PalmDOCHead();
            var mobi = new MobiHead();
            await palm.ReadHeaderAsync(new MemoryStream(Palm(4, 2, 17480)));
            await mobi.ReadHeaderAsync(new MemoryStream(Mobi(huffOffset: 3, huffCount: 2)));
            var text = new TextContent(stream, records, palm, mobi);
            await palm.ReadHeaderAsync(new MemoryStream(Palm(1, 1, 1, 1, 1)));
            await mobi.ReadHeaderAsync(new MemoryStream(Mobi(1252, 7, 99, 9)));
            Assert.Equal("ABAB", await text.ExtractTextAsync());
            // A second extraction owns a fresh work budget and stable decoding state.
            Assert.Equal("ABAB", await text.ExtractTextAsync());
        }
    }

    private delegate byte[] DecodeWithBudget(ReadOnlySpan<byte> input, int outputLength, ref long work);

    [Fact]
    public async Task EmptyDictionaryExpansionConsumesSharedWorkAcrossRecords()
    {
        // A seven-step acyclic expansion with no output must still consume work.
        var (huff, cdic) = Dictionary([[254, 254], [253, 253], []], [false, false, true]);
        var (stream, records) = Container([huff, cdic]);
        using (stream)
        {
            var decoder = new HuffCdicDecompressor();
            await decoder.ReadRecordsAsync(stream, records, 0, 2);
            var method = typeof(HuffCdicDecompressor).GetMethod("Decompress", BindingFlags.Instance | BindingFlags.NonPublic,
                null, [typeof(ReadOnlySpan<byte>), typeof(int), typeof(long).MakeByRefType()], null)!;
            var decode = method.CreateDelegate<DecodeWithBudget>(decoder);
            long work = 14;
            Assert.Empty(decode([255], 16, ref work));
            Assert.Equal(7, work);
            Assert.Empty(decode([255], 16, ref work));
            Assert.Equal(0, work);
            var failure = Assert.Throws<MobiMetadataException>(() => decode([255], 16, ref work));
            Assert.Contains("work limit", failure.Message);
            Assert.Empty(decoder.Decompress([255], 16));
        }
    }

    private static byte[] Palm(uint length, ushort count, ushort compression, ushort encryption = 0, ushort size = 4096)
    {
        var bytes = new byte[16]; W16(bytes, 0, compression); W32(bytes, 4, length);
        W16(bytes, 8, count); W16(bytes, 10, size); W16(bytes, 12, encryption); return bytes;
    }

    private static byte[] Mobi(uint encoding = 65001, ushort flags = 0, uint huffOffset = 0, uint huffCount = 0)
    {
        var bytes = new byte[232]; "MOBI"u8.CopyTo(bytes); W32(bytes, 4, 232); W32(bytes, 12, encoding);
        W32(bytes, 20, 6); W16(bytes, 0xe2, flags); W32(bytes, 96, huffOffset); W32(bytes, 100, huffCount); return bytes;
    }

    private static (byte[], byte[]) Dictionary(byte[][] phrases, bool[] literal)
    {
        var huff = new byte[1304]; "HUFF"u8.CopyTo(huff); W32(huff, 4, 24); W32(huff, 8, 24); W32(huff, 12, 1048);
        for (var i = 0; i < 256; i++) W32(huff, 24 + i * 4, 0xff88);
        var cdic = new byte[16 + 4 * phrases.Length + phrases.Sum(p => p.Length)];
        "CDIC"u8.CopyTo(cdic); W32(cdic, 4, 16); W32(cdic, 8, (uint)phrases.Length); W32(cdic, 12, 8);
        var offset = 16 + 2 * phrases.Length;
        for (var i = 0; i < phrases.Length; i++)
        {
            W16(cdic, 16 + i * 2, (ushort)(offset - 16));
            W16(cdic, offset, (ushort)(phrases[i].Length | (literal[i] ? 0x8000 : 0)));
            phrases[i].CopyTo(cdic, offset + 2); offset += 2 + phrases[i].Length;
        }
        return (huff, cdic);
    }

    private static (MemoryStream, PDBRecordInfo[]) Container(byte[][] chunks)
    {
        var table = new byte[chunks.Length * 8]; var offset = 0;
        for (var i = 0; i < chunks.Length; i++) { W32(table, i * 8, (uint)offset); offset += chunks[i].Length; }
        return (new MemoryStream(chunks.SelectMany(c => c).ToArray()),
            Enumerable.Range(0, chunks.Length).Select(i => new PDBRecordInfo(table, i * 8)).ToArray());
    }
    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}
