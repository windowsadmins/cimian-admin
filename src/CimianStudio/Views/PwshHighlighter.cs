namespace CimianStudio.Views;

using System.Text.RegularExpressions;
using Windows.UI;

/// <summary>
/// Tiny regex-based PowerShell tokenizer for syntax highlighting in <see cref="ScriptEditor"/>.
/// Not a real parser — good enough to color comments, strings, variables, cmdlets, keywords,
/// and numbers in typical pkginfo scripts.
/// </summary>
public static partial class PwshHighlighter
{
    public enum TokenKind { Comment, StringLiteral, Variable, Keyword, Cmdlet, Number }

    public readonly record struct Token(int Start, int Length, TokenKind Kind);

    [GeneratedRegex(
        // Order matters: block comments before line comments, comments/strings before
        // identifiers (so a `#` inside a string isn't picked up as a comment).
        @"(?<bcomment><\#[\s\S]*?\#>)" +
        @"|(?<comment>\#[^\r\n]*)" +
        @"|(?<dstring>""(?:[^""`]|`.)*"")" +
        @"|(?<sstring>'(?:[^']|'')*')" +
        @"|(?<variable>\$[\w:]+)" +
        @"|(?<keyword>\b(?:if|else|elseif|foreach|for|while|do|switch|return|function|param|begin|process|end|try|catch|finally|throw|break|continue|exit|in|true|false|null)\b)" +
        @"|(?<cmdlet>\b[A-Za-z][A-Za-z0-9]*-[A-Za-z][A-Za-z0-9]*\b)" +
        @"|(?<number>\b\d+(?:\.\d+)?\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IEnumerable<Token> Tokenize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        foreach (Match m in TokenRegex().Matches(text))
        {
            var kind = ClassifyMatch(m);
            yield return new Token(m.Index, m.Length, kind);
        }
    }

    private static TokenKind ClassifyMatch(Match m)
    {
        if (m.Groups["bcomment"].Success || m.Groups["comment"].Success) return TokenKind.Comment;
        if (m.Groups["dstring"].Success || m.Groups["sstring"].Success) return TokenKind.StringLiteral;
        if (m.Groups["variable"].Success) return TokenKind.Variable;
        if (m.Groups["keyword"].Success) return TokenKind.Keyword;
        if (m.Groups["cmdlet"].Success) return TokenKind.Cmdlet;
        return TokenKind.Number;
    }

    /// <summary>
    /// Theme-aware palette. Dark side mirrors VS Code Dark+; light side is tuned down so
    /// the pale yellows/greens don't disappear into a white background.
    /// </summary>
    public static Color ColorFor(TokenKind kind, bool isDark) => isDark ? DarkPalette(kind) : LightPalette(kind);

    private static Color DarkPalette(TokenKind kind) => kind switch
    {
        TokenKind.Comment => Color.FromArgb(0xFF, 0x6A, 0x99, 0x55),
        TokenKind.StringLiteral => Color.FromArgb(0xFF, 0xCE, 0x91, 0x78),
        TokenKind.Variable => Color.FromArgb(0xFF, 0x4F, 0xC1, 0xFF),
        TokenKind.Keyword => Color.FromArgb(0xFF, 0x56, 0x9C, 0xD6),
        TokenKind.Cmdlet => Color.FromArgb(0xFF, 0xDC, 0xDC, 0xAA),
        TokenKind.Number => Color.FromArgb(0xFF, 0xB5, 0xCE, 0xA8),
        _ => Color.FromArgb(0xFF, 0xD4, 0xD4, 0xD4),
    };

    private static Color LightPalette(TokenKind kind) => kind switch
    {
        TokenKind.Comment => Color.FromArgb(0xFF, 0x00, 0x80, 0x00),
        TokenKind.StringLiteral => Color.FromArgb(0xFF, 0xA3, 0x15, 0x15),
        TokenKind.Variable => Color.FromArgb(0xFF, 0x00, 0x70, 0xC1),
        TokenKind.Keyword => Color.FromArgb(0xFF, 0x00, 0x00, 0xFF),
        TokenKind.Cmdlet => Color.FromArgb(0xFF, 0x79, 0x5E, 0x26),
        TokenKind.Number => Color.FromArgb(0xFF, 0x09, 0x88, 0x58),
        _ => Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F),
    };
}
