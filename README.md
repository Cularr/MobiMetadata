# MobiMetadata

A .NET library for reading strongly typed metadata from MOBI/AZW ebook files (DRM-free) and extracting images including HD images from azw.res/azw6 files.

## Features

- Read metadata from MOBI, AZW, and AZW3 files
- Extract cover images and embedded images
- Support for HD image extraction from AZW6/azw.res companion files
- KF8 structure and navigation/toc parsing
- Text content extraction with decompression support
- No external dependencies in the main library

## Supported Formats

- MOBI (older Kindle format)
- AZW (Kindle format)
- AZW3/KF8 (Kindle Format 8)
- BOOK/MOBI Palm Database containers
- AZW6/azw.res (HD image containers)

KFX, KPF, Topaz, generic PDB ebook formats, and encrypted text are not supported.
KF8 text extraction returns the decompressed raw content; it does not reconstruct a
complete EPUB or render the book. Rare INDX encoding 65002 is explicitly unsupported.

## Installation

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
    foreach (var item in metadata.Navigation.Entries)
    {
        Console.WriteLine($"{item.Title} - offset {item.Offset}");
    }
}
```

### Text Content Extraction

```csharp
// After reading metadata, access text content
if (metadata.TextContent != null)
{
    var text = await metadata.TextContent.ExtractTextAsync();
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
| `Images` | Page images, without the cover and thumbnail (after `LoadImagesAsync`) |
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

## Reading and Resource Limits

Input streams must be readable and seekable and remain open while extracting data.
The library does not own the streams. Its extraction operations coordinate access to
shared streams; callers must not independently move or modify those streams during
an operation. Reusing an instance clears the previous book's results, including
after a failed read. Previously returned objects belong to the previous read.

In dual MOBI files, text and KF8 structures use the KF8 rendition. `MobiHeader` and
`IsKf8` describe the primary header; `IsDualMobi` identifies a validated second rendition.
`Images` and the indexed image accessors select the same HD resource when available.
`PageCount` counts extracted non-cover images, not printed pages or RESC spine entries.

Allocation limits are 16 MiB for header data, 64 MiB for an individual buffered
record or decompression result, and 256 MiB for extracted text. HUFF dictionary data
and each index are limited to 64 MiB, with bounded entry counts and decoding work.
`OpenImageStream` can read larger image records without allocating their complete
contents. Malformed sizes, invalid compression references and encrypted text produce
`MobiMetadataException`. Raw image bytes and text remain untrusted content for any
renderer or image decoder used by the calling application.

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
