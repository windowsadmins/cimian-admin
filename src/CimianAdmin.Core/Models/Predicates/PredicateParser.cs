namespace CimianAdmin.Core.Models.Predicates;

using System.Text.RegularExpressions;

/// <summary>
/// Best-effort string → <see cref="PredicateBuilder"/> parser. Handles the subset of
/// NSPredicate syntax that the UI emits (and common hand-written variants); anything
/// fancier (parentheses, mixed AND/OR, nested NOTs) falls back to <see cref="TryParse"/>
/// returning false so the caller can drop the user into the Custom tab.
/// </summary>
public static class PredicateParser
{
    private static readonly Regex AnyRow = new(
        @"^\s*ANY\s+(?<key>\w+)\s+(?<op>==|!=|<=|>=|<|>|CONTAINS|BEGINSWITH|ENDSWITH|LIKE|MATCHES)\s+(?<val>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SimpleRow = new(
        @"^\s*(?<key>\w+)\s+(?<op>==|!=|<=|>=|<|>|CONTAINS|BEGINSWITH|ENDSWITH|LIKE|MATCHES)\s+(?<val>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string predicate, out PredicateBuilder builder)
    {
        builder = new PredicateBuilder();
        if (string.IsNullOrWhiteSpace(predicate)) return true;

        var text = predicate.Trim();
        var compound = CompoundType.All;
        var separator = " AND ";

        // Detect outer NOT(...) wrapper for the "None" compound.
        if (text.StartsWith("NOT", StringComparison.OrdinalIgnoreCase))
        {
            var inner = text[3..].TrimStart();
            if (inner.StartsWith('(') && inner.EndsWith(')'))
            {
                compound = CompoundType.None;
                text = inner[1..^1].Trim();
            }
        }

        // Detect compound joiner. If both AND and OR appear, we can't represent that
        // in the simple flat model — bail to Custom.
        var hasAnd = ContainsKeyword(text, "AND");
        var hasOr = ContainsKeyword(text, "OR");
        if (hasAnd && hasOr) return false;
        if (hasOr)
        {
            compound = CompoundType.Any;
            separator = " OR ";
        }
        else if (hasAnd)
        {
            separator = " AND ";
        }

        builder.Compound = compound;
        var rawRows = SplitByKeyword(text, separator.Trim());

        foreach (var raw in rawRows)
        {
            var row = ParseRow(raw);
            if (row is null) return false;
            builder.Rows.Add(row);
        }

        return true;
    }

    private static PredicateRow? ParseRow(string raw)
    {
        var match = AnyRow.Match(raw);
        var isAny = match.Success;
        if (!isAny) match = SimpleRow.Match(raw);
        if (!match.Success) return null;

        var key = match.Groups["key"].Value;
        var op = match.Groups["op"].Value.ToUpperInvariant();
        var val = UnquoteValue(match.Groups["val"].Value);

        // Translate `ANY catalogs == 'X'` back into the friendly Contains for the UI.
        if (isAny)
        {
            op = op switch
            {
                "==" => "CONTAINS",
                "!=" => "NOT CONTAINS",
                _ => op,
            };
        }

        return new PredicateRow
        {
            Keypath = key,
            OperatorToken = op,
            Value = val,
        };
    }

    private static string UnquoteValue(string raw)
    {
        var v = raw.Trim();
        if (v.Length >= 2 && (v[0] == '\'' || v[0] == '"') && v[^1] == v[0])
        {
            v = v[1..^1].Replace("\\'", "'", StringComparison.Ordinal);
        }
        return v;
    }

    private static bool ContainsKeyword(string text, string keyword)
    {
        return Regex.IsMatch(text, $@"\b{keyword}\b", RegexOptions.IgnoreCase);
    }

    private static List<string> SplitByKeyword(string text, string keyword)
    {
        var parts = Regex.Split(text, $@"\s+{keyword}\s+", RegexOptions.IgnoreCase);
        var trimmed = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) trimmed.Add(t);
        }
        return trimmed;
    }
}
