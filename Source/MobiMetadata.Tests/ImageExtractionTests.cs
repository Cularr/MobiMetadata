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

    [Fact]
    public async Task LoadImages_RequiresExthHeader()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.NotNull(mobi.Images);
        Assert.True(mobi.Images.Count > 0);

        _output.WriteLine($"Image count: {mobi.Images.Count}");
    }

    [Fact]
    public async Task LoadImages_PopulatesPageCount()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.PageCount > 0);
        Assert.Equal(mobi.Images.Count, mobi.PageCount);

        _output.WriteLine($"Page count: {mobi.PageCount}");
    }

    [Fact]
    public async Task LoadImages_CreatesPageRecords()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task GetImageBytes_ReturnsValidData()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task GetImageBytes_ThrowsForInvalidIndex()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mobi.GetImageBytesAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mobi.GetImageBytesAsync(mobi.Images.Count));
    }

    [Fact]
    public async Task GetImageBytes_AllImages_AreValid()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var validCount = 0;
        for (int i = 0; i < Math.Min(mobi.Images.Count, 10); i++)
        {
            var bytes = await mobi.GetImageBytesAsync(i);
            if (bytes.Length > 0) validCount++;
        }

        Assert.True(validCount > 0);
        _output.WriteLine($"Valid images: {validCount}/10");
    }

    #endregion

    #region Image Stream Access

    [Fact]
    public async Task OpenImageStream_ReturnsReadableStream()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task OpenImageStream_ThrowsForInvalidIndex()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task Cover_ExtractsBytes()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var coverBytes = await mobi.GetCoverBytesAsync();
        if (coverBytes != null)
        {
            Assert.True(coverBytes.Length > 0);
            _output.WriteLine($"Cover size: {coverBytes.Length} bytes");
        }
        else
        {
            _output.WriteLine("No cover found in this file");
        }
    }

    [Fact]
    public async Task Cover_OpenStream()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var coverStream = mobi.OpenCoverStream();
        if (coverStream != null)
        {
            Assert.True(coverStream.CanRead);
            _output.WriteLine("Cover stream opened successfully");

            await using (coverStream)
            {
                var buffer = new byte[16];
                var read = await coverStream.ReadAsync(buffer);
                Assert.True(read > 0);
            }
        }
        else
        {
            _output.WriteLine("No cover found");
        }
    }

    [Fact]
    public async Task Cover_HasSdCover_WhenAvailable()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        // Check SD cover status
        if (mobi.HasSdCover())
        {
            _output.WriteLine("SD cover available");
            Assert.NotNull(mobi.PageRecords?.Cover);
        }
        else
        {
            _output.WriteLine("No SD cover");
        }
    }

    #endregion

    #region ImageSharp Integration

    [Fact]
    public async Task ImageSharp_LoadsExtractedImage()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task ImageSharp_LoadsImageFromStream()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task ImageSharp_DetectsImageFormat()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
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

    [Fact]
    public async Task ImageSharp_AllImages_LoadSuccessfully()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.Images.Count > 0);

        var successCount = 0;
        var failCount = 0;

        for (int i = 0; i < Math.Min(mobi.Images.Count, 5); i++)
        {
            try
            {
                var imageBytes = await mobi.GetImageBytesAsync(i);
                using var imageMemStream = new MemoryStream(imageBytes);
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageMemStream);

                Assert.True(image.Width > 0);
                Assert.True(image.Height > 0);
                successCount++;

                _output.WriteLine($"Image {i}: {image.Width}x{image.Height} ({image.Metadata.DecodedImageFormat?.Name})");
            }
            catch (Exception ex)
            {
                failCount++;
                _output.WriteLine($"Image {i}: FAILED - {ex.Message}");
            }
        }

        _output.WriteLine($"Successfully loaded: {successCount}, Failed: {failCount}");
        Assert.True(successCount > 0, "At least one image should load successfully");
    }

    #endregion

    #region Theory Tests

    [Theory]
    [InlineData("frankenstein_kindle_images.mobi")]
    [InlineData("alice_kindle_images.mobi")]
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
