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
        var roots = new List<NcxEntry>();
        var ancestors = new Stack<NcxEntry>();
        foreach (var entry in _entries) entry.Children.Clear();
        foreach (var entry in _entries)
        {
            while (ancestors.Count > 0 && ancestors.Peek().Level >= entry.Level) ancestors.Pop();
            if (ancestors.Count == 0) roots.Add(entry);
            else ancestors.Peek().Children.Add(entry);
            ancestors.Push(entry);
        }
        return roots;
    }
    public List<NcxEntry> BuildHierarchicalToc() => BuildHierarchy();
    public NcxEntry? FindChapter(string title) => GetAllChapters().FirstOrDefault(e => e.Title?.Equals(title, StringComparison.OrdinalIgnoreCase) == true);
    public List<NcxEntry> GetAllChapters()
    {
        var result = new List<NcxEntry>();
        var seen = new HashSet<NcxEntry>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<NcxEntry>(_entries.AsEnumerable().Reverse());
        while (pending.TryPop(out var entry))
        {
            if (!seen.Add(entry)) continue;
            result.Add(entry);
            for (int i = entry.Children.Count - 1; i >= 0; i--) pending.Push(entry.Children[i]);
        }
        return result;
    }
}
public record GuideEntry(string? Type, string? Title, string? Href)
{
    public static GuideEntry Cover(string href) => new("cover", "Cover", href);
    public static GuideEntry Toc(string href) => new("toc", "Table of Contents", href);
    public static GuideEntry Start(string href) => new("text", "Start", href);
}
