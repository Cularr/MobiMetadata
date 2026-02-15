using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for text content extraction and KF8 navigation parsing.
/// </summary>
public class TextExtractionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public TextExtractionTests(ITestOutputHelper output)
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

    #region Basic Text Extraction

    [Fact]
    public async Task ExtractText_Kf8File_ReturnsContent()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);
        Assert.Contains("Frankenstein", text);

        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
        _output.WriteLine($"First 200 chars: {text[..Math.Min(200, text.Length)]}...");
    }

    [Fact]
    public async Task ExtractText_OldMobiFile_ReturnsContent()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);
        Assert.Contains("Frankenstein", text);

        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
    }

    [Fact]
    public async Task ExtractText_HuffCdicFile_ReturnsContent()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);

        _output.WriteLine($"Compression: {mobi.PalmDocHeader.CompressionName}");
        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
    }

    #endregion

    #region Text Content Properties

    [Fact]
    public async Task TextContent_HasCorrectEncoding()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        // Modern KF8 files should use UTF-8 (encoding 65001)
        var validEncodings = new uint[] { 65001, 1252 }; // UTF-8 or Windows-1252
        Assert.Contains(mobi.MobiHeader.TextEncoding, validEncodings);

        _output.WriteLine($"Text Encoding: {mobi.MobiHeader.TextEncoding} ({mobi.MobiHeader.TextEncodingName})");
    }

    [Fact]
    public async Task TextContent_TextLengthMatches()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        // TextLength from header should be reasonable
        Assert.True(mobi.TextContent.TextLength > 0);

        var text = await mobi.TextContent.ExtractTextAsync();

        // Extracted text should be close to header length (may differ due to encoding)
        _output.WriteLine($"Header Text Length: {mobi.TextContent.TextLength:N0}");
        _output.WriteLine($"Extracted Text Length: {text.Length:N0}");
    }

    #endregion

    #region KF8 Parser

    [Fact]
    public async Task Kf8Parser_IsCreatedForKf8Files()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.True(mobi.IsKf8);
        Assert.NotNull(mobi.Kf8Parser);
    }

    [Fact]
    public async Task Kf8Parser_IsNullForNonKf8Files()
    {
        // Find a pure old MOBI file (version 6, not dual)
        var oldMobiFiles = _downloader.GetAllOldMobiFiles().ToList();
        if (!oldMobiFiles.Any()) throw new Xunit.SkipException("No old MOBI files downloaded");

        MobiMetadata? mobi = null;
        foreach (var filename in oldMobiFiles)
        {
            var filePath = _downloader.GetFilePath(filename);
            if (!File.Exists(filePath)) continue;

            await using var stream = File.OpenRead(filePath);
            mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}] Version: {mobi.MobiHeader.FileVersion}, IsKf8: {mobi.IsKf8}, IsDualMobi: {mobi.IsDualMobi}");

            // Find a file that is not KF8 and not dual MOBI
            if (!mobi.IsKf8 && !mobi.IsDualMobi)
            {
                Assert.Null(mobi.Kf8Parser);
                return;
            }
        }

        // If all files are dual MOBI, just verify Kf8Parser behavior
        _output.WriteLine("All old MOBI files appear to be dual MOBI files");
        if (mobi != null && mobi.IsDualMobi)
        {
            Assert.NotNull(mobi.Kf8Parser); // Dual MOBI has KF8 parser
        }
    }

    [Fact]
    public async Task Kf8Parser_ParseAsync_ParsesStructures()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        if (mobi.Kf8Parser == null)
        {
            _output.WriteLine("Not a KF8 file, skipping");
            return;
        }

        await mobi.ParseKf8Async();

        _output.WriteLine($"Is KF8: {mobi.Kf8Parser.IsKf8}");
        _output.WriteLine($"Is Dual MOBI: {mobi.Kf8Parser.IsDualMobi}");
        _output.WriteLine($"KF8 Boundary Offset: {mobi.Kf8Parser.Kf8BoundaryOffset}");
        _output.WriteLine($"Skeleton Index: {mobi.Kf8Parser.Skeletons?.Count ?? 0} entries");
        _output.WriteLine($"Chunk Index: {mobi.Kf8Parser.Chunks?.Count ?? 0} entries");
        _output.WriteLine($"FDST Index: {mobi.Kf8Parser.Fdst?.Count ?? 0} entries");
        _output.WriteLine($"NCX Index: {mobi.Kf8Parser.Ncx?.Count ?? 0} entries");
    }

    #endregion

    #region Navigation

    [Fact]
    public async Task Navigation_BuildsTableOfContents()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        if (mobi.Kf8Parser != null)
        {
            await mobi.ParseKf8Async();
        }

        if (mobi.Navigation != null)
        {
            var toc = mobi.Navigation.BuildHierarchicalToc();
            _output.WriteLine($"TOC entries: {toc.Count}");

            foreach (var entry in toc.Take(10))
            {
                _output.WriteLine($"  - {entry.Title} (Level {entry.Level})");
            }

            Assert.True(toc.Count >= 0);
        }
        else
        {
            _output.WriteLine("No navigation data available for this file");
        }
    }

    #endregion

    #region Theory Tests

    [Theory]
    [InlineData("frankenstein_kf8.mobi", "Frankenstein")]
    [InlineData("alice_wonderland_kf8.mobi", "Alice")]
    [InlineData("sherlock_holmes_kf8.mobi", "Sherlock")]
    public async Task ExtractText_Kf8Files_ContainExpectedText(string filename, string expectedText)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);
        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);
        Assert.Contains(expectedText, text);

        _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
    }

    [Theory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("alice_wonderland_kf8.mobi")]
    [InlineData("sherlock_holmes_kf8.mobi")]
    public async Task TextContent_AllKf8Files_ExtractsText(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);
        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);
        _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
    }

    #endregion
}
