namespace MobiMetadata;

public class NcxNavigation
{
    private readonly List<NcxEntry> _entries;
    private readonly List<GuideEntry> _guide;

    public IReadOnlyList<NcxEntry> Entries => _entries;
    public IReadOnlyList<GuideEntry> Guide => _guide;

    public NcxNavigation(IEnumerable<NcxEntry>? entries = null, IEnumerable<GuideEntry>? guide = null)
    {
        _entries = entries?.ToList() ?? [];
        _guide = guide?.ToList() ?? [];
    }

    public List<NcxEntry> BuildHierarchy()
    {
        if (_entries.Count == 0) return [];

        var roots = new List<NcxEntry>();
        var levelStack = new Dictionary<uint, NcxEntry>();

        foreach (var entry in _entries)
        {
            if (entry.Level == 0)
            {
                roots.Add(entry);
            }
            else if (levelStack.TryGetValue(entry.Level - 1, out var parent))
            {
                parent.Children.Add(entry);
            }

            levelStack[entry.Level] = entry;
        }

        return roots;
    }

    public List<NcxEntry> BuildHierarchicalToc() => BuildHierarchy();

    public NcxEntry? FindChapter(string title)
    {
        return FindRecursive(_entries, title);

        static NcxEntry? FindRecursive(IEnumerable<NcxEntry> entries, string search)
        {
            foreach (var entry in entries)
            {
                if (entry.Title?.Equals(search, StringComparison.OrdinalIgnoreCase) == true)
                    return entry;

                var found = FindRecursive(entry.Children, search);
                if (found != null) return found;
            }
            return null;
        }
    }

    public List<NcxEntry> GetAllChapters()
    {
        var result = new List<NcxEntry>();
        Flatten(_entries, result);
        return result;

        static void Flatten(IEnumerable<NcxEntry> entries, List<NcxEntry> output)
        {
            foreach (var entry in entries)
            {
                output.Add(entry);
                Flatten(entry.Children, output);
            }
        }
    }
}

public record GuideEntry(string? Type, string? Title, string? Href)
{
    public static GuideEntry Cover(string href) => new("cover", "Cover", href);
    public static GuideEntry Toc(string href) => new("toc", "Table of Contents", href);
    public static GuideEntry Start(string href) => new("text", "Start", href);
}
