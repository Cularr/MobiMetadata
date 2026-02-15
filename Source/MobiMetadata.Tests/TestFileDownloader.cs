using System.Reflection;

namespace MobiMetadata.Tests;

/// <summary>
/// Downloads test files from Project Gutenberg and other sources.
/// </summary>
public class TestFileDownloader
{
    private readonly HttpClient _httpClient;
    private readonly string _testFilesDir;

    // Free MOBI/KF8 files from Project Gutenberg (public domain)
    // Format: filename -> URL
    public static readonly Dictionary<string, string> TestFiles = new()
    {
        // KF8 format with images (modern AZW3 - version 8)
        ["frankenstein_kf8.mobi"] = "https://www.gutenberg.org/ebooks/84.kf8.images",
        ["alice_wonderland_kf8.mobi"] = "https://www.gutenberg.org/ebooks/11.kf8.images",
        ["pride_prejudice_kf8.mobi"] = "https://www.gutenberg.org/ebooks/1342.kf8.images",
        ["moby_dick_kf8.mobi"] = "https://www.gutenberg.org/ebooks/2701.kf8.images",
        ["sherlock_holmes_kf8.mobi"] = "https://www.gutenberg.org/ebooks/1661.kf8.images",
        ["dracula_kf8.mobi"] = "https://www.gutenberg.org/ebooks/345.kf8.images",
        ["great_gatsby_kf8.mobi"] = "https://www.gutenberg.org/ebooks/64317.kf8.images",
        ["tale_of_two_cities_kf8.mobi"] = "https://www.gutenberg.org/ebooks/98.kf8.images",
        ["jane_eyre_kf8.mobi"] = "https://www.gutenberg.org/ebooks/1260.kf8.images",
        ["war_and_peace_kf8.mobi"] = "https://www.gutenberg.org/ebooks/2600.kf8.images",
        ["metamorphosis_kf8.mobi"] = "https://www.gutenberg.org/ebooks/5200.kf8.images",
        ["time_machine_kf8.mobi"] = "https://www.gutenberg.org/ebooks/35.kf8.images",

        // Older MOBI format - Kindle no images (version 6, PalmDOC compression)
        ["frankenstein_mobi.mobi"] = "https://www.gutenberg.org/ebooks/84.kindle.noimages",
        ["romeo_juliet_mobi.mobi"] = "https://www.gutenberg.org/ebooks/1513.kindle.noimages",
        ["wuthering_heights_mobi.mobi"] = "https://www.gutenberg.org/ebooks/768.kindle.noimages",
        ["sherlock_holmes_mobi.mobi"] = "https://www.gutenberg.org/ebooks/1661.kindle.noimages",
        ["dracula_mobi.mobi"] = "https://www.gutenberg.org/ebooks/345.kindle.noimages",
        ["pride_prejudice_mobi.mobi"] = "https://www.gutenberg.org/ebooks/1342.kindle.noimages",

        // Older MOBI format - Kindle with images (version 6, typically HUFF/CDIC compression)
        ["frankenstein_kindle_images.mobi"] = "https://www.gutenberg.org/ebooks/84.kindle.images",
        ["alice_kindle_images.mobi"] = "https://www.gutenberg.org/ebooks/11.kindle.images",
        ["sherlock_kindle_images.mobi"] = "https://www.gutenberg.org/ebooks/1661.kindle.images",
    };

    public TestFileDownloader(string? testFilesDir = null)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MobiMetadata.Tests/1.0");
        _httpClient.Timeout = TimeSpan.FromMinutes(5);

        _testFilesDir = testFilesDir ?? Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".",
            "TestFiles");

        Directory.CreateDirectory(_testFilesDir);
    }

    public string TestFilesDir => _testFilesDir;

    public async Task DownloadAllAsync(bool forceRedownload = false, int? maxFiles = null)
    {
        Console.WriteLine($"Downloading test files to: {_testFilesDir}");

        var downloaded = 0;
        foreach (var (filename, url) in TestFiles)
        {
            if (maxFiles.HasValue && downloaded >= maxFiles.Value)
            {
                Console.WriteLine($"  [MAX] Reached max files limit ({maxFiles.Value})");
                break;
            }

            var filePath = Path.Combine(_testFilesDir, filename);

            if (File.Exists(filePath) && !forceRedownload)
            {
                var existingSize = new FileInfo(filePath).Length;
                Console.WriteLine($"  [SKIP] {filename} already exists ({existingSize:N0} bytes)");
                continue;
            }

            Console.WriteLine($"  [DOWN] {filename} from {url}");
            try
            {
                var data = await _httpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(filePath, data);
                Console.WriteLine($"  [OK] {filename} ({data.Length:N0} bytes)");
                downloaded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ERR] Failed to download {filename}: {ex.Message}");
            }
        }
    }

    public string GetFilePath(string filename)
    {
        return Path.Combine(_testFilesDir, filename);
    }

    public bool FileExists(string filename)
    {
        return File.Exists(GetFilePath(filename));
    }

    public IEnumerable<string> GetDownloadedFiles()
    {
        return TestFiles.Keys.Where(FileExists);
    }

    /// <summary>
    /// Gets all KF8 (version 8) files - modern AZW3 format
    /// </summary>
    public IEnumerable<string> GetAllKf8Files()
    {
        return TestFiles.Keys.Where(f => f.Contains("_kf8") && FileExists(f));
    }

    /// <summary>
    /// Gets all older MOBI (version 6) files - no images
    /// </summary>
    public IEnumerable<string> GetAllOldMobiFiles()
    {
        return TestFiles.Keys.Where(f => f.Contains("_mobi") && !f.Contains("kindle") && FileExists(f));
    }

    /// <summary>
    /// Gets all Kindle with images (version 6 with HUFF/CDIC) files
    /// </summary>
    public IEnumerable<string> GetAllKindleImagesFiles()
    {
        return TestFiles.Keys.Where(f => f.Contains("_kindle_images") && FileExists(f));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
