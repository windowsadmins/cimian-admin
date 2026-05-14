namespace CimianStudio.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

/// <summary>
/// Full-screen-ish in-page script editor. Opened from the small expand button on
/// <see cref="ScriptEditor"/>; on confirmation, the edited text replaces the original.
/// Includes a live PowerShell lint panel that flags simple structural issues
/// (unbalanced braces/parens/quotes, blatant typos) — not a substitute for
/// PSScriptAnalyzer, but enough to catch most paste mistakes before saving.
/// </summary>
public sealed partial class ScriptFullEditorDialog : ContentDialog
{
    public ScriptFullEditorDialog()
    {
        InitializeComponent();
    }

    public string ScriptText
    {
        get => Editor.ScriptText;
        set => Editor.ScriptText = value;
    }

    public string Label
    {
        get => LabelText.Text;
        set
        {
            LabelText.Text = value;
            Editor.Label = value;
        }
    }

    private void OnScriptChanged(object sender, RoutedEventArgs e) => RunLint();

    /// <summary>
    /// Runs a lightweight structural lint on the current script and pushes the issue list
    /// into the panel at the bottom of the dialog. Each item is "line N: message".
    /// </summary>
    private void RunLint()
    {
        var text = Editor.ScriptText ?? string.Empty;
        var issues = PwshLinter.Analyze(text);
        LintList.ItemsSource = issues.Count == 0
            ? new[] { "No issues detected." }
            : [.. issues];
    }
}
