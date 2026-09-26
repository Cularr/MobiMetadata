using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace MobiMetadata.Tests;

public class CompressionRegressionTests
{
    [Fact]
    public void PalmDocDecodesBackReferencesAndOverlapExactly()
    {
        Assert.Equal("ABCABC", Encoding.ASCII.GetString(PalmDocDecompressor.Decompress([65, 66, 67, 0x80, 0x18], 32)));
        Assert.Equal("AAAAAA", Encoding.ASCII.GetString(PalmDocDecompressor.Decompress([65, 0x80, 0x0a], 6)));
        Assert.Equal(new byte[] { 0, 1, 2, 32, 65 }, PalmDocDecompressor.Decompress([0, 2, 1, 2, 0xc1], 12));
    }

    [Theory]
    [InlineData(new byte[] { 0x80 })]
    [InlineData(new byte[] { 0x80, 0x08 })]
    [InlineData(new byte[] { 65, 0x80, 0 })]
    [InlineData(new byte[] { 2, 65 })]
    public void PalmDocRejectsMalformedTokens(byte[] input)
        => Assert.Throws<MobiMetadataException>(() => PalmDocDecompressor.Decompress(input, 32));

    [Fact]
    public void PalmDocRejectsOutputOverflow()
        => Assert.Throws<MobiMetadataException>(() => PalmDocDecompressor.Decompress([0xc1], 1));

    [Fact]
    public async Task TextPreservesLeadingBytesAndFinalRecordEof()
    {
        var text = await CreateText(["Hello"u8.ToArray()], 5);
        Assert.Equal("Hello", await text.ExtractTextAsync());
    }

    [Fact]
    public async Task TextReadsAllDeclaredRecordsAndTrimsFinalPadding()
    {
        var text = await CreateText(["Hello"u8.ToArray(), " world\0\0"u8.ToArray()], 11);
        Assert.Equal("Hello world", await text.ExtractTextAsync());
    }

    [Fact]
    public async Task TextRemovesVariableLengthAndOverlapTrailers()
    {
        // Text, overlap byte/count, a three-byte trailer, then a two-byte trailer.
        var text = await CreateText([[72, 105, 0xa9, 1, 7, 8, 0x83, 9, 0x82]], 2, flags: 7);
        Assert.Equal("Hi", await text.ExtractTextAsync());
    }

    [Fact]
    public async Task TextJoinsSplitUtf8AndSupportsWindows1252()
    {
        var utf8 = await CreateText([[0xc3], [0xa9]], 2);
        Assert.Equal("é", await utf8.ExtractTextAsync());
        var legacy = await CreateText([[0x80, 0x93, 0xe9]], 3, encoding: 1252);
        Assert.Equal("€“é", await legacy.ExtractTextAsync());
    }

    [Fact]
    public async Task TextUsesRenditionBaseAndPalmDocCount()
    {
        var text = await CreateText(["ignored"u8.ToArray(), "ABC"u8.ToArray()], 3, first: 2, count: 1);
        Assert.Equal("ABC", await text.ExtractTextAsync());
    }

    [Fact]
    public async Task TextRejectsMalformedTrailersAndTruncatedText()
    {
        var trailer = await CreateText([[65, 0xff]], 1, flags: 2);
        await Assert.ThrowsAsync<MobiMetadataException>(() => trailer.ExtractTextAsync());
        var truncated = await CreateText([[65]], 2);
        await Assert.ThrowsAsync<MobiMetadataException>(() => truncated.ExtractTextAsync());
    }

    [Fact]
    public async Task TextRejectsEncryptionAndHandlesZeroRecords()
    {
        var encrypted = await CreateText([[65]], 1, encryption: 1);
        await Assert.ThrowsAsync<MobiMetadataException>(() => encrypted.ExtractTextAsync());
        var empty = await CreateText([], 0);
        Assert.Equal("", await empty.ExtractTextAsync());
    }

    [Fact]
    public async Task HuffDecodesOffsetTableAndMultipleCdicRecords()
    {
        var decoder = await CreateHuff(split: true);
        Assert.Equal(new byte[] { 65, 66, 255, 0 }, decoder.Decompress([190, 189, 0, 255], 16));
    }

    [Fact]
    public async Task HuffExpandsCompressedPhrases()
    {
        var decoder = await CreateHuff(recursive: true);
        Assert.Equal("hellohello", Encoding.ASCII.GetString(decoder.Decompress([255, 255], 10)));
        Assert.Throws<MobiMetadataException>(() => decoder.Decompress([255, 255], 9));
    }

    [Fact]
    public async Task HuffRejectsCyclesAndZeroBitPrefixes()
    {
        var cyclic = await CreateHuff(recursive: true, cycle: true);
        Assert.Throws<MobiMetadataException>(() => cyclic.Decompress([255], 16));
        await Assert.ThrowsAsync<MobiMetadataException>(() => CreateHuff(zeroPrefix: true));
    }

    [Fact]
    public async Task HuffAlignsPartialWindowsAndUsesLongCodeRanges()
    {
        var decoder = await CreateHuff(longCodes: true);
        Assert.Equal(new byte[] { 255 }, decoder.Decompress([0, 0], 10));
    }

    [Fact]
    public async Task HuffDecodesSubByteCodesWithoutZeroPadding()
    {
        var decoder = await CreateHuff(shortCodes: true);
        Assert.Equal(new byte[] { 1, 0, 1, 0, 1, 0, 1, 0 }, decoder.Decompress([0x55], 8));
    }

    [Fact]
    public async Task HuffRejectsOffsetsOutsideCdicRecord()
        => await Assert.ThrowsAsync<MobiMetadataException>(() => CreateHuff(badOffset: true));

    private static async Task<TextContent> CreateText(byte[][] content, uint length, ushort flags = 0,
        uint encoding = 65001, int first = 1, ushort? count = null, ushort encryption = 0)
    {
        var palm = new byte[16];
        W16(palm, 0, 1); W32(palm, 4, length); W16(palm, 8, count ?? (ushort)content.Length);
        W16(palm, 10, 4096); W16(palm, 12, encryption);
        var palmHead = new PalmDOCHead();
        await palmHead.ReadHeaderAsync(new MemoryStream(palm));
        var mobi = new byte[232];
        "MOBI"u8.CopyTo(mobi); W32(mobi, 4, (uint)mobi.Length); W32(mobi, 12, encoding); W32(mobi, 20, 6);
        W16(mobi, 0xe2, flags);
        var mobiHead = new MobiHead();
        await mobiHead.ReadHeaderAsync(new MemoryStream(mobi));
        var chunks = new[] { new byte[] { 0 } }.Concat(content).ToArray();
        var (stream, records) = Container(chunks);
        return new TextContent(stream, records, palmHead, mobiHead, first);
    }

    private static async Task<HuffCdicDecompressor> CreateHuff(bool split = false, bool recursive = false,
        bool cycle = false, bool zeroPrefix = false, bool longCodes = false, bool shortCodes = false, bool badOffset = false)
    {
        var huff = new byte[24 + 1024 + 256];
        "HUFF"u8.CopyTo(huff); W32(huff, 4, 24); W32(huff, 8, 24); W32(huff, 12, 1048);
        for (var i = 0; i < 256; i++) W32(huff, 24 + i * 4, zeroPrefix ? 128u : longCodes ? 9u : shortCodes ? 0x181u : 0xff88u);
        W32(huff, 1048 + 17 * 4, 255); // Nine-bit maximum code, minimum zero.
        var phrases = Enumerable.Range(0, 256).Select(i => ((byte[])[(byte)i], true)).ToArray();
        if (recursive) { phrases[0] = ([(byte)(cycle ? 255 : 254)], false); phrases[1] = ("hello"u8.ToArray(), true); }
        var chunks = new List<byte[]> { huff };
        var capacity = split ? 128 : 256;
        for (var start = 0; start < 256; start += capacity)
        {
            var size = 16 + capacity * 2 + phrases.Skip(start).Take(capacity).Sum(p => 2 + p.Item1.Length);
            var cdic = new byte[size];
            "CDIC"u8.CopyTo(cdic); W32(cdic, 4, 16); W32(cdic, 8, 256); W32(cdic, 12, split ? 7u : 8u);
            var offset = 16 + capacity * 2;
            for (var i = 0; i < capacity; i++)
            {
                var (bytes, literal) = phrases[start + i];
                W16(cdic, 16 + i * 2, (ushort)(offset - 16));
                W16(cdic, offset, (ushort)(bytes.Length | (literal ? 0x8000 : 0)));
                bytes.CopyTo(cdic, offset + 2); offset += bytes.Length + 2;
            }
            if (badOffset) W16(cdic, 16, ushort.MaxValue);
            chunks.Add(cdic);
        }
        var (stream, records) = Container(chunks.ToArray());
        var decoder = new HuffCdicDecompressor();
        await decoder.ReadRecordsAsync(stream, records, 0, (uint)chunks.Count);
        return decoder;
    }

    private static (Stream, PDBRecordInfo[]) Container(byte[][] chunks)
    {
        var table = new byte[chunks.Length * 8];
        var offset = 0;
        for (var i = 0; i < chunks.Length; i++) { W32(table, i * 8, (uint)offset); offset += chunks[i].Length; }
        return (new ShortReadStream(chunks.SelectMany(c => c).ToArray()),
            Enumerable.Range(0, chunks.Length).Select(i => new PDBRecordInfo(table, i * 8)).ToArray());
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }

    private static void W16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
    private static void W32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
}

