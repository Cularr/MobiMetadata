using System.Buffers.Binary;

namespace MobiMetadata;

public class HuffCdicDecompressor
{
    private const int MaxCacheSize = 1024;

    private readonly uint[] _dict1 = new uint[256];
    private readonly uint[] _dict2 = new uint[64];
    private readonly List<CdicDictionary> _dictionaries = [];
    private readonly LinkedList<int> _cacheOrder = [];
    private readonly Dictionary<int, (byte[] Entry, LinkedListNode<int> Node)> _entryCache = [];

    private record CdicDictionary(byte[] Data, uint EntryCount, int CodeLength);

    public async Task ReadRecordsAsync(Stream stream, PDBRecordInfo[] records, uint huffOffset, uint huffCount)
    {
        if (huffCount == 0)
            throw new MobiMetadataException("No HUFF/CDIC records found");

        var huffRecord = records[huffOffset];
        stream.Position = huffRecord.DataOffset;

        var nextOffset = huffOffset + 1 < records.Length
            ? records[huffOffset + 1].DataOffset
            : huffRecord.DataOffset + 4096;

        var huffData = new byte[nextOffset - huffRecord.DataOffset];
        await stream.ReadAsync(huffData).ConfigureAwait(false);
        ParseHuffRecord(huffData);

        for (uint i = 1; i < huffCount; i++)
        {
            var cdicRecord = records[huffOffset + i];
            stream.Position = cdicRecord.DataOffset;

            var cdicNext = huffOffset + i + 1 < records.Length
                ? records[huffOffset + i + 1].DataOffset
                : cdicRecord.DataOffset + 4096;

            var cdicData = new byte[cdicNext - cdicRecord.DataOffset];
            var bytesRead = await stream.ReadAsync(cdicData).ConfigureAwait(false);

            if (bytesRead >= 16)
            {
                ParseCdicRecord(cdicData.AsMemory(0, bytesRead));
            }
        }
    }

    private void ParseHuffRecord(byte[] data)
    {
        if (data.Length < 16)
            throw new MobiMetadataException("Invalid HUFF record: too short");

        if (data[0] != 'H' || data[1] != 'U' || data[2] != 'F' || data[3] != 'F')
            throw new MobiMetadataException("Invalid HUFF record: bad identifier");

        var offset1 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8));
        var offset2 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));

        for (int i = 0; i < 256; i++)
        {
            _dict1[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset1 + i * 4));
        }

        for (int i = 0; i < 64; i++)
        {
            _dict2[i] = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset2 + i * 4));
        }
    }

    private void ParseCdicRecord(Memory<byte> data)
    {
        var span = data.Span;

        if (span[0] != 'C' || span[1] != 'D' || span[2] != 'I' || span[3] != 'C')
            return;

        var entryCount = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4));
        var codeLength = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(12, 4));

        _dictionaries.Add(new CdicDictionary(data.ToArray(), entryCount, (int)codeLength));
    }

    public byte[] Decompress(ReadOnlySpan<byte> input, int outputLength)
    {
        var output = new byte[outputLength];
        var pos = 0;
        var bitPos = 0;
        var bitsAvailable = input.Length * 8;

        while (bitPos < bitsAvailable && pos < outputLength)
        {
            var slice = DecodeSlice(input, ref bitPos, bitsAvailable);
            if (slice == null) break;
            
            var toCopy = Math.Min(slice.Length, outputLength - pos);
            slice.AsSpan(0, toCopy).CopyTo(output.AsSpan(pos));
            pos += toCopy;
        }

        // Return only the portion we actually wrote to
        if (pos < outputLength)
        {
            var trimmed = new byte[pos];
            output.AsSpan(0, pos).CopyTo(trimmed);
            return trimmed;
        }

        return output;
    }

    private byte[]? DecodeSlice(ReadOnlySpan<byte> data, ref int bitPos, int bitsAvailable)
    {
        if (bitPos >= bitsAvailable) return null;

        var bitsToRead = Math.Min(32, bitsAvailable - bitPos);
        var code = ReadBitsBigEndian(data, bitPos, bitsToRead);

        var index = (int)((code >> 24) & 0xFF);
        var entry = _dict1[index];

        var codeLen = (int)((entry >> 27) & 0x1F);
        var isTerminal = ((entry >> 26) & 1) != 0;
        var maxCode = entry & 0x03FFFFFF;

        if (!isTerminal)
        {
            for (int len = codeLen; len <= 32; len++)
            {
                var dict2Idx = (len - 1) * 2;
                if (dict2Idx >= 64) break;

                var minCode = _dict2[dict2Idx];
                var maxCodeForLen = _dict2[dict2Idx + 1];
                var codeForLen = code >> (32 - len);

                if (codeForLen >= minCode && codeForLen <= maxCodeForLen)
                {
                    codeLen = len;
                    maxCode = maxCodeForLen;
                    break;
                }
            }
        }

        bitPos += codeLen;
        var dictIndex = (int)(maxCode - (code >> (32 - codeLen)));
        return GetDictionaryEntry(dictIndex);
    }

    private static uint ReadBitsBigEndian(ReadOnlySpan<byte> data, int bitPos, int numBits)
    {
        uint result = 0;

        for (int i = 0; i < numBits; i++)
        {
            var absPos = bitPos + i;
            var byteIndex = absPos / 8;
            var bitIndex = 7 - (absPos % 8);

            if (byteIndex < data.Length && (data[byteIndex] & (1 << bitIndex)) != 0)
            {
                result |= 1u << (numBits - 1 - i);
            }
        }

        return result;
    }

    private byte[]? GetDictionaryEntry(int index)
    {
        if (_entryCache.TryGetValue(index, out var cached))
        {
            // Move to front (most recently used)
            _cacheOrder.Remove(cached.Node);
            _cacheOrder.AddFirst(cached.Node);
            return cached.Entry;
        }

        var entryOffset = 0;
        foreach (var dict in _dictionaries)
        {
            if (index < entryOffset + dict.EntryCount)
            {
                var localIndex = index - entryOffset;
                var entry = GetEntryFromCdic(dict.Data, localIndex);

                if (entry != null)
                {
                    AddToCache(index, entry);
                }

                return entry;
            }
            entryOffset += (int)dict.EntryCount;
        }

        return null;
    }

    private void AddToCache(int index, byte[] entry)
    {
        if (_entryCache.Count >= MaxCacheSize)
        {
            // Remove least recently used
            var lruIndex = _cacheOrder.Last;
            if (lruIndex != null)
            {
                _entryCache.Remove(lruIndex.Value);
                _cacheOrder.RemoveLast();
            }
        }

        var node = _cacheOrder.AddFirst(index);
        _entryCache[index] = (entry, node);
    }

    private byte[]? GetEntryFromCdic(byte[] data, int index)
    {
        var span = data.AsSpan();
        var offset = 16;

        for (int i = 0; i < index && offset < span.Length - 2; i++)
        {
            var entryLen = span[offset] | (span[offset + 1] << 8);
            offset += 2 + entryLen;
        }

        if (offset >= span.Length - 2) return null;

        var length = span[offset] | (span[offset + 1] << 8);
        offset += 2;

        if (offset + length > span.Length) return null;

        var entry = span.Slice(offset, length).ToArray();

        if (length > 0 && (entry[0] & 0x80) != 0)
        {
            entry = DecompressEntry(entry);
        }

        return entry;
    }

    private static byte[] DecompressEntry(byte[] entry)
    {
        if (entry.Length < 2) return entry;

        var result = new byte[entry.Length - 1];
        Buffer.BlockCopy(entry, 1, result, 0, result.Length);
        return result;
    }

    public async Task<byte[]> DecompressAsync(Memory<byte> input, int outputLength)
        => await Task.Run(() => Decompress(input.Span, outputLength)).ConfigureAwait(false);
}
