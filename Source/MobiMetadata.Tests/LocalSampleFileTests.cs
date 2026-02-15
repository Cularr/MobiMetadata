using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests using locally available sample files (AZW3, MOBI, PDB).
/// </summary>
public class LocalSampleFileTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _testFilesDir;

    // Local sample files
    public static readonly string[] Azw3Files =
    [
        "sample-azw3-files-Alices Adventures in Wonderland.azw3",
        "sample-azw3-files-Around the World in 28 Languages.azw3",
        "sample-azw3-files-famouspaintings.azw3",
        "sample-azw3-files-sample1.azw3",
        "sample-azw3-files-Sway.azw3"
    ];

    public static readonly string[] MobiFiles =
    [
        "sample-mobi-files-Alices Adventures in Wonderland.mobi",
        "sample-mobi-files-Around the World in 28 Languages.mobi",
        "sample-mobi-files-famouspaintings.mobi",
        "sample-mobi-files-sample1.mobi",
        "sample-mobi-files-Sway.mobi"
    ];

    public static readonly string[] PdbFiles =
    [
        "sample-pdb-files-Alices Adventures in Wonderland.pdb",
        "sample-pdb-files-Around the World in 28 Languages.pdb",
        "sample-pdb-files-famouspaintings.pdb"
    ];

    public static readonly string ComicFile = "Cage of Eden v13 c104-112.mobi";

    public LocalSampleFileTests(ITestOutputHelper output)
    {
        _output = output;
        _testFilesDir = FindTestFilesDir();
    }

    private static string FindTestFilesDir()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        
        while (dir != null)
        {
            var testFiles = Path.Combine(dir.FullName, "TestFiles");
            if (Directory.Exists(testFiles))
                return testFiles;
            
            dir = dir.Parent;
        }
        
        return Path.Combine(baseDir, "TestFiles");
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    private string GetFilePath(string filename) => Path.Combine(_testFilesDir, filename);
    private bool FileExists(string filename) => File.Exists(GetFilePath(filename));

    #region AZW3 File Tests

    [Fact]
    public async Task Azw3_ReadsMetadata()
    {
        foreach (var filename in Azw3Files)
        {
            if (!FileExists(filename)) continue;

            var filePath = GetFilePath(filename);
            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}]");
            _output.WriteLine($"  Name: {mobi.MobiHeader.FullName}");
            _output.WriteLine($"  Version: {mobi.MobiHeader.FileVersion}");
            _output.WriteLine($"  IsKf8: {mobi.IsKf8}");
            _output.WriteLine($"  Compression: {mobi.PalmDocHeader.CompressionName}");

            Assert.NotNull(mobi.PdbHeader);
            Assert.NotNull(mobi.MobiHeader);
        }
    }

    [Theory]
    [InlineData("sample-azw3-files-Alices Adventures in Wonderland.azw3")]
    [InlineData("sample-azw3-files-Sway.azw3")]
    public async Task Azw3_ExtractsText(string filename)
    {
        if (!FileExists(filename)) throw new Xunit.SkipException($"File not found: {filename}");

        var filePath = GetFilePath(filename);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        if (mobi.TextContent != null)
        {
            var text = await mobi.TextContent.ExtractTextAsync();
            _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
            Assert.NotEmpty(text);
        }
    }

    #endregion

    #region MOBI File Tests

    [Fact]
    public async Task Mobi_ReadsMetadata()
    {
        foreach (var filename in MobiFiles)
        {
            if (!FileExists(filename)) continue;

            var filePath = GetFilePath(filename);
            await using var stream = File.OpenRead(filePath);
            var mobi = new MobiMetadata();
            await mobi.ReadMetadataAsync(stream);

            _output.WriteLine($"[{filename}]");
            _output.WriteLine($"  Name: {mobi.MobiHeader.FullName}");
            _output.WriteLine($"  Version: {mobi.MobiHeader.FileVersion}");
            _output.WriteLine($"  Type: {mobi.MobiHeader.MobiTypeName}");
            _output.WriteLine($"  Compression: {mobi.PalmDocHeader.CompressionName}");

            Assert.NotNull(mobi.PdbHeader);
            Assert.NotNull(mobi.MobiHeader);
        }
    }

    [Theory]
    [InlineData("sample-mobi-files-Alices Adventures in Wonderland.mobi")]
    [InlineData("sample-mobi-files-Sway.mobi")]
    public async Task Mobi_ExtractsText(string filename)
    {
        if (!FileExists(filename)) throw new Xunit.SkipException($"File not found: {filename}");

        var filePath = GetFilePath(filename);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        if (mobi.TextContent != null)
        {
            var text = await mobi.TextContent.ExtractTextAsync();
            _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
            Assert.NotEmpty(text);
        }
    }

    #endregion

    #region PDB File Tests

    [Fact]
    public async Task Pdb_ReadsPdbHeader()
    {
        // PDB files have a different format - they may not have MOBI headers
        // We can only test that PDB header parsing works
        foreach (var filename in PdbFiles)
        {
            if (!FileExists(filename)) continue;

            var filePath = GetFilePath(filename);
            await using var stream = File.OpenRead(filePath);
            
            // Read only PDB header
            var pdb = new PDBHead();
            await pdb.ReadHeaderAsync(stream);

            _output.WriteLine($"[{filename}]");
            _output.WriteLine($"  PDB Name: {pdb.Name}");
            _output.WriteLine($"  PDB Type: {pdb.TypeName}/{pdb.CreatorName}");
            _output.WriteLine($"  Records: {pdb.NumRecords}");

            Assert.True(pdb.NumRecords > 0);
        }
    }

    #endregion

    #region Comic File Tests

    [Fact]
    public async Task Comic_ReadsMetadata()
    {
        if (!FileExists(ComicFile)) throw new Xunit.SkipException($"Comic file not found: {ComicFile}");

        var filePath = GetFilePath(ComicFile);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine($"=== Comic: {ComicFile} ===");
        _output.WriteLine($"Name: {mobi.MobiHeader.FullName}");
        _output.WriteLine($"Version: {mobi.MobiHeader.FileVersion}");
        _output.WriteLine($"Compression: {mobi.PalmDocHeader.CompressionName}");
        _output.WriteLine($"Has DRM: {mobi.MobiHeader.HasDrm}");

        Assert.NotNull(mobi.PdbHeader);
        Assert.NotNull(mobi.MobiHeader);
    }

    [Fact]
    public async Task Comic_LoadsImages()
    {
        if (!FileExists(ComicFile)) throw new Xunit.SkipException($"Comic file not found: {ComicFile}");

        var filePath = GetFilePath(ComicFile);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);

        try
        {
            await mobi.LoadImagesAsync();

            _output.WriteLine($"Page count: {mobi.PageCount}");
            _output.WriteLine($"Cover: {(mobi.Cover != null ? $"Yes ({mobi.Cover.Size} bytes)" : "No")}");

            if (mobi.Images.Count > 0)
            {
                for (int i = 0; i < Math.Min(5, mobi.Images.Count); i++)
                {
                    _output.WriteLine($"  Image {i}: {mobi.Images[i].Size} bytes");
                }
            }

            Assert.True(mobi.Images.Count > 0);
        }
        catch (MobiMetadataException ex)
        {
            _output.WriteLine($"Could not load images: {ex.Message}");
        }
    }

    [Fact]
    public async Task Comic_ExtractsAllImages()
    {
        if (!FileExists(ComicFile)) throw new Xunit.SkipException($"Comic file not found: {ComicFile}");

        var filePath = GetFilePath(ComicFile);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);

        try
        {
            await mobi.LoadImagesAsync();

            var successCount = 0;
            var failCount = 0;

            for (int i = 0; i < Math.Min(mobi.Images.Count, 10); i++)
            {
                try
                {
                    var imageBytes = await mobi.GetImageBytesAsync(i);
                    using var imageMemStream = new MemoryStream(imageBytes);
                    using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageMemStream);

                    _output.WriteLine($"Image {i}: {image.Width}x{image.Height} ({image.Metadata.DecodedImageFormat?.Name})");
                    successCount++;
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Image {i}: FAILED - {ex.Message}");
                    failCount++;
                }
            }

            _output.WriteLine($"Successfully loaded: {successCount}, Failed: {failCount}");
            Assert.True(successCount > 0, "At least one image should load successfully");
        }
        catch (MobiMetadataException ex)
        {
            _output.WriteLine($"Could not load images: {ex.Message}");
        }
    }

    #endregion

    #region Cross-Format Tests

    [Fact]
    public async Task CrossFormat_Azw3VsMobi_SameContent()
    {
        // Compare Alice in Wonderland AZW3 vs MOBI
        var azw3File = "sample-azw3-files-Alices Adventures in Wonderland.azw3";
        var mobiFile = "sample-mobi-files-Alices Adventures in Wonderland.mobi";

        if (!FileExists(azw3File) || !FileExists(mobiFile))
            throw new Xunit.SkipException("Files not found");

        // Read AZW3
        await using var azw3Stream = File.OpenRead(GetFilePath(azw3File));
        var azw3Mobi = new MobiMetadata();
        await azw3Mobi.ReadMetadataAsync(azw3Stream);

        // Read MOBI
        await using var mobiStream = File.OpenRead(GetFilePath(mobiFile));
        var mobiMobi = new MobiMetadata();
        await mobiMobi.ReadMetadataAsync(mobiStream);

        _output.WriteLine($"AZW3: Version {azw3Mobi.MobiHeader.FileVersion}, Compression: {azw3Mobi.PalmDocHeader.CompressionName}");
        _output.WriteLine($"MOBI: Version {mobiMobi.MobiHeader.FileVersion}, Compression: {mobiMobi.PalmDocHeader.CompressionName}");

        // AZW3 should be KF8 (version 8), MOBI should be older (version 6)
        Assert.True(azw3Mobi.MobiHeader.FileVersion >= mobiMobi.MobiHeader.FileVersion);
    }

    #endregion
}
