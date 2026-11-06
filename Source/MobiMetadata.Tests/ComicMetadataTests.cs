using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace MobiMetadata.Tests;

public class ComicMetadataTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _comicPath;

    public ComicMetadataTests(ITestOutputHelper output)
    {
        _output = output;
        _comicPath = Environment.GetEnvironmentVariable("MOBI_TEST_COMIC") ?? string.Empty;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Comic_ExploreAllMetadata()
    {
        if (!File.Exists(_comicPath))
        {
            throw new Xunit.SkipException($"Comic file not found: {_comicPath}");
        }

        await using var stream = File.OpenRead(_comicPath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine("=== PDB HEADER ===");
        _output.WriteLine($"Name: {mobi.PdbHeader.Name}");
        _output.WriteLine($"Type: {mobi.PdbHeader.TypeName}");
        _output.WriteLine($"Creator: {mobi.PdbHeader.CreatorName}");
        _output.WriteLine($"Num Records: {mobi.PdbHeader.NumRecords}");
        _output.WriteLine($"Creation Date: {mobi.PdbHeader.CreationDate}");
        _output.WriteLine($"Modification Date: {mobi.PdbHeader.ModificationDate}");

        _output.WriteLine("\n=== PALMDOC HEADER ===");
        _output.WriteLine($"Compression: {mobi.PalmDocHeader.Compression} ({mobi.PalmDocHeader.CompressionName})");
        _output.WriteLine($"Text Length: {mobi.PalmDocHeader.TextLength:N0}");
        _output.WriteLine($"Record Count: {mobi.PalmDocHeader.RecordCount}");
        _output.WriteLine($"Record Size: {mobi.PalmDocHeader.RecordSize}");
        _output.WriteLine($"Encryption: {mobi.PalmDocHeader.EncryptionType} ({mobi.PalmDocHeader.EncryptionTypeName})");

        _output.WriteLine("\n=== MOBI HEADER ===");
        _output.WriteLine($"Full Name: {mobi.MobiHeader.FullName}");
        _output.WriteLine($"File Version: {mobi.MobiHeader.FileVersion}");
        _output.WriteLine($"MOBI Type: {mobi.MobiHeader.MobiType} ({mobi.MobiHeader.MobiTypeName})");
        _output.WriteLine($"Text Encoding: {mobi.MobiHeader.TextEncoding} ({mobi.MobiHeader.TextEncodingName})");
        _output.WriteLine($"Unique ID: {mobi.MobiHeader.UniqueId}");
        _output.WriteLine($"Min Version: {mobi.MobiHeader.MinVersion}");
        _output.WriteLine($"Has DRM: {mobi.MobiHeader.HasDrm}");
        _output.WriteLine($"Has EXTH: {mobi.MobiHeader.HasExthHeader}");
        _output.WriteLine($"Has HUFF/CDIC: {mobi.MobiHeader.HasHuffCdic}");
        _output.WriteLine($"First Image Index: {mobi.MobiHeader.FirstImageIndex}");
        _output.WriteLine($"First Content Record: {mobi.MobiHeader.FirstContentRecord}");
        _output.WriteLine($"Last Content Record: {mobi.MobiHeader.LastContentRecord}");
        _output.WriteLine($"Is KF8: {mobi.IsKf8}");
        _output.WriteLine($"Is Dual MOBI: {mobi.IsDualMobi}");

        if (mobi.MobiHeader.ExthHeader != null)
        {
            var exth = mobi.MobiHeader.ExthHeader;
            _output.WriteLine("\n=== EXTH HEADER ===");
            _output.WriteLine($"Author: {exth.Author}");
            _output.WriteLine($"Publisher: {exth.Publisher}");
            _output.WriteLine($"Language: {exth.Language}");
            _output.WriteLine($"Subject: {exth.Subject}");
            _output.WriteLine($"Description: {exth.Description}");
            _output.WriteLine($"Published Date: {exth.PublishedDate}");
            _output.WriteLine($"Rights: {exth.Rights}");
            _output.WriteLine($"ASIN: {exth.ASIN}");
            _output.WriteLine($"ASIN2: {exth.ASIN2}");
            _output.WriteLine($"ISBN: {exth.IBSN}");
            _output.WriteLine($"Source: {exth.Source}");
            _output.WriteLine($"Type: {exth.Type}");
            _output.WriteLine($"CDE Type: {exth.CDEType}");
            _output.WriteLine($"Book Type: {exth.BookType}");
            _output.WriteLine($"Cover Offset: {exth.CoverOffset}");
            _output.WriteLine($"Thumb Offset: {exth.ThumbOffset}");
            _output.WriteLine($"Has Fake Cover: {exth.HasFakeCover}");
            _output.WriteLine($"Sample: {exth.Sample}");
            _output.WriteLine($"Creator Software: {exth.CreatorSoftware}");
            _output.WriteLine($"Creator Build String: {exth.CreatorBuildString}");
            _output.WriteLine($"Contributor: {exth.Contributor}");
            _output.WriteLine($"KF8 Boundary Offset: {exth.Kf8BoundaryOffset}");
            _output.WriteLine($"Fixed Layout: {exth.FixedLayout}");
            _output.WriteLine($"Orientation Lock: {exth.OrientationLock}");
            _output.WriteLine($"Page Progression Direction: {exth.PageProgressionDirection}");
            _output.WriteLine($"Container ID: {exth.ContainerId}");
            _output.WriteLine($"Container Resolution: {exth.ContainerResolution}");
            _output.WriteLine($"Container MIME Type: {exth.ContainerMimeType}");
        }

        _output.WriteLine("\n=== IMAGE LOADING ===");
        try
        {
            await mobi.LoadImagesAsync();
            _output.WriteLine($"Images loaded successfully");
            _output.WriteLine($"Page Count: {mobi.PageCount}");
            _output.WriteLine($"Cover: {(mobi.Cover != null ? $"Yes ({mobi.Cover.Size} bytes)" : "No")}");

            if (mobi.Images.Count > 0)
            {
                _output.WriteLine($"\nFirst 5 images:");
                for (int i = 0; i < Math.Min(5, mobi.Images.Count); i++)
                {
                    _output.WriteLine($"  [{i}] Size: {mobi.Images[i].Size} bytes");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Image loading failed: {ex.Message}");
        }

        _output.WriteLine("\n=== VALIDATION ===");
        Assert.NotNull(mobi.PdbHeader);
        Assert.NotNull(mobi.PalmDocHeader);
        Assert.NotNull(mobi.MobiHeader);
        Assert.True(mobi.PdbHeader.NumRecords > 0, "Should have records");
    }

    [Fact]
    public async Task Comic_ExtractCover()
    {
        if (!File.Exists(_comicPath))
        {
            throw new Xunit.SkipException($"Comic file not found: {_comicPath}");
        }

        await using var stream = File.OpenRead(_comicPath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        var coverBytes = await mobi.GetCoverBytesAsync();

        if (coverBytes != null)
        {
            _output.WriteLine($"Cover found: {coverBytes.Length} bytes");

            using var coverStream = new MemoryStream(coverBytes);
            using var image = await SixLabors.ImageSharp.Image.LoadAsync(coverStream);
            _output.WriteLine($"Cover dimensions: {image.Width}x{image.Height}");
            _output.WriteLine($"Cover format: {image.Metadata.DecodedImageFormat?.Name ?? "unknown"}");

            Assert.True(image.Width > 0);
            Assert.True(image.Height > 0);
        }
        else
        {
            _output.WriteLine("No cover found");
        }
    }

    [Fact]
    public async Task Comic_ExtractAllImages()
    {
        if (!File.Exists(_comicPath))
        {
            throw new Xunit.SkipException($"Comic file not found: {_comicPath}");
        }

        await using var stream = File.OpenRead(_comicPath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        _output.WriteLine($"Total pages: {mobi.PageCount}");

        var successCount = 0;
        var failCount = 0;
        var dimensions = new List<(int Width, int Height, string Format)>();

        for (int i = 0; i < mobi.Images.Count; i++)
        {
            try
            {
                var imageBytes = await mobi.GetImageBytesAsync(i);
                using var imageMemStream = new MemoryStream(imageBytes);
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageMemStream);

                dimensions.Add((image.Width, image.Height, image.Metadata.DecodedImageFormat?.Name ?? "unknown"));
                successCount++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Image {i}: FAILED - {ex.Message}");
                failCount++;
            }
        }

        _output.WriteLine($"\n=== IMAGE SUMMARY ===");
        _output.WriteLine($"Successfully loaded: {successCount}/{mobi.Images.Count}");
        _output.WriteLine($"Failed: {failCount}");

        if (dimensions.Count > 0)
        {
            var uniqueSizes = dimensions.Select(d => (d.Width, d.Height)).Distinct().ToList();
            _output.WriteLine($"\nUnique dimensions:");
            foreach (var size in uniqueSizes.OrderBy(s => s.Width))
            {
                var count = dimensions.Count(d => d.Width == size.Width && d.Height == size.Height);
                _output.WriteLine($"  {size.Width}x{size.Height}: {count} pages");
            }

            var formats = dimensions.Select(d => d.Format).Distinct().ToList();
            _output.WriteLine($"\nFormats: {string.Join(", ", formats)}");
        }

        Assert.True(successCount > 0, "At least some images should load successfully");
    }

    [Fact]
    public async Task Comic_GetPageByIndex()
    {
        if (!File.Exists(_comicPath))
        {
            throw new Xunit.SkipException($"Comic file not found: {_comicPath}");
        }

        await using var stream = File.OpenRead(_comicPath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        Assert.True(mobi.PageCount > 0, "Should have pages");

        // Test middle page
        var midIndex = mobi.PageCount / 2;
        var midBytes = await mobi.GetImageBytesAsync(midIndex);

        _output.WriteLine($"Page {midIndex}: {midBytes.Length} bytes");

        using var midStream = new MemoryStream(midBytes);
        using var midImage = await SixLabors.ImageSharp.Image.LoadAsync(midStream);
        _output.WriteLine($"Dimensions: {midImage.Width}x{midImage.Height}");

        // Test last page
        var lastIndex = mobi.PageCount - 1;
        var lastBytes = await mobi.GetImageBytesAsync(lastIndex);

        _output.WriteLine($"Page {lastIndex}: {lastBytes.Length} bytes");

        Assert.True(midBytes.Length > 0);
        Assert.True(lastBytes.Length > 0);
    }

    [Fact]
    public async Task Comic_StreamAccess()
    {
        if (!File.Exists(_comicPath))
        {
            throw new Xunit.SkipException($"Comic file not found: {_comicPath}");
        }

        await using var stream = File.OpenRead(_comicPath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);
        await mobi.LoadImagesAsync();

        // Test stream access
        using var imageStream = mobi.OpenImageStream(0);
        Assert.True(imageStream.CanRead);

        using var image = await SixLabors.ImageSharp.Image.LoadAsync(imageStream);
        _output.WriteLine($"First page via stream: {image.Width}x{image.Height}");

        Assert.True(image.Width > 0);
        Assert.True(image.Height > 0);
    }
}
