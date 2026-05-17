namespace CimianStudio.Core.Models.Predicates;

using System.Globalization;
using System.Text;

/// <summary>
/// Renders a <see cref="PredicateBuilder"/> back to an NSPredicate-flavoured string,
/// matching the syntax Cimian's <c>pkg/predicates</c> evaluator accepts.
/// </summary>
public static class PredicateSerializer
{
    public static string ToPredicateString(PredicateBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Rows.Count == 0) return string.Empty;

        var parts = new List<string>();
        foreach (var row in builder.Rows)
        {
            var rendered = RenderRow(row);
            if (!string.IsNullOrEmpty(rendered)) parts.Add(rendered);
        }
        if (parts.Count == 0) return string.Empty;

        return builder.Compound switch
        {
            CompoundType.Any => string.Join(" OR ", parts),
            CompoundType.None => "NOT (" + string.Join(" AND ", parts) + ")",
            _ => string.Join(" AND ", parts),
        };
    }

    private static string RenderRow(PredicateRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Keypath)) return string.Empty;

        var keypath = PredicateKeypaths.Find(row.Keypath);
        var value = RenderValue(row.Value, keypath?.ValueType ?? PredicateValueType.String);

        // Collection facts compare against a single membership value via ANY.
        if (keypath?.ValueType == PredicateValueType.StringList)
        {
            var op = row.OperatorToken switch
            {
                "CONTAINS" => "==",
                "NOT CONTAINS" => "!=",
                _ => row.OperatorToken,
            };
            return new StringBuilder()
                .Append("ANY ").Append(row.Keypath).Append(' ').Append(op).Append(' ').Append(value)
                .ToString();
        }

        return new StringBuilder()
            .Append(row.Keypath).Append(' ').Append(row.OperatorToken).Append(' ').Append(value)
            .ToString();
    }

    private static string RenderValue(string raw, PredicateValueType type)
    {
        raw ??= string.Empty;
        return type switch
        {
            PredicateValueType.Integer or PredicateValueType.Boolean =>
                raw.Length == 0 ? "0" : raw.Trim(),
            PredicateValueType.Date => $"'{Escape(raw)}'",
            _ => $"'{Escape(raw)}'",
        };
    }

    private static string Escape(string s) => s.Replace("'", "\\'", StringComparison.Ordinal);

    public static string ToFriendlyString(PredicateBuilder builder)
    {
        // Used as the display label for the conditional summary. Same shape as the
        // canonical form for now — keeping a separate seam in case we want a more
        // human description later.
        return ToPredicateString(builder);
    }

    public static string FormatNumber(double n) =>
        n.ToString(CultureInfo.InvariantCulture);
}
