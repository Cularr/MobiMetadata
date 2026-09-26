namespace MobiMetadata;

internal class SubStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _start;
    private readonly long _length;
    private readonly SemaphoreSlim _semaphore;
    private long _position;

    public SubStream(Stream baseStream, long start, int length)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        StreamAccess.ValidateRange(baseStream, start, length);
        _semaphore = StreamAccess.GetLock(baseStream);
        _start = start;
        _length = length;
        _position = 0;
    }

    public override bool CanRead => _baseStream.CanRead;
    public override bool CanSeek => _baseStream.CanSeek;
    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        _semaphore.Wait();
        try
        {
            var toRead = (int)Math.Min(buffer.Length, _length - _position);
            if (toRead <= 0) return 0;
            _baseStream.Position = _start + _position;
            var read = _baseStream.Read(buffer[..toRead]);
            _position += read;
            return read;
        }
        finally { _semaphore.Release(); }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var toRead = (int)Math.Min(buffer.Length, _length - _position);
            if (toRead <= 0) return 0;
            _baseStream.Position = _start + _position;
            var read = await _baseStream.ReadAsync(buffer[..toRead], cancellationToken).ConfigureAwait(false);
            _position += read;
            return read;
        }
        finally { _semaphore.Release(); }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
        };

        if (_position < 0) _position = 0;
        if (_position > _length) _position = _length;

        return _position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing) { }
}
