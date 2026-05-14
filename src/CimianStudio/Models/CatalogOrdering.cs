namespace CimianStudio.Models;

/// <summary>
/// Stable ordering for catalog names so editors render checkboxes in a
/// predictable pipeline order (Development → Testing → Staging → Production)
/// and the YAML round-trip preserves it.  Anything outside the preferred list
/// falls back to alphabetical at the end.
/// </summary>
public static class CatalogOrdering
{
    // Hard-coded for now; can be promoted to AppSettings later for repos with
    // different pipelines.
    public static IReadOnlyList<string> PreferredOrder { get; } =
        ["Development", "Testing", "Staging", "Production"];

    public static IEnumerable<string> Sort(IEnumerable<string> catalogs)
    {
        ArgumentNullException.ThrowIfNull(catalogs);

        var remaining = new List<string>(catalogs);
        foreach (var preferred in PreferredOrder)
        {
            var match = remaining.FirstOrDefault(
                c => string.Equals(c, preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                yield return match;
                remaining.RemoveAll(
                    c => string.Equals(c, preferred, StringComparison.OrdinalIgnoreCase));
            }
        }

        foreach (var c in remaining.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
        {
            yield return c;
        }
    }
}
