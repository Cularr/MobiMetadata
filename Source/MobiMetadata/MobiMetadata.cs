namespace MobiMetadata;

public class MobiMetadata : IDisposable
{
    private const int SdDataOffset = 0;
    private const int HdDataOffset = 12; // Skip CRES + 12-byte header

    private readonly PDBHead _pdbHeader;
    private readonly PalmDOCHead _palmDocHeader;
    private readonly MobiHead _mobiHeader;
    private readonly bool _requireExthHeader;
    private readonly object _lock = new();

    private Stream _stream = null!;
    private volatile bool _isInitialized;
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

    public bool IsKf8 => _mobiHeader.FileVersion >= 8;
    public bool IsDualMobi => _mobiHeader.ExthHeader?.Kf8BoundaryOffset > 0;

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
        if (!_disposed)
        {
            if (disposing)
            {
                // Note: We don't own the streams passed to us, so we don't dispose them
                // The streams are passed externally and should be disposed by the caller
            }
            _disposed = true;
        }
    }

    public async Task ReadMetadataAsync(Stream stream)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_lock)
        {
            _stream = stream;
        }

        await _pdbHeader.ReadHeaderAsync(stream).ConfigureAwait(false);
        await _palmDocHeader.ReadHeaderAsync(stream).ConfigureAwait(false);

        _mobiHeader.PreviousHeaderPosition = _palmDocHeader.StreamPosition;
        await _mobiHeader.ReadHeaderAsync(stream).ConfigureAwait(false);

        if (_mobiHeader.ExthHeader == null && _requireExthHeader)
            throw new MobiMetadataException($"No EXTH header found in '{_mobiHeader.FullName}'");

        lock (_lock)
        {
            TextContent = new TextContent(_stream, _pdbHeader.Records, _palmDocHeader, _mobiHeader);
        }

        if (IsKf8 || IsDualMobi)
        {
            Kf8Parser = new KF8Parser(stream, _pdbHeader.Records, _mobiHeader, _mobiHeader.ExthHeader);
        }

        _isInitialized = true;
    }

    public async Task ParseKf8Async()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Kf8Parser == null) return;

        await Kf8Parser.ParseAsync().ConfigureAwait(false);

        if (Kf8Parser.Ncx is { Count: > 0 })
        {
            Navigation = new NcxNavigation(Kf8Parser.Ncx);
        }
    }

    public async Task LoadImagesAsync(Stream? hdStream = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureInitialized();

        var exthHeader = _mobiHeader.ExthHeader
            ?? throw new MobiMetadataException($"Cannot load images: EXTH header is required but not available for '{_mobiHeader.FullName}'.");

        PageRecords = new PageRecords(
            _stream, _pdbHeader.Records, SdDataOffset,
            _mobiHeader.FirstImageIndex, _mobiHeader.LastContentRecord,
            exthHeader.CoverOffset, exthHeader.ThumbOffset);

        await PageRecords.AnalyzeAsync().ConfigureAwait(false);

        if (hdStream != null)
        {
            await LoadHdContainerAsync(hdStream).ConfigureAwait(false);
            await HdContainer!.AnalyzeHdAsync(PageRecords.Images.Count).ConfigureAwait(false);
        }

        if (exthHeader.BookType != "comic")
        {
            await PageRecords.SeparateFontsAsync().ConfigureAwait(false);
        }

        MergeImages();
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
            throw new MobiMetadataException($"Cannot access images before reading metadata. Call {nameof(ReadMetadataAsync)} first.");

        if (_pdbHeader.SkipRecords)
            throw new MobiMetadataException($"Cannot load images for '{_mobiHeader.FullName}': PDB records were skipped during reading.");

        if (_mobiHeader.SkipProperties)
            throw new MobiMetadataException($"Cannot load images for '{_mobiHeader.FullName}': MOBI properties were skipped during reading.");

        if (_mobiHeader.SkipExthHeader)
            throw new MobiMetadataException($"Cannot load images for '{_mobiHeader.FullName}': EXTH header was skipped during reading.");
    }

    private async Task LoadHdContainerAsync(Stream hdStream)
    {
        var exthHeader = _mobiHeader.ExthHeader
            ?? throw new MobiMetadataException($"Cannot load HD container: EXTH header is required but not available.");

        var hdPdb = new PDBHead();
        await hdPdb.ReadHeaderAsync(hdStream).ConfigureAwait(false);

        if (!hdPdb.IsHdImageContainer)
            throw new MobiMetadataException($"Invalid HD image container: expected RBIN/CONT format but got '{hdPdb.TypeName}/{hdPdb.CreatorName}'.");

        Azw6Header = new Azw6Head(skipExthHeader: true);
        await Azw6Header.ReadHeaderAsync(hdStream).ConfigureAwait(false);

        if (Azw6Header.Title != _mobiHeader.FullName)
            throw new MobiMetadataException($"HD container title mismatch: HD title '{Azw6Header.Title}' does not match book title '{_mobiHeader.FullName}'.");

        HdContainer = new PageRecords(
            hdStream, hdPdb.Records, HdDataOffset,
            1, (ushort)(hdPdb.Records.Length - 1),
            exthHeader.CoverOffset, exthHeader.ThumbOffset);
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

        for (int i = 0; i < count; i++)
        {
            if (HdContainer != null && !HdContainer.Images[i].IsCresPlaceholder)
            {
                mergedImages.Add(HdContainer.Images[i]);
            }
            else if (PageRecords != null)
            {
                mergedImages.Add(PageRecords.Images[i]);
            }
        }

        _images = mergedImages;

        var rescPageCount = PageRecords?.Resc?.PageCount ?? 0;
        if (rescPageCount > 0 && _images.Count != rescPageCount)
        {
            throw new MobiMetadataException($"Image count mismatch in '{_mobiHeader.FullName}': found {_images.Count} images but RESC specifies {rescPageCount} pages.");
        }
    }

    public bool HasHdCover() => HdContainer?.Cover != null;
    public bool HasSdCover() => PageRecords?.Cover != null;
    public bool IsHdImage(int index) => HdContainer != null && !HdContainer.Images[index].IsCresPlaceholder;

    public int PageCount => PageRecords?.PageCount ?? 0;

    public async Task<byte[]> GetImageBytesAsync(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (PageRecords == null)
            throw new MobiMetadataException($"Cannot get image bytes: call {nameof(LoadImagesAsync)} first.");
        return await PageRecords.GetImageBytesAsync(pageIndex).ConfigureAwait(false);
    }

    public Stream OpenImageStream(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (PageRecords == null)
            throw new MobiMetadataException($"Cannot open image stream: call {nameof(LoadImagesAsync)} first.");
        return PageRecords.OpenImageStream(pageIndex);
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
