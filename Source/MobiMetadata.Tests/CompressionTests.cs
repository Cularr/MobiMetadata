using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for compression handling (PalmDOC and HUFF/CDIC).
/// </summary>
public class CompressionTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public CompressionTests(ITestOutputHelper output)
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

    #region Compression Type Detection

    [SkippableFact]
    public async Task Compression_Kf8_DetectedCorrectly()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.True(mobi.IsKf8);
        Assert.Equal(PalmDOCHead.CompressionPalmDoc, mobi.PalmDocHeader.Compression);

        _output.WriteLine($"Compression: {mobi.PalmDocHeader.Compression} ({mobi.PalmDocHeader.CompressionName})");
    }

    [SkippableFact]
    public async Task Compression_PalmDoc_DetectedCorrectly()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(PalmDOCHead.CompressionPalmDoc, mobi.PalmDocHeader.Compression);
        Assert.Equal("PalmDOC", mobi.PalmDocHeader.CompressionName);

        _output.WriteLine($"Compression: {mobi.PalmDocHeader.CompressionName}");
    }

    #endregion

#region Text Decompression

    [SkippableFact]
    public async Task Decompression_PalmDoc_Works()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(PalmDOCHead.CompressionPalmDoc, mobi.PalmDocHeader.Compression);
        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.NotEmpty(text);
        Assert.Contains("Frankenstein", text);

        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
        _output.WriteLine($"First 200 chars: {text[..Math.Min(200, text.Length)]}...");
    }

    [SkippableFact]
    public async Task Decompression_KindleImages_Works()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(PalmDOCHead.CompressionPalmDoc, mobi.PalmDocHeader.Compression);
        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();

        Assert.Contains("Frankenstein", text);
        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
    }

    [SkippableFact]
    public async Task Decompression_Kf8_Works()
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

        _output.WriteLine($"Compression: {mobi.PalmDocHeader.CompressionName}");
        _output.WriteLine($"Extracted text length: {text.Length:N0} characters");
    }

    #endregion

    #region Text Content Properties

    [SkippableFact]
    public async Task TextContent_HasValidProperties()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        _output.WriteLine($"Compression Name: {mobi.TextContent.CompressionName}");
        _output.WriteLine($"Text Encoding: {mobi.TextContent.TextEncoding}");
        _output.WriteLine($"Text Length: {mobi.TextContent.TextLength:N0}");
        _output.WriteLine($"Record Count: {mobi.TextContent.RecordCount}");

        Assert.True(mobi.TextContent.TextLength > 0);
        Assert.True(mobi.TextContent.RecordCount > 0);
        Assert.NotNull(mobi.TextContent.CompressionName);
        Assert.NotNull(mobi.TextContent.TextEncoding);
    }

    #endregion

    #region Theory Tests

    [SkippableTheory]
    [InlineData("frankenstein_mobi.mobi", PalmDOCHead.CompressionPalmDoc)]
    [InlineData("romeo_juliet_mobi.mobi", PalmDOCHead.CompressionPalmDoc)]
    [InlineData("wuthering_heights_mobi.mobi", PalmDOCHead.CompressionPalmDoc)]
    public async Task Compression_OldMobiFiles_UsePalmDoc(string filename, ushort expectedCompression)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(expectedCompression, mobi.PalmDocHeader.Compression);
    }

    [SkippableTheory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("alice_wonderland_kf8.mobi")]
    [InlineData("sherlock_holmes_kf8.mobi")]
    public async Task Decompression_AllKf8Files_ExtractsText(string filename)
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
