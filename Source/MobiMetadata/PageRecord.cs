using System.Buffers;
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
        _stream = stream;
        _position = position;
        _length = (int)length;
        _dataOffset = dataOffset;
        _semaphore = semaphore ?? new SemaphoreSlim(1, 1);
    }

    public int Size => _length;
    protected int DataLength => _length;

    public bool IsCresPlaceholder => _length == 4;

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
        foreach (var signature in new[] { "FDST", "FLIS", "FCIS", "INDX", "DATP" })
        {
            if (await MatchSignatureAsync(Encoding.ASCII.GetBytes(signature)).ConfigureAwait(false))
                return true;
        }
        return _length < 4 || await MatchSignatureAsync(new byte[] { 0xe9, 0x8e, 0x0d, 0x0a }).ConfigureAwait(false);
    }

    private async Task<bool> MatchSignatureAsync(byte[] signature)
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
        _semaphore.Wait();
        try
        {
            _stream.Position = _position + _dataOffset;
            return new SubStream(_stream, _position + _dataOffset, _length - _dataOffset);
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
