using System.Text;
using System.Xml;

namespace MobiMetadata;

public class RescRecord : PageRecord
{
    private string _xmlContent = "";

    public int PageCount { get; private set; }

    public RescRecord(Stream stream, long position, uint length, SemaphoreSlim? semaphore = null)
        : base(stream, position, length, 0, semaphore)
    {
    }

    public void AdjustPageCount(int delta) => PageCount += delta;

    public async Task ParseAsync()
    {
        _xmlContent = "";
        PageCount = 0;
        var contentLength = DataLength - 4;
        var data = await ReadDataAsync(4, contentLength).ConfigureAwait(false);

        var xmlStr = Encoding.UTF8.GetString(data.Span).TrimEnd('\0');

        var start = xmlStr.IndexOf('<');
        var end = xmlStr.LastIndexOf('>') + 1;

        if (start >= 0 && end > start)
        {
            _xmlContent = xmlStr[start..end];
        }

        PageCount = CountPages();
    }

    private int CountPages()
    {
        var count = 0;
        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            ConformanceLevel = ConformanceLevel.Fragment,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true
        };

        using var reader = XmlReader.Create(new StringReader(_xmlContent), settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "itemref")
            {
                var idRef = reader.GetAttribute("idref");
                var skelId = reader.GetAttribute("skelid");

                if (idRef != null && skelId != null)
                    count++;
            }
        }

        return count;
    }

    public async Task<string> GetPrettyXmlAsync()
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            ConformanceLevel = ConformanceLevel.Fragment,
            Indent = true,
            Async = true
        };

        using var reader = XmlReader.Create(new StringReader(_xmlContent), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            ConformanceLevel = ConformanceLevel.Fragment
        });

        using var writer = XmlWriter.Create(sb, settings);
        await writer.WriteNodeAsync(reader, false).ConfigureAwait(false);

        return sb.ToString();
    }
}
