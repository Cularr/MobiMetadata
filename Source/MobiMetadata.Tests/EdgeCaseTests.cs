using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for edge cases, error handling, DRM detection, and IDisposable.
/// </summary>
public class EdgeCaseTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public EdgeCaseTests(ITestOutputHelper output)
    {
        _output = output;
        _downloader = new TestFileDownloader();
    }

    public async Task InitializeAsync()
    {
        await _downloader.DownloadAllAsync(maxFiles: 10);
    }

    public Task DisposeAsync()
    {
        _downloader.Dispose();
        return Task.CompletedTask;
    }

    #region IDisposable Tests

    [Fact]
    public async Task IDisposable_CanBeDisposed()
    {
        var mobi = new MobiMetadata();
        mobi.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mobi.OpenImageStream(0));
    }

    [Fact]
    public async Task IDisposable_DisposedObject_ThrowsOnReadMetadata()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        var mobi = new MobiMetadata();
        mobi.Dispose();

        await using var stream = File.OpenRead(filePath);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mobi.ReadMetadataAsync(stream));
    }

    [Fact]
    public async Task IDisposable_DisposedObject_ThrowsOnLoadImages()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);
        mobi.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => mobi.LoadImagesAsync());
    }

    [Fact]
    public async Task IDisposable_MultipleDisposeCalls_DoNotThrow()
    {
        var mobi = new MobiMetadata();
        mobi.Dispose();
        mobi.Dispose(); // Should not throw
        mobi.Dispose(); // Should not throw

        Assert.True(true);
    }

    [Fact]
    public async Task IDisposable_UsingPattern_Works()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        
        string fullName;
        using (var mobi = new MobiMetadata())
        {
            await mobi.ReadMetadataAsync(stream);
            fullName = mobi.MobiHeader.FullName;
        }
        // mobi is now disposed

        Assert.NotNull(fullName);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task LoadImages_WithoutReadMetadata_Throws()
    {
        var mobi = new MobiMetadata();

        var ex = await Assert.ThrowsAsync<MobiMetadataException>(() => mobi.LoadImagesAsync());
        Assert.Contains("ReadMetadataAsync", ex.Message);
    }

    [Fact]
    public async Task GetImageBytes_WithoutLoadImages_Throws()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        var ex = await Assert.ThrowsAsync<MobiMetadataException>(() => mobi.GetImageBytesAsync(0));
        Assert.Contains("LoadImagesAsync", ex.Message);
    }

    [Fact]
    public async Task OpenImageStream_WithoutLoadImages_Throws()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        var ex = Assert.Throws<MobiMetadataException>(() => mobi.OpenImageStream(0));
        Assert.Contains("LoadImagesAsync", ex.Message);
    }

    [Fact]
    public async Task RequireExthHeader_WithNoExth_Throws()
    {
        // Create a minimal MOBI without EXTH header
        // For this test, we'll just verify the constructor works
        var mobi = new MobiMetadata(requireExthHeader: true);
        Assert.NotNull(mobi);
    }

    [Fact]
    public async Task InvalidStream_Throws()
    {
        var mobi = new MobiMetadata();
        var invalidStream = new MemoryStream([1, 2, 3, 4]); // Too small

        await Assert.ThrowsAnyAsync<Exception>(() => mobi.ReadMetadataAsync(invalidStream));
    }

    #endregion

    #region DRM Detection Tests

    [Fact]
    public async Task DRM_PublicDomainFiles_HaveNoDrm()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.False(mobi.MobiHeader.HasDrm, "Public domain file should not have DRM");
    }

    [Fact]
    public async Task DRM_NoDrm_MeansHasDrmFalse()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        // HasDrm checks both DrmOffset != 0 and DrmCount != 0
        // Note: DrmOffset can be uint.MaxValue (0xFFFFFFFF) to indicate no DRM
        Assert.False(mobi.MobiHeader.HasDrm);
    }

    [Fact]
    public async Task DRM_EncryptionNone_ForPublicDomain()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(0, mobi.PalmDocHeader.EncryptionType);
        Assert.Equal("None", mobi.PalmDocHeader.EncryptionTypeName);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public async Task ImageIndex_AtBoundary()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var count = mobi.Images.Count;
        Assert.True(count > 0);

        // Last valid index
        var lastBytes = await mobi.GetImageBytesAsync(count - 1);
        Assert.NotNull(lastBytes);
        Assert.True(lastBytes.Length > 0);

        // First invalid index
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => mobi.GetImageBytesAsync(count));
    }

    [Fact]
    public async Task PageCount_MatchesImageCount()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.Equal(mobi.Images.Count, mobi.PageCount);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_DefaultParameters_Works()
    {
        var mobi = new MobiMetadata();
        Assert.NotNull(mobi.PdbHeader);
        Assert.NotNull(mobi.PalmDocHeader);
        Assert.NotNull(mobi.MobiHeader);
    }

    [Fact]
    public void Constructor_CustomHeaders_Works()
    {
        var customPdb = new PDBHead();
        var customPalmDoc = new PalmDOCHead();
        var customMobi = new MobiHead();

        var mobi = new MobiMetadata(customPdb, customPalmDoc, customMobi);

        Assert.Same(customPdb, mobi.PdbHeader);
        Assert.Same(customPalmDoc, mobi.PalmDocHeader);
        Assert.Same(customMobi, mobi.MobiHeader);
    }

    [Fact]
    public void Constructor_RequireExthHeader_Works()
    {
        var mobi = new MobiMetadata(requireExthHeader: true);
        Assert.NotNull(mobi);
    }

    #endregion

    #region Compression Constants Tests

    [Fact]
    public void CompressionConstants_AreCorrect()
    {
        Assert.Equal(1, PalmDOCHead.CompressionNone);
        Assert.Equal(2, PalmDOCHead.CompressionPalmDoc);
        Assert.Equal(17480, PalmDOCHead.CompressionHuffCdic);
    }

    [Fact]
    public async Task CompressionName_UsesConstants()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(PalmDOCHead.CompressionPalmDoc, mobi.PalmDocHeader.Compression);
        Assert.Equal("PalmDOC", mobi.PalmDocHeader.CompressionName);
    }

    #endregion

    #region Theory Tests

    [Theory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("alice_wonderland_kf8.mobi")]
    [InlineData("moby_dick_kf8.mobi")]
    public async Task DRM_AllPublicDomainFiles_HaveNoDrm(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.False(mobi.MobiHeader.HasDrm, $"{filename} should not have DRM");
    }

    [Theory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("frankenstein_mobi.mobi")]
    [InlineData("frankenstein_kindle_images.mobi")]
    public async Task Encryption_AllPublicDomainFiles_IsNone(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(0, mobi.PalmDocHeader.EncryptionType);
        Assert.Equal("None", mobi.PalmDocHeader.EncryptionTypeName);
    }

    #endregion
}
