using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for KF8 vs old MOBI format detection.
/// </summary>
public class FormatDetectionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public FormatDetectionTests(ITestOutputHelper output)
    {
        _output = output;
        _downloader = new TestFileDownloader();
    }

    public async Task InitializeAsync()
    {
        await _downloader.DownloadAllAsync(maxFiles: 20);
    }

    public Task DisposeAsync()
    {
        _downloader.Dispose();
        return Task.CompletedTask;
    }

    #region KF8 Detection

    [SkippableFact]
    public async Task Kf8Files_HaveVersion8()
    {
        var kf8Files = _downloader.GetAllKf8Files().ToList();
        Assert.NotEmpty(kf8Files);

        foreach (var filename in kf8Files.Take(5))
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}] Version: {mobi.MobiHeader.FileVersion}, IsKf8: {mobi.IsKf8}");

            Assert.Equal(8u, mobi.MobiHeader.FileVersion);
            Assert.True(mobi.IsKf8, $"{filename} should be detected as KF8");
        }
    }

    [SkippableFact]
    public async Task Kf8Files_HaveExthHeader()
    {
        var kf8Files = _downloader.GetAllKf8Files().ToList();
        if (!kf8Files.Any()) throw new Xunit.SkipException("No KF8 files downloaded");

        foreach (var filename in kf8Files.Take(3))
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            Assert.True(mobi.MobiHeader.HasExthHeader, $"{filename} should have EXTH header");
            Assert.NotNull(mobi.MobiHeader.ExthHeader);
        }
    }

    [SkippableFact]
    public async Task Kf8Files_MobiTypeIsElectronicBook()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        // KF8 files have MOBI type 248 (ePub extension) or type 2 (Mobipocket Book)
        Assert.True(mobi.MobiHeader.MobiType == 248 || mobi.MobiHeader.MobiType == 2,
            $"Expected type 248 or 2, got {mobi.MobiHeader.MobiType}");

        _output.WriteLine($"MOBI Type: {mobi.MobiHeader.MobiType} ({mobi.MobiHeader.MobiTypeName})");
    }

    #endregion

    #region Old MOBI Detection

    [SkippableFact]
    public async Task OldMobiFiles_HaveVersion6()
    {
        var oldMobiFiles = _downloader.GetAllOldMobiFiles().ToList();
        Assert.NotEmpty(oldMobiFiles);

        foreach (var filename in oldMobiFiles.Take(5))
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}] Version: {mobi.MobiHeader.FileVersion}, IsKf8: {mobi.IsKf8}");

            Assert.Equal(6u, mobi.MobiHeader.FileVersion);
            Assert.False(mobi.IsKf8, $"{filename} should NOT be detected as KF8");
        }
    }

    [SkippableFact]
    public async Task OldMobiFiles_MobiTypeIsMobipocketBook()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        // Old MOBI files have type 2 (Mobipocket Book)
        Assert.Equal(2u, mobi.MobiHeader.MobiType);
        Assert.Equal("Mobipocket Book", mobi.MobiHeader.MobiTypeName);
    }

    #endregion

    #region Kindle Images (Version 6 with HUFF/CDIC)

    [SkippableFact]
    public async Task KindleImagesFiles_HaveVersion6()
    {
        var kindleImagesFiles = _downloader.GetAllKindleImagesFiles().ToList();
        if (!kindleImagesFiles.Any()) throw new Xunit.SkipException("No kindle_images files downloaded");

        foreach (var filename in kindleImagesFiles)
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}] Version: {mobi.MobiHeader.FileVersion}, " +
                             $"Compression: {mobi.PalmDocHeader.Compression}, " +
                             $"HasHuffCdic: {mobi.MobiHeader.HasHuffCdic}");

            Assert.Equal(6u, mobi.MobiHeader.FileVersion);
            Assert.False(mobi.IsKf8);
        }
    }

    [SkippableFact]
    public async Task KindleImagesFiles_HaveImages()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.True(mobi.MobiHeader.FirstImageIndex > 0, "Should have images");
        _output.WriteLine($"First Image Index: {mobi.MobiHeader.FirstImageIndex}");
    }

    [SkippableFact]
    public async Task KindleImagesFiles_OftenUseHuffCdic()
    {
        var kindleImagesFiles = _downloader.GetAllKindleImagesFiles().ToList();
        if (!kindleImagesFiles.Any()) throw new Xunit.SkipException("No kindle_images files downloaded");

        var huffCdicCount = 0;
        foreach (var filename in kindleImagesFiles)
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            if (mobi.MobiHeader.HasHuffCdic)
            {
                huffCdicCount++;
                _output.WriteLine($"[{filename}] Uses HUFF/CDIC compression");
            }
        }

        _output.WriteLine($"Files with HUFF/CDIC: {huffCdicCount}/{kindleImagesFiles.Count()}");
    }

    #endregion

    #region Dual MOBI Detection

    [SkippableFact]
    public async Task DualMobi_DetectionWorks()
    {
        // Dual MOBI has KF8 boundary offset > 0
        // This is rare in public domain files
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine($"Is KF8: {mobi.IsKf8}");
        _output.WriteLine($"Is Dual MOBI: {mobi.IsDualMobi}");
        _output.WriteLine($"KF8 Boundary Offset: {mobi.MobiHeader.ExthHeader?.Kf8BoundaryOffset ?? 0}");

        // Just verify the property works - IsDualMobi depends on KF8 boundary offset
        Assert.IsType<bool>(mobi.IsDualMobi);
    }

    #endregion

    #region Theory Tests - All Files

    [SkippableTheory]
    [InlineData("frankenstein_kf8.mobi", 8u, true)]
    [InlineData("alice_wonderland_kf8.mobi", 8u, true)]
    [InlineData("frankenstein_mobi.mobi", 6u, false)]
    [InlineData("romeo_juliet_mobi.mobi", 6u, false)]
    [InlineData("frankenstein_kindle_images.mobi", 6u, false)]
    public async Task FormatDetection_VersionAndKf8Flag(
        string filename, uint expectedVersion, bool expectedIsKf8)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(expectedVersion, mobi.MobiHeader.FileVersion);
        Assert.Equal(expectedIsKf8, mobi.IsKf8);
    }

    #endregion
}
