using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace MobiMetadata.Tests;

public class ExthValueRegressionTests
{
    [Theory]
    [InlineData(1, 255u)]
    [InlineData(2, 4660u)]
    [InlineData(4, 4294967295u)]
    public async Task NumericValuesAcceptUnsignedBigEndianWidths(int width, uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        var header = await Read((404u, bytes[(4 - width)..]));
        Assert.Equal(value, header.TextToSpeechDisabled);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task NumericValuesRejectUnsupportedWidths(int width)
    {
        var header = await Read((404u, new byte[width]));
        Assert.Throws<MobiMetadataException>(() => header.TextToSpeechDisabled);
    }

    [Fact]
    public async Task ContainerIdentityAndAssetTypeRemainDistinct()
    {
        var header = await Read((542u, Encoding.UTF8.GetBytes("unique-container")),
            (543u, Encoding.UTF8.GetBytes("HD_CONTAINER")));
        Assert.Equal("unique-container", header.ContainerId);
        Assert.Equal("HD_CONTAINER", header.AssetType);
    }

    [Theory]
    [InlineData(1, 2u)]
    [InlineData(2, 258u)]
    [InlineData(4, uint.MaxValue)]
    public async Task VersionIsNumericWithCompatibleStringAccessor(int width, uint value)
    {
        var bytes = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        var header = await Read((114u, bytes[(4 - width)..]));
        Assert.Equal(value, header.NumericVersionNumber);
        Assert.Equal(value.ToString(System.Globalization.CultureInfo.InvariantCulture), header.VersionNumber);
    }

    [Fact]
    public async Task HexAccessorsPreserveBinaryBytes()
    {
        byte[] payload = [0xff, 0, 0xa1];
        var header = await Read((208u, payload), (209u, payload), (300u, payload));
        Assert.Equal("FF00A1", header.WatermarkHexString);
        Assert.Equal("FF00A1", header.TamperProofKeysHexString);
        Assert.Equal("FF00A1", header.FontSignatureHexString);
        Assert.Equal(payload, header.Records[0].RecordData.ToArray());
        // Keep legacy text accessors available with their original interpretation.
        Assert.Equal(Encoding.UTF8.GetString(payload), header.Watermark);
    }

    [Fact]
    public async Task CorrectNamesRetainLegacyAliases()
    {
        var header = await Read((129u, Encoding.UTF8.GetBytes("kindle:embed:1")),
            (538u, Encoding.UTF8.GetBytes("high")));
        Assert.Equal("kindle:embed:1", header.MetadataResourceUri);
        Assert.Equal(header.MetadataResourceUri, header.MastheadCoverImage);
        Assert.Equal(header.MetadataResourceUri, header.K8MastheadCoverImage);
        Assert.Equal("high", header.ResourceContainerFidelity);
        Assert.Equal(header.ResourceContainerFidelity, header.ContainerResolution);
    }

    [Fact]
    public async Task MissingValuesRetainEmptyOrMissingSemantics()
    {
        var header = await Read();
        Assert.Equal(uint.MaxValue, header.TextToSpeechDisabled);
        Assert.Null(header.NumericVersionNumber);
        Assert.Empty(header.VersionNumber);
        Assert.Empty(header.ContainerId);
        Assert.Empty(header.AssetType);
        Assert.Empty(header.WatermarkHexString);
    }

    private static async Task<EXTHHead> Read(params (uint Type, byte[] Payload)[] records)
    {
        var bytes = new byte[12 + records.Sum(r => 8 + r.Payload.Length)];
        "EXTH"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)records.Length);
        int offset = 12;
        foreach (var record in records)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), record.Type);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset + 4), (uint)(8 + record.Payload.Length));
            record.Payload.CopyTo(bytes, offset + 8);
            offset += 8 + record.Payload.Length;
        }
        var header = new EXTHHead();
        using var stream = new MemoryStream(bytes);
        await header.ReadHeaderAsync(stream);
        return header;
    }
}
