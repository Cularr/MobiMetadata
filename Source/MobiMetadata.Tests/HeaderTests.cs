using Xunit;
using Xunit.Abstractions;
// using Xunit.Sdk;

namespace MobiMetadata.Tests;

/// <summary>
/// Tests for PDB, PalmDOC, MOBI, and EXTH header parsing.
/// </summary>
public class HeaderTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly TestFileDownloader _downloader;

    public HeaderTests(ITestOutputHelper output)
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

    #region PDB Header Tests

    [SkippableFact]
    public async Task PDBHeader_ReadsBasicProperties()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.PdbHeader);
        Assert.True(mobi.PdbHeader.TypeName == "MOBI" || mobi.PdbHeader.TypeName == "BOOK",
            $"Expected MOBI or BOOK, got {mobi.PdbHeader.TypeName}");
        Assert.True(mobi.PdbHeader.NumRecords > 0);
        Assert.NotNull(mobi.PdbHeader.CreatorName);
        Assert.NotEmpty(mobi.PdbHeader.Name);

        _output.WriteLine($"Name: {mobi.PdbHeader.Name}");
        _output.WriteLine($"Type: {mobi.PdbHeader.TypeName}/{mobi.PdbHeader.CreatorName}");
        _output.WriteLine($"Records: {mobi.PdbHeader.NumRecords}");
    }

    [SkippableFact]
    public async Task PDBHeader_HasValidRecordInfo()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.True(mobi.PdbHeader.Records.Length > 0);
        
        for (int i = 0; i < Math.Min(5, mobi.PdbHeader.Records.Length); i++)
        {
            var record = mobi.PdbHeader.Records[i];
            Assert.True(record.DataOffset > 0, $"Record {i} should have valid offset");
            _output.WriteLine($"Record {i}: Offset={record.DataOffset}");
        }
    }

    [SkippableTheory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("alice_wonderland_kf8.mobi")]
    [InlineData("sherlock_holmes_kf8.mobi")]
    public async Task PDBHeader_AllKf8Files_HaveValidStructure(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.True(mobi.PdbHeader.NumRecords > 0);
        Assert.Equal(mobi.PdbHeader.NumRecords, (ushort)mobi.PdbHeader.Records.Length);
    }

    #endregion

    #region PalmDOC Header Tests

    [SkippableFact]
    public async Task PalmDOCHeader_ReadsCompressionInfo()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.PalmDocHeader);
        Assert.True(mobi.PalmDocHeader.TextLength > 0);
        Assert.True(mobi.PalmDocHeader.RecordCount > 0);
        Assert.True(mobi.PalmDocHeader.RecordSize > 0);
        Assert.True(mobi.PalmDocHeader.RecordSize == 4096, "PalmDOC record size should be 4096");

        _output.WriteLine($"Compression: {mobi.PalmDocHeader.Compression} ({mobi.PalmDocHeader.CompressionName})");
        _output.WriteLine($"Text Length: {mobi.PalmDocHeader.TextLength:N0}");
        _output.WriteLine($"Record Count: {mobi.PalmDocHeader.RecordCount}");
        _output.WriteLine($"Encryption: {mobi.PalmDocHeader.EncryptionType} ({mobi.PalmDocHeader.EncryptionTypeName})");
    }

    [SkippableFact]
    public async Task PalmDOCHeader_EncryptionIsNone_ForPublicDomain()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(0, mobi.PalmDocHeader.EncryptionType);
        Assert.Equal("None", mobi.PalmDocHeader.EncryptionTypeName);
    }

    [SkippableTheory]
    [InlineData("frankenstein_mobi.mobi", PalmDOCHead.CompressionPalmDoc)]
    public async Task PalmDOCHeader_DetectsCorrectCompression(string filename, ushort expectedCompression)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(expectedCompression, mobi.PalmDocHeader.Compression);
        _output.WriteLine($"{filename}: Compression={mobi.PalmDocHeader.CompressionName}");
    }

    #endregion

    #region MOBI Header Tests

    [SkippableFact]
    public async Task MobiHeader_ReadsCoreProperties()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader);
        Assert.Equal("MOBI", mobi.MobiHeader.Identifier);
        Assert.True(mobi.MobiHeader.HeaderLength > 0);
        Assert.NotNull(mobi.MobiHeader.FullName);
        Assert.True(mobi.MobiHeader.FileVersion > 0);
        Assert.True(mobi.MobiHeader.UniqueId > 0);

        _output.WriteLine($"Full Name: {mobi.MobiHeader.FullName}");
        _output.WriteLine($"File Version: {mobi.MobiHeader.FileVersion}");
        _output.WriteLine($"MOBI Type: {mobi.MobiHeader.MobiTypeName}");
        _output.WriteLine($"Text Encoding: {mobi.MobiHeader.TextEncodingName}");
        _output.WriteLine($"Unique ID: {mobi.MobiHeader.UniqueId}");
    }

    [SkippableFact]
    public async Task MobiHeader_Kf8File_HasVersion8()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(8u, mobi.MobiHeader.FileVersion);
        Assert.True(mobi.IsKf8);
    }

    [SkippableFact]
    public async Task MobiHeader_OldMobiFile_HasVersion6()
    {
        var filePath = _downloader.GetFilePath("frankenstein_mobi.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.Equal(6u, mobi.MobiHeader.FileVersion);
        Assert.False(mobi.IsKf8);
    }

    [SkippableFact]
    public async Task MobiHeader_PublicDomain_HasNoDrm()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        // HasDrm = DrmOffset != 0 && DrmCount != 0
        // Public domain files should not have DRM (HasDrm should be false)
        Assert.False(mobi.MobiHeader.HasDrm, "Public domain file should not have DRM");

        _output.WriteLine($"DRM Offset: {mobi.MobiHeader.DrmOffset}");
        _output.WriteLine($"DRM Count: {mobi.MobiHeader.DrmCount}");
        _output.WriteLine($"DRM Size: {mobi.MobiHeader.DrmSize}");
        _output.WriteLine($"DRM Flags: {mobi.MobiHeader.DrmFlags}");
        _output.WriteLine($"Has DRM: {mobi.MobiHeader.HasDrm}");
    }

    [SkippableFact]
    public async Task MobiHeader_HuffCdicProperties()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine($"Huffman Record Offset: {mobi.MobiHeader.HuffmanRecordOffset}");
        _output.WriteLine($"Huffman Record Count: {mobi.MobiHeader.HuffmanRecordCount}");
        _output.WriteLine($"Has HUFF/CDIC: {mobi.MobiHeader.HasHuffCdic}");

        if (mobi.MobiHeader.HasHuffCdic)
        {
            Assert.True(mobi.MobiHeader.HuffmanRecordCount > 0);
        }
    }

    [SkippableFact]
    public async Task MobiHeader_ContentRecordIndices()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine($"First Content Record: {mobi.MobiHeader.FirstContentRecord}");
        _output.WriteLine($"Last Content Record: {mobi.MobiHeader.LastContentRecord}");
        _output.WriteLine($"First Image Index: {mobi.MobiHeader.FirstImageIndex}");
        _output.WriteLine($"First Non-Book Index: {mobi.MobiHeader.FirstNonBookIndex}");

        // FirstContentRecord can be 0 for some files
        Assert.True(mobi.MobiHeader.LastContentRecord >= mobi.MobiHeader.FirstContentRecord);
    }

    [SkippableFact]
    public async Task MobiHeader_IndexFields_Exist()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        _output.WriteLine($"Extra Index 0 (Skeleton): {mobi.MobiHeader.ExtraIndex0}");
        _output.WriteLine($"Extra Index 1 (Chunks): {mobi.MobiHeader.ExtraIndex1}");
        _output.WriteLine($"Extra Index 2 (FDST): {mobi.MobiHeader.ExtraIndex2}");
        _output.WriteLine($"Extra Index 3 (NCX): {mobi.MobiHeader.ExtraIndex3}");
    }

    #endregion

    #region EXTH Header Tests

    [SkippableFact]
    public async Task ExthHeader_ReadsBasicMetadata()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);
        Assert.NotNull(mobi.MobiHeader.ExthHeader.Author);
        Assert.NotNull(mobi.MobiHeader.ExthHeader.Language);

        _output.WriteLine($"Author: {mobi.MobiHeader.ExthHeader.Author}");
        _output.WriteLine($"Publisher: {mobi.MobiHeader.ExthHeader.Publisher}");
        _output.WriteLine($"Language: {mobi.MobiHeader.ExthHeader.Language}");
        _output.WriteLine($"Description: {Truncate(mobi.MobiHeader.ExthHeader.Description, 100)}");
    }

    [SkippableFact]
    public async Task ExthHeader_ReadsBookIdentification()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);

        _output.WriteLine($"ASIN: {mobi.MobiHeader.ExthHeader.ASIN}");
        _output.WriteLine($"ASIN2: {mobi.MobiHeader.ExthHeader.ASIN2}");
        _output.WriteLine($"ISBN: {mobi.MobiHeader.ExthHeader.IBSN}");
        _output.WriteLine($"Source: {mobi.MobiHeader.ExthHeader.Source}");
        _output.WriteLine($"CDE Type: {mobi.MobiHeader.ExthHeader.CDEType}");
    }

    [SkippableFact]
    public async Task ExthHeader_ReadsCoverInfo()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kindle_images.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);

        _output.WriteLine($"Cover Offset: {mobi.MobiHeader.ExthHeader.CoverOffset}");
        _output.WriteLine($"Thumb Offset: {mobi.MobiHeader.ExthHeader.ThumbOffset}");
        _output.WriteLine($"Has Fake Cover: {mobi.MobiHeader.ExthHeader.HasFakeCover}");
    }

    [SkippableFact]
    public async Task ExthHeader_ReadsCreatorInfo()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);

        _output.WriteLine($"Creator Software: {mobi.MobiHeader.ExthHeader.CreatorSoftware}");
        _output.WriteLine($"Creator Major Version: {mobi.MobiHeader.ExthHeader.CreatorMajorVersion}");
        _output.WriteLine($"Creator Minor Version: {mobi.MobiHeader.ExthHeader.CreatorMinorVersion}");
        _output.WriteLine($"Creator Build Number: {mobi.MobiHeader.ExthHeader.CreatorBuildNumber}");
        _output.WriteLine($"Creator Build String: {mobi.MobiHeader.ExthHeader.CreatorBuildString}");
    }

    [SkippableFact]
    public async Task ExthHeader_ReadsKf8SpecificFields()
    {
        var filePath = _downloader.GetFilePath("frankenstein_kf8.mobi");
        if (!File.Exists(filePath)) throw new Xunit.SkipException("Test file not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);

        _output.WriteLine($"KF8 Boundary Offset: {mobi.MobiHeader.ExthHeader.Kf8BoundaryOffset}");
        _output.WriteLine($"Book Type: {mobi.MobiHeader.ExthHeader.BookType}");
        _output.WriteLine($"Fixed Layout: {mobi.MobiHeader.ExthHeader.FixedLayout}");
        _output.WriteLine($"Orientation Lock: {mobi.MobiHeader.ExthHeader.OrientationLock}");
    }

    [SkippableTheory]
    [InlineData("frankenstein_kf8.mobi")]
    [InlineData("alice_wonderland_kf8.mobi")]
    [InlineData("moby_dick_kf8.mobi")]
    public async Task ExthHeader_AllKf8Files_HaveAuthor(string filename)
    {
        var filePath = _downloader.GetFilePath(filename);
        if (!File.Exists(filePath)) throw new Xunit.SkipException($"Test file {filename} not downloaded");

        await using var stream = File.OpenRead(filePath);
        var mobi = new MobiMetadata();
        await mobi.ReadMetadataAsync(stream);

        Assert.NotNull(mobi.MobiHeader.ExthHeader);
        Assert.False(string.IsNullOrEmpty(mobi.MobiHeader.ExthHeader.Author));

        _output.WriteLine($"[{filename}] Author: {mobi.MobiHeader.ExthHeader.Author}");
    }

    #endregion

    private static string? Truncate(string? text, int maxLength)
    {
        if (text == null || text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }
}
