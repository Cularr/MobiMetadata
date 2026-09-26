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
        var parents = ResolveParents(_entries);
        var roots = new List<NcxEntry>();
        foreach (var entry in _entries) entry.Children.Clear();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (parents[i] < 0) roots.Add(_entries[i]);
            else _entries[parents[i]].Children.Add(_entries[i]);
        }
        return roots;
    }

    // Resolve and validate before mutating Children. Each parent edge is visited
    // at most twice, avoiding recursion and quadratic walks on deeply nested TOCs.
    internal static int[] ResolveParents(IReadOnlyList<NcxEntry> entries)
    {
        var parents = new int[entries.Count];
        var ancestors = new Stack<int>();
        var seen = new HashSet<NcxEntry>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (!seen.Add(entry)) throw new MobiMetadataException("Duplicate navigation entry reference.");
            while (ancestors.Count > 0 && entries[ancestors.Peek()].Level >= entry.Level) ancestors.Pop();
            if (entry.ParentIndex is uint parent)
            {
                if (parent != uint.MaxValue && parent >= entries.Count)
                    throw new MobiMetadataException("NCX parent index is outside the navigation table.");
                parents[i] = parent == uint.MaxValue ? -1 : (int)parent;
            }
            else parents[i] = ancestors.Count == 0 ? -1 : ancestors.Peek();
            ancestors.Push(i);
        }
        var state = new byte[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            int current = i;
            while (current >= 0 && state[current] == 0)
            {
                state[current] = 1;
                current = parents[current];
            }
            if (current >= 0 && state[current] == 1)
                throw new MobiMetadataException("NCX parent relationships contain a cycle.");
            current = i;
            while (current >= 0 && state[current] == 1)
            {
                state[current] = 2;
                current = parents[current];
            }
        }
        return parents;
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
