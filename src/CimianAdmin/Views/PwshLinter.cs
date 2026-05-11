namespace CimianAdmin.Views;

using System.Globalization;

/// <summary>
/// Tiny structural PowerShell linter. Reports per-line issues for things that don't need a
/// full parse to catch: unbalanced braces/parens/brackets, unterminated quotes, suspicious
/// patterns like <c>Wrte-Host</c> typos. Strings and comments are tracked while scanning so
/// braces inside a quoted string don't get counted as unmatched.
/// </summary>
public static class PwshLinter
{
    public static List<string> Analyze(string text)
    {
        var issues = new List<string>();
        if (string.IsNullOrEmpty(text)) return issues;

        var lines = text.Split('\n');

        // Bracket balance across the whole script.
        int braces = 0, parens = 0, brackets = 0;
        int firstUnbalanceLine = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            // Per-line quote / brace counting. We track which quote we're inside so a `}`
            // inside a single- or double-quoted string doesn't decrement the brace count.
            char quote = '\0'; // '\0' = not in a string
            for (var j = 0; j < line.Length; j++)
            {
                var c = line[j];

                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    continue;
                }

                // PowerShell line comment — rest of line is ignored.
                if (c == '#') break;

                switch (c)
                {
                    case '\'': quote = '\''; break;
                    case '"': quote = '"'; break;
                    case '{': braces++; break;
                    case '}': braces--; if (braces < 0 && firstUnbalanceLine == 0) firstUnbalanceLine = lineNumber; break;
                    case '(': parens++; break;
                    case ')': parens--; if (parens < 0 && firstUnbalanceLine == 0) firstUnbalanceLine = lineNumber; break;
                    case '[': brackets++; break;
                    case ']': brackets--; if (brackets < 0 && firstUnbalanceLine == 0) firstUnbalanceLine = lineNumber; break;
                }
            }

            if (quote != '\0')
            {
                issues.Add($"Line {Fmt(lineNumber)}: unterminated {(quote == '"' ? "double" : "single")}-quoted string");
            }

            // Cheap typo check — Write-Host is so common a misspelling will likely surface.
            if (line.Contains("Wrte-Host", System.StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Line {Fmt(lineNumber)}: did you mean Write-Host?");
            }
        }

        if (braces != 0)
        {
            issues.Add($"Unbalanced braces (net {Fmt(braces)}{(firstUnbalanceLine > 0 ? $", first issue around line {Fmt(firstUnbalanceLine)}" : string.Empty)})");
        }
        if (parens != 0)
        {
            issues.Add($"Unbalanced parentheses (net {Fmt(parens)})");
        }
        if (brackets != 0)
        {
            issues.Add($"Unbalanced brackets (net {Fmt(brackets)})");
        }

        return issues;
    }

    private static string Fmt(int n) => n.ToString(CultureInfo.InvariantCulture);
}
