namespace CimianStudio.Views;

using System.Globalization;

/// <summary>
/// Shared timestamp rendering for editor headers. UTC values from the model
/// are converted to local time and formatted with a locale-neutral pattern so
/// the output is stable regardless of system culture. Both editors render
/// "Created · Modified" identically — extracted here to avoid drift.
/// </summary>
internal static class TimestampFormatter
{
    public static string FormatCreatedModified(DateTime? created, DateTime? modified)
    {
        var c = Fmt(created);
        var m = Fmt(modified);
        if (string.IsNullOrEmpty(c) && string.IsNullOrEmpty(m)) return string.Empty;
        if (string.IsNullOrEmpty(c)) return $"Modified {m}";
        if (string.IsNullOrEmpty(m)) return $"Created {c}";
        return $"Created {c}  ·  Modified {m}";
    }

    private static string Fmt(DateTime? t) => t is null
        ? string.Empty
        : t.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
