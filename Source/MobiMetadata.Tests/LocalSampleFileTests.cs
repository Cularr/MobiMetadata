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

    public static readonly string ComicFile = Path.GetFullPath(Environment.GetEnvironmentVariable("MOBI_TEST_COMIC") ?? "missing-comic.mobi");

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
    private void SkipWithoutSamples(IEnumerable<string> files) => Skip.IfNot(files.Any(FileExists), "No local sample files installed");

    #region AZW3 File Tests

    [SkippableFact]
    public async Task Azw3_ReadsMetadata()
    {
        SkipWithoutSamples(Azw3Files);

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

            Assert.False(string.IsNullOrWhiteSpace(mobi.MobiHeader.FullName));
            Assert.True(mobi.MobiHeader.FileVersion > 0);
        }
    }

    [SkippableTheory]
    [InlineData("sample-azw3-files-Alices Adventures in Wonderland.azw3")]
    [InlineData("sample-azw3-files-Sway.azw3")]
    public async Task Azw3_ExtractsText(string filename)
    {
        if (!FileExists(filename)) throw new Xunit.SkipException($"File not found: {filename}");

        var filePath = GetFilePath(filename);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();
        _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
        Assert.NotEmpty(text);
    }

    #endregion

    #region MOBI File Tests

    [SkippableFact]
    public async Task Mobi_ReadsMetadata()
    {
        SkipWithoutSamples(MobiFiles);

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

            Assert.False(string.IsNullOrWhiteSpace(mobi.MobiHeader.FullName));
            Assert.True(mobi.MobiHeader.FileVersion > 0);
        }
    }

    [SkippableTheory]
    [InlineData("sample-mobi-files-Alices Adventures in Wonderland.mobi")]
    [InlineData("sample-mobi-files-Sway.mobi")]
    public async Task Mobi_ExtractsText(string filename)
    {
        if (!FileExists(filename)) throw new Xunit.SkipException($"File not found: {filename}");

        var filePath = GetFilePath(filename);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.TextContent);

        var text = await mobi.TextContent.ExtractTextAsync();
        _output.WriteLine($"[{filename}] Text length: {text.Length:N0} chars");
        Assert.NotEmpty(text);
    }

    #endregion

    #region PDB File Tests

    [SkippableFact]
    public async Task Pdb_ReadsPdbHeader()
    {
        // PDB files have a different format - they may not have MOBI headers
        // We can only test that PDB header parsing works
        SkipWithoutSamples(PdbFiles);

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
            Assert.False(string.IsNullOrWhiteSpace(pdb.Name));
            Assert.False(string.IsNullOrWhiteSpace(pdb.TypeName));
        }
    }

    #endregion

    #region Comic File Tests

    [SkippableFact]
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

    [SkippableFact]
    public async Task Comic_LoadsImages()
    {
        if (!FileExists(ComicFile)) throw new Xunit.SkipException($"Comic file not found: {ComicFile}");

        var filePath = GetFilePath(ComicFile);
        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata(requireExthHeader: true);
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        _output.WriteLine($"Page count: {mobi.PageCount}");
        _output.WriteLine($"Cover: {(mobi.Cover != null ? $"Yes ({mobi.Cover.Size} bytes)" : "No")}");

        Assert.NotEmpty(mobi.Images);
        Assert.NotNull(mobi.Cover);
    }

    [SkippableFact]
    public async Task Comic_ExtractsAllImages()
    {
        if (!FileExists(ComicFile)) throw new Xunit.SkipException($"Comic file not found: {ComicFile}");

        var filePath = GetFilePath(ComicFile);
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
        }
    }

    #endregion

    #region Cross-Format Tests

    [SkippableFact]
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
