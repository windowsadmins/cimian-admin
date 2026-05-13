namespace CimianAdmin.Core.Models.Search;

using CimianAdmin.Core.Models.Manifests;

/// <summary>
/// Manifest-side parallel to <see cref="SmartSearchPredicate"/>. Field set is
/// different (lists like managed_installs, included_manifests) but the
/// operator semantics are shared via <see cref="SmartSearchOperator"/>.
/// </summary>
public sealed class ManifestSearchPredicate
{
    public bool MatchAll { get; set; } = true;
    public List<ManifestSearchRule> Rules { get; set; } = [];
    public bool IsEmpty => Rules.Count == 0;
}

public sealed class ManifestSearchRule
{
    public ManifestSearchField Field { get; set; } = ManifestSearchField.Name;
    public SmartSearchOperator Op { get; set; } = SmartSearchOperator.Contains;
    public string Value { get; set; } = string.Empty;
}

public enum ManifestSearchField
{
    Name,
    DisplayName,
    Notes,
    FilePath,
    Catalog,
    ManagedInstall,
    ManagedUninstall,
    ManagedUpdate,
    OptionalInstall,
    DefaultInstall,
    IncludedManifest,
}

public static class ManifestSmartFilter
{
    public static bool Matches(Manifest manifest, ManifestSearchPredicate pred)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(pred);
        if (pred.IsEmpty) return true;
        var results = pred.Rules.Select(r => Evaluate(manifest, r));
        return pred.MatchAll ? results.All(static b => b) : results.Any(static b => b);
    }

    private static bool Evaluate(Manifest m, ManifestSearchRule rule)
    {
        if (IsListField(rule.Field))
        {
            // List fields aggregate top-level entries plus every conditional_items
            // bucket — "find manifests that include FooApp" should hit the manifest
            // whether the entry sits at top-level or inside a conditional.
            var values = ReadList(m, rule.Field);
            return EvaluateList(values, rule.Op, rule.Value);
        }
        var text = ReadText(m, rule.Field);
        return EvaluateText(text, rule.Op, rule.Value);
    }

    private static bool IsListField(ManifestSearchField f) => f switch
    {
        ManifestSearchField.Catalog
            or ManifestSearchField.ManagedInstall
            or ManifestSearchField.ManagedUninstall
            or ManifestSearchField.ManagedUpdate
            or ManifestSearchField.OptionalInstall
            or ManifestSearchField.DefaultInstall
            or ManifestSearchField.IncludedManifest => true,
        _ => false,
    };

    private static string ReadText(Manifest m, ManifestSearchField f) => f switch
    {
        ManifestSearchField.Name => m.Name ?? string.Empty,
        ManifestSearchField.DisplayName => m.DisplayName ?? string.Empty,
        ManifestSearchField.Notes => m.Notes ?? string.Empty,
        ManifestSearchField.FilePath => m.FilePath ?? string.Empty,
        _ => string.Empty,
    };

    private static IReadOnlyList<string> ReadList(Manifest m, ManifestSearchField f)
    {
        var top = ReadTopLevel(m, f);
        if (m.ConditionalItems is not { Count: > 0 } conds) return top;

        var merged = new List<string>(top);
        Walk(conds, f, merged);
        return merged;

        static void Walk(List<ConditionalItem> items, ManifestSearchField field, List<string> sink)
        {
            foreach (var c in items)
            {
                var bucket = ReadConditional(c, field);
                if (bucket is { Count: > 0 }) sink.AddRange(bucket);
                if (c.NestedConditionalItems is { Count: > 0 } nested) Walk(nested, field, sink);
            }
        }
    }

    private static IReadOnlyList<string> ReadTopLevel(Manifest m, ManifestSearchField f) => f switch
    {
        ManifestSearchField.Catalog => (IReadOnlyList<string>?)m.Catalogs ?? [],
        ManifestSearchField.ManagedInstall => (IReadOnlyList<string>?)m.ManagedInstalls ?? [],
        ManifestSearchField.ManagedUninstall => (IReadOnlyList<string>?)m.ManagedUninstalls ?? [],
        ManifestSearchField.ManagedUpdate => (IReadOnlyList<string>?)m.ManagedUpdates ?? [],
        ManifestSearchField.OptionalInstall => (IReadOnlyList<string>?)m.OptionalInstalls ?? [],
        ManifestSearchField.DefaultInstall => (IReadOnlyList<string>?)m.DefaultInstalls ?? [],
        ManifestSearchField.IncludedManifest => (IReadOnlyList<string>?)m.IncludedManifests ?? [],
        _ => [],
    };

    private static List<string>? ReadConditional(ConditionalItem c, ManifestSearchField f) => f switch
    {
        // Catalogs aren't a conditional-bucket field — only the manifest top-level
        // declares catalogs. Return null so the merger skips.
        ManifestSearchField.ManagedInstall => c.ManagedInstalls,
        ManifestSearchField.ManagedUninstall => c.ManagedUninstalls,
        ManifestSearchField.ManagedUpdate => c.ManagedUpdates,
        ManifestSearchField.OptionalInstall => c.OptionalInstalls,
        ManifestSearchField.IncludedManifest => c.IncludedManifests,
        _ => null,
    };

    private static bool EvaluateText(string haystack, SmartSearchOperator op, string needle) => op switch
    {
        SmartSearchOperator.Contains => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase),
        SmartSearchOperator.DoesNotContain => !haystack.Contains(needle, StringComparison.OrdinalIgnoreCase),
        SmartSearchOperator.Equals => string.Equals(haystack, needle, StringComparison.OrdinalIgnoreCase),
        SmartSearchOperator.StartsWith => haystack.StartsWith(needle, StringComparison.OrdinalIgnoreCase),
        SmartSearchOperator.EndsWith => haystack.EndsWith(needle, StringComparison.OrdinalIgnoreCase),
        SmartSearchOperator.IsEmpty => string.IsNullOrEmpty(haystack),
        SmartSearchOperator.IsNotEmpty => !string.IsNullOrEmpty(haystack),
        _ => false,
    };

    private static bool EvaluateList(IReadOnlyList<string> values, SmartSearchOperator op, string needle) => op switch
    {
        SmartSearchOperator.Contains => values.Any(v => v.Contains(needle, StringComparison.OrdinalIgnoreCase)),
        SmartSearchOperator.DoesNotContain => !values.Any(v => v.Contains(needle, StringComparison.OrdinalIgnoreCase)),
        SmartSearchOperator.Equals => values.Any(v => string.Equals(v, needle, StringComparison.OrdinalIgnoreCase)),
        SmartSearchOperator.StartsWith => values.Any(v => v.StartsWith(needle, StringComparison.OrdinalIgnoreCase)),
        SmartSearchOperator.EndsWith => values.Any(v => v.EndsWith(needle, StringComparison.OrdinalIgnoreCase)),
        SmartSearchOperator.IsEmpty => values.Count == 0,
        SmartSearchOperator.IsNotEmpty => values.Count > 0,
        _ => false,
    };
}
