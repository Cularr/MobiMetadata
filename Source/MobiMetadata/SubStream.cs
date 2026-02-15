namespace MobiMetadata;

internal class SubStream : Stream
{
    private readonly Stream _baseStream;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public SubStream(Stream baseStream, long start, int length)
    {
        _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
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
    {
        var remaining = _length - _position;
        if (remaining <= 0) return 0;

        var toRead = (int)Math.Min(count, remaining);
        _baseStream.Position = _start + _position;
        var read = _baseStream.Read(buffer, offset, toRead);
        _position += read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var remaining = _length - _position;
        if (remaining <= 0) return 0;

        var toRead = (int)Math.Min(count, remaining);
        _baseStream.Position = _start + _position;
        var read = await _baseStream.ReadAsync(buffer, offset, toRead, cancellationToken).ConfigureAwait(false);
        _position += read;
        return read;
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
