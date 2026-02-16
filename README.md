# MobiMetadata

A .NET library for reading strongly typed metadata from MOBI/AZW ebook files (DRM-free) and extracting images including HD images from azw.res/azw6 files.

## Features

- Read metadata from MOBI, AZW, and AZW3 files
- Extract cover images and embedded images
- Support for HD image extraction from AZW6/azw.res companion files
- Full KF8 format support with navigation/toc parsing
- Text content extraction with decompression support
- No external dependencies in the main library

## Supported Formats

- MOBI (older Kindle format)
- AZW (Kindle format)
- AZW3/KF8 (Kindle Format 8)
- PDB (Palm Database)
- AZW6/azw.res (HD image containers)

## Installation

### Via NuGet

```bash
dotnet add package MobiMetadata
```

### Via GitHub Packages

Add the GitHub Packages source.

Then install:

```bash
dotnet add package MobiMetadata
```

## Quick Start

### Reading Metadata

```csharp
using MobiMetadata;

// Create a new instance
var metadata = new MobiMetadata();

// Read metadata from a file
await using var stream = File.OpenRead("book.mobi");
await metadata.ReadMetadataAsync(stream);

// Access headers
var pdbHeader = metadata.PdbHeader;      // Palm Database header
var palmDocHeader = metadata.PalmDocHeader; // PalmDOC compression header
var mobiHeader = metadata.MobiHeader;    // MOBI-specific header

// Access book metadata
var title = mobiHeader.FullName;
var author = mobiHeader.ExthHeader?.Author;
var publisher = mobiHeader.ExthHeader?.Publisher;
var description = mobiHeader.ExthHeader?.Description;

// Check format
bool isKf8 = metadata.IsKf8;      // KF8 format (file version >= 8)
bool isDualMobi = metadata.IsDualMobi; // Combined MOBI+KF8
```

### Loading and Extracting Images

```csharp
// Load images from the file
await metadata.LoadImagesAsync();

// Access cover image
var coverBytes = await metadata.GetCoverBytesAsync();
// Or open as a stream
var coverStream = metadata.OpenCoverStream();

// Access all images
foreach (var image in metadata.Images)
{
    var imageBytes = await image.GetImageBytesAsync();
    var mimeType = image.MimeType;
}

// Get specific image by index
var imageBytes = await metadata.GetImageBytesAsync(0);
```

### Loading HD Images

```csharp
// Load images including HD versions from AZW6 companion file
await using var hdStream = File.OpenRead("book.azw.res");
await metadata.LoadImagesAsync(hdStream);

// Check if HD images are available
bool hasHdCover = metadata.HasHdCover();
bool isHdImage = metadata.IsHdImage(0);
```

### KF8 Navigation (Table of Contents)

```csharp
// Parse KF8 specific structures
await metadata.ParseKf8Async();

// Access navigation
if (metadata.Navigation != null)
{
    foreach (var item in metadata.Navigation.Items)
    {
        Console.WriteLine($"{item.Title} - {item.Href}");
    }
}
```

### Text Content Extraction

```csharp
// After reading metadata, access text content
if (metadata.TextContent != null)
{
    var text = await metadata.TextContent.GetTextAsync();
}
```

## API Reference

### MobiMetadata Class

The main entry point for reading MOBI files.

| Property | Description |
|----------|-------------|
| `PdbHeader` | Palm Database header information |
| `PalmDocHeader` | PalmDOC compression header |
| `MobiHeader` | MOBI-specific header with book metadata |
| `TextContent` | Text extraction helper (after `ReadMetadataAsync`) |
| `Kf8Parser` | KF8 format parser (for KF8/Dual MOBI files) |
| `Navigation` | Navigation/toc (after `ParseKf8Async`) |
| `PageRecords` | Image records (after `LoadImagesAsync`) |
| `Images` | List of all images (after `LoadImagesAsync`) |
| `Cover` | Cover image record (after `LoadImagesAsync`) |
| `IsKf8` | True if file is KF8 format |
| `IsDualMobi` | True if file contains both MOBI and KF8 |

| Method | Description |
|--------|-------------|
| `ReadMetadataAsync(stream)` | Read all metadata from the file |
| `LoadImagesAsync(hdStream?)` | Load image records (optionally with HD) |
| `ParseKf8Async()` | Parse KF8 navigation structures |
| `GetCoverBytesAsync()` | Get cover image as byte array |
| `OpenCoverStream()` | Open cover image as stream |
| `GetImageBytesAsync(index)` | Get specific image as byte array |
| `OpenImageStream(index)` | Open specific image as stream |

### Header Classes

- **PDBHead**: Palm Database header - contains record info, file type, creator
- **PalmDOCHead**: Compression header - compression type, text length
- **MobiHead**: MOBI header - title, author, publisher, language, etc.
- **EXTHHead**: Extended header - additional metadata like ASIN, CDE type
- **Azw6Head**: AZW6 HD image container header

## Compression Support

The library supports the following compression formats:

- No compression
- PalmDOC compression
- HUFF/CDIC compression (Huffman coding)

## Building

```bash
# Clone the repository
git clone https://github.com/Cularr/MobiMetadata.git
cd MobiMetadata

# Build
dotnet build

# Run tests
dotnet test
```

## Requirements

- .NET 10.0 or later

## License

MIT License - see [LICENSE](LICENSE) for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.