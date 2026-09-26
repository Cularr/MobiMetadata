using System.Text;

namespace MobiMetadata;

internal static class MobiEncoding
{
    internal static Encoding GetEncoding(uint codePage) => codePage switch
    {
        65001 => Encoding.UTF8,
        1252 => CodePagesEncodingProvider.Instance.GetEncoding(1252)!,
        _ => throw new MobiMetadataException($"Unsupported MOBI text encoding: {codePage}")
    };
}
