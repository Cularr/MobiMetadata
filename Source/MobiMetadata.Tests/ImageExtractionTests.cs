using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for image extraction functionality.
/// </summary>
public class ImageExtractionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public ImageExtractionTests(ITestOutputHelper output)
    {
        _output = output;
        _downloader = new TestFileDownloader();
    }

    public async Task InitializeAsync()
    {
        await _downloader.DownloadAllAsync(maxFiles: 15);
    }

    public Task DisposeAsync()
    {
        _downloader.Dispose();
        return Task.CompletedTask;
    }

    #region Basic Image Loading

    [SkippableFact]
    public async Task LoadImages_RequiresExthHeader()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.NotNull(mobi.Images);
        Assert.True(mobi.Images.Count > 0);

        _output.WriteLine($"Image count: {mobi.Images.Count}");
    }

    [SkippableFact]
    public async Task LoadImages_PopulatesPageCount()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.PageCount > 0);
        Assert.Equal(mobi.Images.Count, mobi.PageCount);

        _output.WriteLine($"Page count: {mobi.PageCount}");
    }

    [SkippableFact]
    public async Task LoadImages_CreatesPageRecords()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.NotNull(mobi.PageRecords);
        Assert.NotNull(mobi.PageRecords.Images);
    }

    #endregion

    #region Image Bytes Access

    [SkippableFact]
    public async Task GetImageBytes_ReturnsValidData()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);

        var imageBytes = await mobi.GetImageBytesAsync(0);
        Assert.NotNull(imageBytes);
        Assert.True(imageBytes.Length > 0);

        _output.WriteLine($"First image size: {imageBytes.Length} bytes");
    }

    [SkippableFact]
    public async Task GetImageBytes_ThrowsForInvalidIndex()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mobi.GetImageBytesAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mobi.GetImageBytesAsync(mobi.Images.Count));
    }

    [SkippableFact]
    public async Task GetImageBytes_AllImages_AreValid()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.NotEmpty(mobi.Images);

        for (int i = 0; i < mobi.Images.Count; i++)
        {
            var bytes = await mobi.GetImageBytesAsync(i);
            Assert.NotEmpty(bytes);
        }

        _output.WriteLine($"Valid images: {mobi.Images.Count}");
    }

    #endregion

    #region Image Stream Access

    [SkippableFact]
    public async Task OpenImageStream_ReturnsReadableStream()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);

        using var imageStream = mobi.OpenImageStream(0);
        Assert.NotNull(imageStream);
        Assert.True(imageStream.CanRead);

        var buffer = new byte[16];
        var read = await imageStream.ReadAsync(buffer);
        Assert.True(read > 0);

        _output.WriteLine($"Read {read} bytes from image stream");
    }

    [SkippableFact]
    public async Task OpenImageStream_ThrowsForInvalidIndex()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => mobi.OpenImageStream(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => mobi.OpenImageStream(mobi.Images.Count));
    }

    #endregion

    #region Cover Extraction

    [SkippableFact]
    public async Task Images_CoverAndThumbnailOnlyBook_HasNoPages()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.Equal(0, mobi.PageCount);
        Assert.Empty(mobi.Images);
        Assert.NotNull(mobi.Cover);
        Assert.NotNull(mobi.PageRecords?.Thumbnail);
        Assert.NotEmpty((await mobi.GetCoverBytesAsync())!);
    }

    [SkippableFact]
    public async Task Cover_ExtractsBytes()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var coverBytes = await mobi.GetCoverBytesAsync();
        Assert.NotNull(coverBytes);

        using var image = SixLabors.ImageSharp.Image.Load(coverBytes);
        Assert.True(image.Width > 0);
        _output.WriteLine($"Cover size: {coverBytes.Length} bytes");
    }

    [SkippableFact]
    public async Task Cover_OpenStream()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        await using var coverStream = mobi.OpenCoverStream();
        Assert.NotNull(coverStream);
        Assert.True(coverStream.CanRead);

        var buffer = new byte[16];
        var read = await coverStream.ReadAsync(buffer);
        Assert.True(read > 0);
    }

    [SkippableFact]
    public async Task Cover_HasSdCover()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.HasSdCover());
        Assert.NotNull(mobi.PageRecords?.Cover);
    }

    #endregion

    #region ImageSharp Integration

    [SkippableFact]
    public async Task ImageSharp_LoadsExtractedImage()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);

        var imageBytes = await mobi.GetImageBytesAsync(0);
        Assert.NotNull(imageBytes);

        using var imageStream = new MemoryStream(imageBytes);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageStream);

        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);

        _output.WriteLine($"Image dimensions: {image.Width}x{image.Height}");
        _output.WriteLine($"Image format: {image.Metadata.DecodedImageFormat?.Name ?? "unknown"}");
    }

    [SkippableFact]
    public async Task ImageSharp_LoadsImageFromStream()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);

        using var imageStream = mobi.OpenImageStream(0);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageStream);

        Assert.NotNull(image);
        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);

        _output.WriteLine($"Image dimensions: {image.Width}x{image.Height}");
    }

    [SkippableFact]
    public async Task ImageSharp_DetectsImageFormat()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var imageBytes = await mobi.GetImageBytesAsync(0);
        using var imageStream = new MemoryStream(imageBytes);
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageStream);

        var format = image.Metadata.DecodedImageFormat;
        Assert.NotNull(format);

        _output.WriteLine($"Detected format: {format.Name}");
        _output.WriteLine($"Default extension: {format.FileExtensions.FirstOrDefault()}");
    }

    [SkippableFact]
    public async Task ImageSharp_AllImages_LoadSuccessfully()
    {
        var filePath = _downloader.GetFilePath("jane_eyre_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.NotEmpty(mobi.Images);

        for (int i = 0; i < mobi.Images.Count; i++)
        {
            var imageBytes = await mobi.GetImageBytesAsync(i);
            using var imageMemStream = new MemoryStream(imageBytes);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageMemStream);

            Assert.True(image.Width > 0);
            Assert.True(image.Height > 0);
            _output.WriteLine($"Image {i}: {image.Width}x{image.Height} ({image.Metadata.DecodedImageFormat?.Name})");
        }
    }

    #endregion

    #region Theory Tests

    [SkippableTheory]
    [InlineData("jane_eyre_kf8.mobi")]
    [InlineData("pride_prejudice_kf8.mobi")]
    public async Task LoadImages_AllKindleImagesFiles_LoadSuccessfully(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);
        _output.WriteLine($"[{filename}] Images: {mobi.Images.Count}");
    }

    #endregion
}
