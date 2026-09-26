using System.Text;

namespace MobiMetadata;

public class PageRecord
{
    private static readonly byte[] CRES = Encoding.ASCII.GetBytes("CRES");
    private static readonly byte[] DATP = Encoding.ASCII.GetBytes("DATP");
    private static readonly byte[] RESC = Encoding.ASCII.GetBytes("RESC");
    private static readonly byte[] FONT = Encoding.ASCII.GetBytes("FONT");
    private static readonly byte[] KindleEmbed = Encoding.ASCII.GetBytes("kindle:embed");

    private readonly Stream _stream;
    private readonly long _position;
    private readonly int _length;
    private readonly int _dataOffset;
    private readonly SemaphoreSlim _semaphore;

    public PageRecord(Stream stream, long position, uint length, int dataOffset = 0, SemaphoreSlim? semaphore = null)
    {
        StreamAccess.ValidateRange(stream, position, length);
        if (length > int.MaxValue || dataOffset < 0 || (dataOffset > length && !(length == 4 && dataOffset == 12)))
            throw new MobiMetadataException("Invalid image record length or payload offset.");
        _stream = stream;
        _position = position;
        _length = (int)length;
        _dataOffset = dataOffset;
        _semaphore = StreamAccess.GetLock(stream, semaphore);
    }

    public int Size => _length;
    internal uint ResourceIndex { get; set; }
    protected int DataLength => _length;

    public bool IsCresPlaceholder => _dataOffset == 12 && _length == 4;

    public async Task<bool> IsCresAsync() => await MatchSignatureAsync(CRES).ConfigureAwait(false);
    public async Task<bool> IsDatpAsync() => await MatchSignatureAsync(DATP).ConfigureAwait(false);
    public async Task<bool> IsFontAsync() => await MatchSignatureAsync(FONT).ConfigureAwait(false);
    public async Task<bool> IsKindleEmbedAsync() => await MatchSignatureAsync(KindleEmbed).ConfigureAwait(false);

    public async Task<RescRecord?> GetRescRecordAsync()
    {
        if (!await MatchSignatureAsync(RESC).ConfigureAwait(false))
            return null;

        return new RescRecord(_stream, _position, (uint)_length, _semaphore);
    }

    public async Task<bool> IsStructuralAsync()
    {
        foreach (var signature in new[] { "FDST", "FLIS", "FCIS", "INDX", "DATP", "SRCS", "BOUNDARY", "AUDI", "VIDE", "HUFF", "CDIC" })
        {
            if (await MatchSignatureAsync(Encoding.ASCII.GetBytes(signature)).ConfigureAwait(false))
                return true;
        }
        return _length < 4 || await MatchSignatureAsync(new byte[] { 0xe9, 0x8e, 0x0d, 0x0a }).ConfigureAwait(false);
    }

    public async Task<bool> IsImageAsync()
    {
        if (IsCresPlaceholder || _length <= _dataOffset) return false;
        var prefix = new byte[Math.Min(12, _length - _dataOffset)];
        await StreamAccess.ReadExactlyAtAsync(_stream, _position + _dataOffset, prefix).ConfigureAwait(false);
        return IsImageSignature(prefix);
    }

    private static bool IsImageSignature(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ||
        bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) ||
        bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) || bytes.StartsWith("BM"u8) ||
        bytes.StartsWith(new byte[] { 0x49, 0x49, 0x2a, 0 }) ||
        bytes.StartsWith(new byte[] { 0x49, 0x49, 0xbc, 0x01 }) || // JPEG XR in HD containers
        bytes.StartsWith(new byte[] { 0x4d, 0x4d, 0, 0x2a }) ||
        (bytes.Length >= 12 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8));

    private static void ValidateAllocation(int length)
    {
        if (length < 0 || length > StreamAccess.MaxRecordLength)
            throw new MobiMetadataException("Record exceeds the supported allocation limit; use a stream for large images.");
    }

    internal PageRecord WithPayloadOffset(int offset) => new(_stream, _position, (uint)_length, offset, _semaphore)
    {
        ResourceIndex = ResourceIndex
    };

    internal async Task<bool> IsImageContainerAsync()
    {
        if (_length < 60) return false;
        var prefix = await ReadDataAsync(48, 12).ConfigureAwait(false);
        if (!prefix.Span[..4].SequenceEqual("EXTH"u8)) return false;
        var length = Converter.ToUInt32(prefix.Span.Slice(4, 4));
        if (length < 12 || length > 16 * 1024 * 1024 || length > _length - 48)
            throw new MobiMetadataException("Invalid inline container EXTH length.");
        var bytes = await ReadDataAsync(48, (int)length).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        var exth = new EXTHHead();
        await exth.ReadHeaderAsync(stream).ConfigureAwait(false);
        return exth.ContainerMimeType == "application/image";
    }

    internal async Task<bool> MatchSignatureAsync(byte[] signature)
    {
        if (_length < signature.Length) return false;
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Position = _position;
            var buffer = new byte[signature.Length];
            await _stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
            return buffer.AsSpan().SequenceEqual(signature);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public byte[] ReadData()
    {
        ValidateAllocation(_length);
        _semaphore.Wait();
        try
        {
            _stream.Position = _position;
            var buffer = new byte[_length];
            _stream.ReadExactly(buffer);
            return buffer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<byte[]> ReadDataAsync()
    {
        ValidateAllocation(_length);
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Position = _position;
            var buffer = new byte[_length];
            await _stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
            return buffer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    protected async Task<Memory<byte>> ReadDataAsync(int offset, int length)
    {
        if (offset < 0 || offset > _length || length < 0 || length > _length - offset)
            throw new MobiMetadataException("Requested data exceeds its record.");
        ValidateAllocation(length);
        var buffer = new byte[length];
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Position = _position + offset;
            await _stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
            return buffer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public byte[] GetImageBytes()
    {
        var dataLength = _length - _dataOffset;
        ValidateAllocation(dataLength);
        var buffer = new byte[dataLength];
        
        _semaphore.Wait();
        try
        {
            _stream.Position = _position + _dataOffset;
            _stream.ReadExactly(buffer);
            return buffer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<byte[]> GetImageBytesAsync()
    {
        var dataLength = _length - _dataOffset;
        ValidateAllocation(dataLength);
        var buffer = new byte[dataLength];
        
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _stream.Position = _position + _dataOffset;
            await _stream.ReadExactlyAsync(buffer).ConfigureAwait(false);
            return buffer;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public Stream OpenImageStream()
    {
        if (IsCresPlaceholder)
            throw new MobiMetadataException("A CRES placeholder has no image payload.");
        return new SubStream(_stream, _position + _dataOffset, _length - _dataOffset);
    }
}
