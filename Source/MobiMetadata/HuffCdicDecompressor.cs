using System.Buffers.Binary;

namespace MobiMetadata;

public class HuffCdicDecompressor
{
    private const int MaximumBytes = 64 * 1024 * 1024;
    private readonly uint[] _prefixes = new uint[256];
    private readonly uint[] _ranges = new uint[64];
    private readonly List<Phrase> _phrases = [];
    private bool _initialized;
    private record Phrase(ReadOnlyMemory<byte> Bytes, bool Literal);

    public async Task ReadRecordsAsync(Stream stream, PDBRecordInfo[] records, uint huffOffset, uint huffCount)
    {
        _initialized = false;
        _phrases.Clear();
        if (huffCount < 2 || huffOffset >= records.Length || huffCount > records.Length - huffOffset)
            throw new MobiMetadataException("Invalid HUFF/CDIC record range");
        var header = await StreamAccess.ReadRecordAsync(stream, records, (int)huffOffset).ConfigureAwait(false);
        if (header.Length < 24 || !header.AsSpan(0, 4).SequenceEqual("HUFF"u8) || U32(header, 4) != 24)
            throw new MobiMetadataException("Invalid HUFF header");
        var prefixOffset = U32(header, 8);
        var rangeOffset = U32(header, 12);
        if (prefixOffset < 24 || prefixOffset > header.Length - 1024 || rangeOffset < 24 || rangeOffset > header.Length - 256)
            throw new MobiMetadataException("HUFF tables exceed record bounds");
        for (var i = 0; i < _prefixes.Length; i++)
        {
            var entry = U32(header, (int)prefixOffset + i * 4);
            var length = (int)(entry & 31);
            if (length == 0 || (length <= 8 && (entry & 128) == 0) || (entry >> 8) >= (1UL << length))
                throw new MobiMetadataException("Invalid HUFF prefix code");
            _prefixes[i] = entry;
        }
        for (var i = 0; i < _ranges.Length; i++) _ranges[i] = U32(header, (int)rangeOffset + i * 4);
        uint total = 0;
        long dictionaryBytes = 0;
        for (var record = 1; record < huffCount; record++)
        {
            var data = await StreamAccess.ReadRecordAsync(stream, records, (int)huffOffset + record).ConfigureAwait(false);
            dictionaryBytes += data.Length;
            if (dictionaryBytes > MaximumBytes) throw new MobiMetadataException("CDIC dictionary size limit exceeded");
            if (data.Length < 16 || !data.AsSpan(0, 4).SequenceEqual("CDIC"u8) || U32(data, 4) != 16)
                throw new MobiMetadataException("Invalid CDIC header");
            var declared = U32(data, 8);
            var bits = U32(data, 12);
            if (declared == 0 || declared > 1_000_000 || bits > 16 || (record > 1 && declared != total) || declared <= _phrases.Count)
                throw new MobiMetadataException("Invalid CDIC phrase count");
            total = declared;
            var count = (int)Math.Min(1u << (int)bits, declared - (uint)_phrases.Count);
            if (count > (data.Length - 16) / 2) throw new MobiMetadataException("Truncated CDIC offset table");
            for (var i = 0; i < count; i++)
            {
                var offset = 16 + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(16 + i * 2));
                if (offset < 16 + count * 2 || offset > data.Length - 2)
                    throw new MobiMetadataException("Invalid CDIC phrase offset");
                var descriptor = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));
                var length = descriptor & 0x7fff;
                if (length > data.Length - offset - 2) throw new MobiMetadataException("Truncated CDIC phrase");
                _phrases.Add(new Phrase(data.AsMemory(offset + 2, length), (descriptor & 0x8000) != 0));
            }
        }
        if (_phrases.Count != total) throw new MobiMetadataException("Incomplete CDIC dictionary");
        _initialized = true;
    }

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    public byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        long remainingWork = 16_000_000;
        return Decompress(input, outputLength, ref remainingWork);
    }

    internal byte[] Decompress(ReadOnlySpan<byte> input, int outputLength, ref long remainingWork)
    {
        if (!_initialized) throw new MobiMetadataException("HUFF/CDIC not initialized");
        if (outputLength < 0 || outputLength > MaximumBytes || input.Length > MaximumBytes)
            throw new MobiMetadataException("HUFF/CDIC size limit exceeded");
        using var output = new MemoryStream(Math.Min(outputLength, 65536));
        var active = new HashSet<int>();
        var work = Math.Min(16_000_000L, remainingWork);
        var initialWork = work;
        try { Expand(input, output, outputLength, active, ref work); }
        finally { remainingWork -= initialWork - work; }
        return output.ToArray();
    }

    private void Expand(ReadOnlySpan<byte> input, MemoryStream output, int limit, HashSet<int> active, ref long work)
    {
        var bit = 0;
        while (bit < input.Length * 8)
        {
            if (--work < 0) throw new MobiMetadataException("HUFF/CDIC work limit exceeded");
            // The next code is left aligned, including a zero-padded final window.
            uint window = 0;
            for (var n = 0; n < Math.Min(32, input.Length * 8 - bit); n++)
                window |= (uint)((input[(bit + n) / 8] >> (7 - (bit + n) % 8)) & 1) << (31 - n);
            var prefix = _prefixes[window >> 24];
            var length = (int)(prefix & 31);
            var maximum = prefix >> 8;
            if ((prefix & 128) == 0)
            {
                while (length <= 32 && (window >> (32 - length)) < _ranges[(length - 1) * 2]) length++;
                if (length > 32) throw new MobiMetadataException("Invalid HUFF code range");
                maximum = _ranges[(length - 1) * 2 + 1];
            }
            if (length > input.Length * 8 - bit) break; // An incomplete final code is padding.
            var code = window >> (32 - length);
            if (code > maximum || maximum - code >= _phrases.Count)
                throw new MobiMetadataException("Invalid HUFF dictionary index");
            bit += length;
            var index = (int)(maximum - code);
            var phrase = _phrases[index];
            if (phrase.Literal)
            {
                if (phrase.Bytes.Length > limit - output.Length) throw new MobiMetadataException("HUFF/CDIC output limit exceeded");
                output.Write(phrase.Bytes.Span);
            }
            else
            {
                if (active.Count >= 64 || !active.Add(index)) throw new MobiMetadataException("Cyclic or excessively nested CDIC phrase");
                Expand(phrase.Bytes.Span, output, limit, active, ref work);
                active.Remove(index);
            }
        }
    }

    public Task<byte[]> DecompressAsync(Memory<byte> input, int outputLength)
        => Task.Run(() => Decompress(input.Span, outputLength));
}
