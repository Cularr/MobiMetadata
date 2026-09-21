namespace MobiMetadata;

public class PageRecords
{
    private List<PageRecord> _records = [];
    private List<PageRecord> _images = [];
    private List<PageRecord> _rest = [];
    private List<PageRecord> _fonts = [];

    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public IReadOnlyList<PageRecord> Images => _images;
    public IReadOnlyList<PageRecord> Rest => _rest;
    public IReadOnlyList<PageRecord> Fonts => _fonts;
    public PageRecord? Cover { get; private set; }
    public PageRecord? Thumbnail { get; private set; }
    public PageRecord? Datp { get; private set; }
    public PageRecord? KindleEmbed { get; private set; }
    public RescRecord? Resc { get; private set; }

    public PageRecords(
        Stream stream,
        PDBRecordInfo[] pdbRecords,
        int dataOffset,
        uint firstImageIndex,
        ushort lastImageIndex,
        uint coverOffset,
        uint thumbOffset)
    {
        var safeLastIndex = Math.Min((int)lastImageIndex, pdbRecords.Length - 1);
        var coverIndex = firstImageIndex + coverOffset;
        var thumbIndex = firstImageIndex + thumbOffset;

        for (var i = (long)firstImageIndex; i <= safeLastIndex; i++)
        {
            var record = pdbRecords[i];
            var endOffset = i + 1 < pdbRecords.Length ? pdbRecords[i + 1].DataOffset : stream.Length;
            var recordOffset = record.DataOffset;
            if (endOffset < recordOffset || endOffset > stream.Length)
                throw new MobiMetadataException("Image record offsets are outside the stream or out of order.");
            var length = checked((uint)(endOffset - recordOffset));

            var pageRecord = new PageRecord(stream, recordOffset, length, dataOffset, _semaphore);

            if (coverOffset > 0 && i == coverIndex)
            {
                Cover = pageRecord;
            }
            else if (thumbOffset > 0 && i == thumbIndex)
            {
                Thumbnail = pageRecord;
            }
            else
            {
                _records.Add(pageRecord);
            }
        }
    }

    public async Task AnalyzeAsync()
    {
        var (rescIndex, resc) = await FindRescIndexAsync();

        if (rescIndex == -1 || resc == null)
        {
            _images = [];
            foreach (var record in _records)
            {
                if (await record.IsStructuralAsync().ConfigureAwait(false))
                    _rest.Add(record);
                else
                    _images.Add(record);
            }
            return;
        }

        Resc = resc;
        await resc.ParseAsync().ConfigureAwait(false);
        _records.RemoveAt(rescIndex);

        await RemoveDatpFromEndIfNeededAsync(resc).ConfigureAwait(false);

        if (_records.Count > resc.PageCount)
        {
            await SeparateRestRecordsAsync(resc.PageCount).ConfigureAwait(false);
            _images = _records.Take(resc.PageCount).ToList();
        }
        else
        {
            _images = [.. _records];
        }

        ValidateImageCount(resc);
    }

    public async Task AnalyzeHdAsync(int expectedCount)
    {
        if (_records.Count > expectedCount)
        {
            await SeparateHdRestRecordsAsync(expectedCount).ConfigureAwait(false);
            _images = _records.Take(expectedCount).ToList();
        }
        else
        {
            _images = [.. _records];
        }

        if (_images.Count != expectedCount)
            throw new MobiMetadataException($"HD count {_images.Count} != SD count {expectedCount}");
    }

    public async Task SeparateFontsAsync()
    {
        var fonts = new List<PageRecord>();
        var remainingImages = new List<PageRecord>();

        for (int i = _images.Count - 1; i >= 0; i--)
        {
            if (await _images[i].IsFontAsync().ConfigureAwait(false))
            {
                fonts.Add(_images[i]);
                Resc?.AdjustPageCount(-1);
            }
            else
            {
                remainingImages.Add(_images[i]);
            }
        }

        if (fonts.Count > 0)
        {
            _fonts = fonts;
            _images = Enumerable.Reverse(remainingImages).ToList();
        }
    }

    public int PageCount => _images.Count;

    public async Task<byte[]> GetImageBytesAsync(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _images.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return await _images[pageIndex].GetImageBytesAsync().ConfigureAwait(false);
    }

    public Stream OpenImageStream(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _images.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return _images[pageIndex].OpenImageStream();
    }

    public async Task<byte[]?> GetCoverBytesAsync()
    {
        if (Cover == null) return null;
        return await Cover.GetImageBytesAsync().ConfigureAwait(false);
    }

    public Stream? OpenCoverStream() => Cover?.OpenImageStream();

    private async Task<(int Index, RescRecord? Resc)> FindRescIndexAsync()
    {
        for (var i = _records.Count - 1; i >= 0; i--)
        {
            var rescRecord = await _records[i].GetRescRecordAsync().ConfigureAwait(false);
            if (rescRecord != null)
            {
                return (i, rescRecord);
            }
        }
        return (-1, null);
    }

    private async Task RemoveDatpFromEndIfNeededAsync(RescRecord resc)
    {
        if (_records.Count == resc.PageCount && _records.Count > 0)
        {
            var lastIdx = _records.Count - 1;
            if (await _records[lastIdx].IsDatpAsync().ConfigureAwait(false))
            {
                Datp = _records[lastIdx];
                _records.RemoveAt(lastIdx);
                resc.AdjustPageCount(-1);
            }
        }
    }

    private async Task SeparateRestRecordsAsync(int imageCount)
    {
        var rest = _records.Skip(imageCount).ToList();
        var remainingRest = new List<PageRecord>();

        foreach (var record in rest)
        {
            if (await record.IsDatpAsync().ConfigureAwait(false))
            {
                Datp = record;
            }
            else
            {
                remainingRest.Add(record);
            }
        }

        _rest = remainingRest;
    }

    private async Task SeparateHdRestRecordsAsync(int expectedCount)
    {
        var rest = _records.Skip(expectedCount).ToList();
        var remainingRest = new List<PageRecord>();

        foreach (var record in rest)
        {
            if (await record.IsKindleEmbedAsync().ConfigureAwait(false))
            {
                KindleEmbed = record;
            }
            else
            {
                remainingRest.Add(record);
            }
        }

        _rest = remainingRest;
    }

    private void ValidateImageCount(RescRecord resc)
    {
        if (_images.Count < resc.PageCount)
            throw new MobiMetadataException($"Image count {_images.Count} < RESC page count {resc.PageCount}");
    }
}
