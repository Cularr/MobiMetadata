namespace MobiMetadata;

public class PageRecords
{
    private readonly List<PageRecord> _records = [];
    private readonly List<PageRecord> _images = [];
    private readonly List<PageRecord> _rest = [];
    private readonly List<PageRecord> _fonts = [];

    public IReadOnlyList<PageRecord> Images => _images;
    public IReadOnlyList<PageRecord> Rest => _rest;
    public IReadOnlyList<PageRecord> Fonts => _fonts;
    public PageRecord? Cover { get; private set; }
    public PageRecord? Thumbnail { get; private set; }
    public PageRecord? Datp { get; private set; }
    public PageRecord? KindleEmbed { get; private set; }
    public RescRecord? Resc { get; private set; }

    public PageRecords(Stream stream, PDBRecordInfo[] pdbRecords, int dataOffset,
        uint firstImageIndex, ushort lastImageIndex, uint coverOffset, uint thumbOffset)
        : this(stream, pdbRecords, dataOffset, [(firstImageIndex, lastImageIndex)], coverOffset, thumbOffset) { }

    internal PageRecords(Stream stream, PDBRecordInfo[] pdbRecords, int dataOffset,
        (uint First, ushort Last)[] ranges, uint coverOffset, uint thumbOffset)
    {
        uint resourceIndex = 0;
        foreach (var range in ranges)
        for (var i = (long)range.First; i <= Math.Min((int)range.Last, pdbRecords.Length - 1); i++)
        {
            long start = pdbRecords[i].DataOffset;
            long end = i + 1 < pdbRecords.Length ? pdbRecords[i + 1].DataOffset : stream.Length;
            StreamAccess.ValidateRange(stream, start, end - start);
            // Short structural records in an HD container have no CRES payload header.
            var payloadOffset = dataOffset;
            if (dataOffset != 0 && end - start < dataOffset && end - start != 4)
                payloadOffset = 0;
            var record = new PageRecord(stream, start, checked((uint)(end - start)), payloadOffset)
            {
                ResourceIndex = resourceIndex++
            };
            if (coverOffset != uint.MaxValue && record.ResourceIndex == coverOffset) Cover = record;
            if (thumbOffset != uint.MaxValue && record.ResourceIndex == thumbOffset) Thumbnail = record;
            _records.Add(record);
        }
    }

    public Task AnalyzeAsync() => AnalyzeRecordsAsync(false);

    // Keep placeholders and pair by resource index, before filtering fonts.
    public Task AnalyzeHdAsync() => AnalyzeRecordsAsync(true);

    private async Task AnalyzeRecordsAsync(bool hd)
    {
        _images.Clear();
        _rest.Clear();
        _fonts.Clear();
        Resc = null;
        Datp = null;
        KindleEmbed = null;
        bool? imageContainer = null;
        foreach (var rawRecord in _records)
        {
            var record = rawRecord;
            bool usable = false;
            if (!hd && await record.MatchSignatureAsync("CONT"u8.ToArray()).ConfigureAwait(false))
            {
                imageContainer = record.Size == 12 && await record.MatchSignatureAsync("CONTBOUNDARY"u8.ToArray()).ConfigureAwait(false)
                    ? null : await record.IsImageContainerAsync().ConfigureAwait(false);
            }
            else if (!hd && imageContainer.HasValue)
            {
                if (imageContainer.Value && record.Size >= 12 && await record.IsCresAsync().ConfigureAwait(false))
                {
                    record = record.WithPayloadOffset(12);
                    usable = await record.IsImageAsync().ConfigureAwait(false);
                }
            }
            else usable = await IsUsableImageAsync(record, hd).ConfigureAwait(false);

            bool isCover = Cover?.ResourceIndex == record.ResourceIndex;
            bool isThumbnail = Thumbnail?.ResourceIndex == record.ResourceIndex;
            if (isCover || isThumbnail)
            {
                if (!usable) throw new MobiMetadataException("Cover or thumbnail record is not an image resource.");
                if (isCover) Cover = record;
                if (isThumbnail) Thumbnail = record;
                continue;
            }
            var resc = await record.GetRescRecordAsync().ConfigureAwait(false);
            if (resc != null)
            {
                await resc.ParseAsync().ConfigureAwait(false);
                Resc = resc;
                _rest.Add(record);
            }
            else if (await record.IsFontAsync().ConfigureAwait(false)) _fonts.Add(record);
            else if (await record.IsDatpAsync().ConfigureAwait(false)) Datp = record;
            else if (await record.IsKindleEmbedAsync().ConfigureAwait(false)) KindleEmbed = record;
            else if (usable) _images.Add(record);
            else _rest.Add(record);
        }
    }

    private static async Task<bool> IsUsableImageAsync(PageRecord record, bool hd)
    {
        if (hd && !await record.IsCresAsync().ConfigureAwait(false)) return false;
        return (hd && record.IsCresPlaceholder) || await record.IsImageAsync().ConfigureAwait(false);
    }

    public int PageCount => _images.Count;

    public async Task<byte[]> GetImageBytesAsync(int pageIndex)
    {
        if ((uint)pageIndex >= _images.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return await _images[pageIndex].GetImageBytesAsync().ConfigureAwait(false);
    }

    public Stream OpenImageStream(int pageIndex)
    {
        if ((uint)pageIndex >= _images.Count) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        return _images[pageIndex].OpenImageStream();
    }

    public async Task<byte[]?> GetCoverBytesAsync() => Cover == null ? null : await Cover.GetImageBytesAsync().ConfigureAwait(false);
    public Stream? OpenCoverStream() => Cover?.OpenImageStream();
}
