namespace MobiMetadata;

public class MobiMetadata : IDisposable
{
    private const int SdDataOffset = 0;
    private const int HdDataOffset = 12; // Skip CRES + 12-byte header

    private readonly PDBHead _pdbHeader;
    private readonly PalmDOCHead _palmDocHeader;
    private readonly MobiHead _mobiHeader;
    private readonly bool _requireExthHeader;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private Stream _stream = null!;
    private volatile bool _isInitialized;
    private int _kf8RecordBase;
    private uint _kf8FirstImageIndex = uint.MaxValue;
    private bool _disposed;

    public PDBHead PdbHeader => _pdbHeader;
    public PalmDOCHead PalmDocHeader => _palmDocHeader;
    public MobiHead MobiHeader => _mobiHeader;

    public TextContent? TextContent { get; private set; }
    public KF8Parser? Kf8Parser { get; private set; }
    public NcxNavigation? Navigation { get; private set; }
    public PageRecords? PageRecords { get; private set; }
    public PageRecords? HdContainer { get; private set; }
    public Azw6Head? Azw6Header { get; private set; }

    private List<PageRecord> _images = [];
    public IReadOnlyList<PageRecord> Images => _images;
    public PageRecord? Cover { get; private set; }

    public bool IsKf8 => _isInitialized && _mobiHeader.FileVersion == 8;
    public bool IsDualMobi { get; private set; }

    public MobiMetadata(PDBHead? pdbHeader = null, PalmDOCHead? palmDocHeader = null, MobiHead? mobiHeader = null, bool requireExthHeader = false)
    {
        _pdbHeader = pdbHeader ?? new PDBHead();
        _palmDocHeader = palmDocHeader ?? new PalmDOCHead();
        _mobiHeader = mobiHeader ?? new MobiHead();
        _requireExthHeader = requireExthHeader;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        // The caller owns the streams and disposes them.
        _disposed = true;
    }

    public async Task ReadMetadataAsync(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(stream);
        StreamAccess.ValidateRange(stream, 0, 0);
        await _operationLock.WaitAsync().ConfigureAwait(false);
        var gate = StreamAccess.GetLock(stream);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ResetState();
            _stream = stream;
            stream.Position = 0;
            await _pdbHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
            if (!_pdbHeader.SkipProperties && (_pdbHeader.TypeName != "BOOK" || _pdbHeader.CreatorName != "MOBI"))
                throw new MobiMetadataException("Unsupported ebook container: expected BOOK/MOBI (DRM-free MOBI or AZW3).");
            _palmDocHeader.RecordEndPosition = _pdbHeader.FirstRecordEndPosition;
            _mobiHeader.RecordEndPosition = _pdbHeader.FirstRecordEndPosition;
            await _palmDocHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
            _mobiHeader.PreviousHeaderPosition = _palmDocHeader.StreamPosition;
            await _mobiHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
            if (_mobiHeader.ExthHeader == null && _requireExthHeader)
                throw new MobiMetadataException($"No EXTH header found in '{_mobiHeader.FullName}'");

            var activeMobi = _mobiHeader;
            var activePalm = _palmDocHeader;
            int recordBase = 0;
            uint boundary = _mobiHeader.ExthHeader?.Kf8BoundaryOffset ?? uint.MaxValue;
            if (_mobiHeader.FileVersion != 8 && boundary != 0 && boundary != uint.MaxValue && !_pdbHeader.SkipRecords)
            {
                var records = _pdbHeader.Records;
                if (boundary >= records.Length || boundary < 2 || records[boundary].DataOffset - (long)records[boundary - 1].DataOffset != 8)
                    throw new MobiMetadataException("Invalid KF8 boundary record.");
                stream.Position = records[boundary - 1].DataOffset;
                var marker = new byte[8];
                await stream.ReadExactlyAsync(marker).ConfigureAwait(false);
                if (!marker.AsSpan().SequenceEqual("BOUNDARY"u8))
                    throw new MobiMetadataException("Invalid KF8 boundary marker.");
                recordBase = (int)boundary;
                long end = boundary + 1 < records.Length ? records[boundary + 1].DataOffset : stream.Length;
                stream.Position = records[boundary].DataOffset;
                activePalm = new PalmDOCHead { RecordEndPosition = end };
                activeMobi = new MobiHead { RecordEndPosition = end };
                await activePalm.ReadHeaderAsync(stream).ConfigureAwait(false);
                activeMobi.PreviousHeaderPosition = activePalm.StreamPosition;
                await activeMobi.ReadHeaderAsync(stream).ConfigureAwait(false);
                if (activeMobi.FileVersion != 8)
                    throw new MobiMetadataException("KF8 boundary does not reference a version 8 header.");
                IsDualMobi = true;
                _kf8RecordBase = recordBase;
                if (activeMobi.FirstImageIndex is > 0 and < uint.MaxValue)
                {
                    long firstImage = (long)recordBase + activeMobi.FirstImageIndex;
                    if (firstImage > records.Length)
                        throw new MobiMetadataException("KF8 image index is outside the record directory.");
                    _kf8FirstImageIndex = (uint)firstImage;
                }
            }
            if (!_pdbHeader.SkipRecords && !activePalm.SkipProperties && !activeMobi.SkipProperties)
            {
                TextContent = new TextContent(stream, _pdbHeader.Records, activePalm, activeMobi, recordBase + 1);
                if (activeMobi.FileVersion == 8 || IsDualMobi)
                    Kf8Parser = new KF8Parser(stream, _pdbHeader.Records, activeMobi, activeMobi.ExthHeader, recordBase);
            }
            _isInitialized = true;
        }
        catch
        {
            ResetState();
            throw;
        }
        finally
        {
            gate.Release();
            _operationLock.Release();
        }
    }

    private void ResetState()
    {
        _isInitialized = false;
        IsDualMobi = false;
        _kf8RecordBase = 0;
        _kf8FirstImageIndex = uint.MaxValue;
        TextContent = null;
        Kf8Parser = null;
        Navigation = null;
        ClearImages();
        _pdbHeader.ResetReadState();
        _palmDocHeader.ResetReadState();
        _mobiHeader.ResetReadState();
    }

    private void ClearImages()
    {
        PageRecords = null;
        HdContainer = null;
        Azw6Header = null;
        Cover = null;
        _images = [];
    }

    public async Task ParseKf8Async()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            Navigation = null;
            if (Kf8Parser == null) return;
            await Kf8Parser.ParseAsync().ConfigureAwait(false);
            if (Kf8Parser.Ncx is { Count: > 0 }) Navigation = new NcxNavigation(Kf8Parser.Ncx);
        }
        finally { _operationLock.Release(); }
    }

    public async Task LoadImagesAsync(Stream? hdStream = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            ClearImages();
            var exthHeader = _mobiHeader.ExthHeader;
            var lastResource = IsDualMobi ? (ushort)(_kf8RecordBase - 2) : ushort.MaxValue;
            (uint First, ushort Last)[] resourceRanges = IsDualMobi && _kf8FirstImageIndex != uint.MaxValue
                ? [(_mobiHeader.FirstImageIndex, lastResource), (_kf8FirstImageIndex, ushort.MaxValue)]
                : [(_mobiHeader.FirstImageIndex, lastResource)];
            PageRecords = new PageRecords(
                _stream, _pdbHeader.Records, SdDataOffset,
                resourceRanges,
                exthHeader?.CoverOffset ?? uint.MaxValue, exthHeader?.ThumbOffset ?? uint.MaxValue);

            await PageRecords.AnalyzeAsync().ConfigureAwait(false);
            if (hdStream != null)
            {
                await LoadHdContainerAsync(hdStream).ConfigureAwait(false);
                await HdContainer!.AnalyzeHdAsync().ConfigureAwait(false);
            }
            MergeImages();
        }
        catch { ClearImages(); throw; }
        finally { _operationLock.Release(); }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new MobiMetadataException($"Cannot access images before reading metadata. Call {nameof(ReadMetadataAsync)} first.");

        if (_pdbHeader.SkipRecords)
            throw new MobiMetadataException($"Cannot load images for '{_mobiHeader.FullName}': PDB records were skipped during reading.");

        if (_mobiHeader.SkipProperties)
            throw new MobiMetadataException($"Cannot load images for '{_mobiHeader.FullName}': MOBI properties were skipped during reading.");

    }

    private async Task LoadHdContainerAsync(Stream hdStream)
    {
        var exthHeader = _mobiHeader.ExthHeader;
        var hdPdb = new PDBHead();
        var gate = StreamAccess.GetLock(hdStream);
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            hdStream.Position = 0;
            await hdPdb.ReadHeaderAsync(hdStream).ConfigureAwait(false);

            if (!hdPdb.IsHdImageContainer)
                throw new MobiMetadataException($"Invalid HD image container: expected RBIN/CONT format but got '{hdPdb.TypeName}/{hdPdb.CreatorName}'.");

            Azw6Header = new Azw6Head(skipExthHeader: true) { RecordEndPosition = hdPdb.FirstRecordEndPosition };
            await Azw6Header.ReadHeaderAsync(hdStream).ConfigureAwait(false);

            if (Azw6Header.Title != _mobiHeader.FullName)
                throw new MobiMetadataException($"HD container title mismatch: HD title '{Azw6Header.Title}' does not match book title '{_mobiHeader.FullName}'.");
        }
        finally { gate.Release(); }

        HdContainer = new PageRecords(
            hdStream, hdPdb.Records, HdDataOffset,
            1, (ushort)(hdPdb.Records.Length - 1),
            exthHeader?.CoverOffset ?? uint.MaxValue, exthHeader?.ThumbOffset ?? uint.MaxValue);
    }

    private void MergeImages()
    {
        if (HdContainer?.Cover != null && !HdContainer.Cover.IsCresPlaceholder)
        {
            Cover = HdContainer.Cover;
        }
        else if (PageRecords?.Cover != null)
        {
            Cover = PageRecords.Cover;
        }

        var count = PageRecords?.Images.Count ?? 0;
        var mergedImages = new List<PageRecord>(count);
        var hdImages = HdContainer?.Images.ToDictionary(r => r.ResourceIndex);

        for (int i = 0; i < count; i++)
        {
            if (PageRecords != null && hdImages != null && hdImages.TryGetValue(PageRecords.Images[i].ResourceIndex, out var hd) && !hd.IsCresPlaceholder)
            {
                mergedImages.Add(hd);
            }
            else if (PageRecords != null)
            {
                mergedImages.Add(PageRecords.Images[i]);
            }
        }

        _images = mergedImages;

    }

    public bool HasHdCover() => HdContainer?.Cover is { IsCresPlaceholder: false };
    public bool HasSdCover() => PageRecords?.Cover != null;
    public bool IsHdImage(int index)
    {
        if ((uint)index >= _images.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return HdContainer != null && HdContainer.Images.Contains(_images[index]);
    }

    public int PageCount => PageRecords?.PageCount ?? 0;

    public async Task<byte[]> GetImageBytesAsync(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (PageRecords == null)
            throw new MobiMetadataException($"Cannot get image bytes: call {nameof(LoadImagesAsync)} first.");
        if ((uint)pageIndex >= _images.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return await _images[pageIndex].GetImageBytesAsync().ConfigureAwait(false);
    }

    public Stream OpenImageStream(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (PageRecords == null)
            throw new MobiMetadataException($"Cannot open image stream: call {nameof(LoadImagesAsync)} first.");
        if ((uint)pageIndex >= _images.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return _images[pageIndex].OpenImageStream();
    }

    public async Task<byte[]?> GetCoverBytesAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Cover != null)
            return await Cover.GetImageBytesAsync().ConfigureAwait(false);
        if (PageRecords != null)
            return await PageRecords.GetCoverBytesAsync().ConfigureAwait(false);
        return null;
    }

    public Stream? OpenCoverStream()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return Cover != null
            ? Cover.OpenImageStream()
            : PageRecords?.OpenCoverStream();
    }
}
