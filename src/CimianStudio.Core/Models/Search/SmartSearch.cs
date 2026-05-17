namespace CimianStudio.Core.Models.Search;

using CimianStudio.Core.Models.Packages;

/// <summary>
/// MunkiAdmin-style criteria search: a small list of attribute · operator ·
/// value rows joined by All or Any. The evaluator runs on the in-memory list
/// so it composes with the existing free-text search box and the group-by /
/// catalog filters.
/// </summary>
public sealed class SmartSearchPredicate
{
    public bool MatchAll { get; set; } = true;
    public List<SmartSearchRule> Rules { get; set; } = [];
    public bool IsEmpty => Rules.Count == 0;
}

public sealed class SmartSearchRule
{
    public SmartSearchField Field { get; set; } = SmartSearchField.Name;
    public SmartSearchOperator Op { get; set; } = SmartSearchOperator.Contains;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Fields the user can target. Text fields (Name, DisplayName, ...) are
/// straightforward string compares; list fields (Catalog, Requires, ...) treat
/// "Contains" as "any element contains" and "Equals" as "any element equals".
/// </summary>
public enum SmartSearchField
{
    Name,
    DisplayName,
    Version,
    Category,
    Developer,
    Description,
    Catalog,
    InstallerType,
    Requires,
    BlockingApplication,
    SupportedArchitecture,
}

public enum SmartSearchOperator
{
    Contains,
    DoesNotContain,
    Equals,
    StartsWith,
    EndsWith,
    IsEmpty,
    IsNotEmpty,
}

public static class PackageSmartFilter
{
    public static bool Matches(Package pkg, SmartSearchPredicate pred)
    {
        ArgumentNullException.ThrowIfNull(pkg);
        ArgumentNullException.ThrowIfNull(pred);
        if (pred.IsEmpty) return true;

        var results = pred.Rules.Select(r => Evaluate(pkg, r));
        return pred.MatchAll ? results.All(static b => b) : results.Any(static b => b);
    }

    private static bool Evaluate(Package pkg, SmartSearchRule rule)
    {
        if (IsListAttribute(rule.Field))
        {
            var list = ReadList(pkg, rule.Field);
            return EvaluateList(list, rule.Op, rule.Value);
        }

        var text = ReadText(pkg, rule.Field);
        return EvaluateText(text, rule.Op, rule.Value);
    }

    private static bool IsListAttribute(SmartSearchField attr) => attr switch
    {
        SmartSearchField.Catalog
            or SmartSearchField.Requires
            or SmartSearchField.BlockingApplication
            or SmartSearchField.SupportedArchitecture => true,
        _ => false,
    };

    private static string ReadText(Package pkg, SmartSearchField attr) => attr switch
    {
        SmartSearchField.Name => pkg.Name ?? string.Empty,
        SmartSearchField.DisplayName => pkg.DisplayName ?? string.Empty,
        SmartSearchField.Version => pkg.Version ?? string.Empty,
        SmartSearchField.Category => pkg.Category ?? string.Empty,
        SmartSearchField.Developer => pkg.Developer ?? string.Empty,
        SmartSearchField.Description => pkg.Description ?? string.Empty,
        SmartSearchField.InstallerType => pkg.Installer?.Type ?? string.Empty,
        _ => string.Empty,
    };

    private static IReadOnlyList<string> ReadList(Package pkg, SmartSearchField attr) => attr switch
    {
        SmartSearchField.Catalog => (IReadOnlyList<string>?)pkg.Catalogs ?? [],
        SmartSearchField.Requires => (IReadOnlyList<string>?)pkg.Requires ?? [],
        SmartSearchField.BlockingApplication => (IReadOnlyList<string>?)pkg.BlockingApplications ?? [],
        SmartSearchField.SupportedArchitecture => (IReadOnlyList<string>?)pkg.SupportedArchitectures ?? [],
        _ => [],
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
