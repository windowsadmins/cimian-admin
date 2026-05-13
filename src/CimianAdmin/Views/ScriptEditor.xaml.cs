namespace CimianAdmin.Views;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

/// <summary>
/// Code-style script editor with PowerShell syntax highlighting.
/// Uses <see cref="RichEditBox"/> so we can color characters via <c>ITextDocument</c>.
/// Highlighting runs on a 150 ms debounce after the last keystroke to keep typing snappy.
/// </summary>
public sealed partial class ScriptEditor : UserControl
{
    private readonly DispatcherQueueTimer _highlightTimer;
    private bool _suppressHighlight;
    private bool _suppressTextChanged;
    private ScrollViewer? _editorScrollViewer;
    private int _lineCount = 1;

    public event RoutedEventHandler? ScriptChanged;

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    /// <summary>
    /// Gets or sets the script text. RichEditBox uses <c>\r</c> as its line terminator
    /// internally; we normalise to <c>\n</c> on read so the YAML emitter writes clean
    /// literal blocks without stray carriage returns.
    /// </summary>
    public string ScriptText
    {
        get
        {
            Editor.Document.GetText(TextGetOptions.None, out var text);
            // Strip the trailing carriage return that ITextDocument always tacks on.
            if (text.EndsWith('\r'))
            {
                text = text[..^1];
            }
            return text.Replace("\r", "\n", StringComparison.Ordinal);
        }
        set
        {
            _suppressTextChanged = true;
            try
            {
                Editor.Document.SetText(TextSetOptions.None, value ?? string.Empty);
                ApplyHighlighting();
                _lineCount = 0; // force refresh
                UpdateLineNumbers();
            }
            finally
            {
                _suppressTextChanged = false;
            }
        }
    }

    public ScriptEditor()
    {
        InitializeComponent();
        _highlightTimer = DispatcherQueue.CreateTimer();
        _highlightTimer.Interval = TimeSpan.FromMilliseconds(150);
        _highlightTimer.IsRepeating = false;
        _highlightTimer.Tick += (_, _) => ApplyHighlighting();
    }

    private async void OnExpandClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ScriptFullEditorDialog
        {
            Label = Label,
            ScriptText = ScriptText,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ScriptText = dialog.ScriptText;
            ScriptChanged?.Invoke(this, new RoutedEventArgs());
        }
    }

    private void OnTextChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressHighlight || _suppressTextChanged)
        {
            return;
        }

        _highlightTimer.Stop();
        _highlightTimer.Start();
        UpdateLineNumbers();
        ScriptChanged?.Invoke(this, new RoutedEventArgs());
    }

    private void OnEditorLoaded(object sender, RoutedEventArgs e)
    {
        AttachInnerScrollViewer();
        UpdateLineNumbers();
    }

    private void OnEditorSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Clip geometry needs to match the gutter's actual size or content sticks out
        // when scrolled.
        GutterClipGeometry.Rect = new Rect(0, 0, GutterClip.ActualWidth, GutterClip.ActualHeight);
    }

    private void AttachInnerScrollViewer()
    {
        if (_editorScrollViewer is not null)
        {
            return;
        }

        _editorScrollViewer = FindDescendant<ScrollViewer>(Editor);
        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ViewChanged += OnEditorViewChanged;
        }
    }

    private void OnEditorViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_editorScrollViewer is null) return;
        // Negate the scroll offset and apply to the gutter so the line numbers track.
        GutterScroll.Y = -_editorScrollViewer.VerticalOffset;
    }

    private void UpdateLineNumbers()
    {
        Editor.Document.GetText(TextGetOptions.None, out var text);
        if (text.EndsWith('\r'))
        {
            text = text[..^1];
        }

        // RichEditBox uses \r as the line break.
        var count = 1;
        foreach (var ch in text)
        {
            if (ch == '\r' || ch == '\n')
            {
                count++;
            }
        }

        if (count == _lineCount)
        {
            return;
        }
        _lineCount = count;

        var sb = new System.Text.StringBuilder(count * 4);
        for (var i = 1; i <= count; i++)
        {
            if (i > 1) sb.Append('\n');
            sb.Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        LineNumbers.Text = sb.ToString();
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }
            var match = FindDescendant<T>(child);
            if (match is not null)
            {
                return match;
            }
        }
        return null;
    }

    private void ApplyHighlighting()
    {
        if (_suppressHighlight) return;
        _suppressHighlight = true;
        try
        {
            var doc = Editor.Document;
            doc.GetText(TextGetOptions.None, out var text);
            if (text.EndsWith('\r'))
            {
                text = text[..^1];
            }

            // Save selection so highlighting doesn't move the caret.
            var selStart = doc.Selection.StartPosition;
            var selEnd = doc.Selection.EndPosition;

            // Reset every character to the foreground default.
            var allRange = doc.GetRange(0, text.Length);
            allRange.CharacterFormat.ForegroundColor = DefaultForeground();

            foreach (var token in PwshHighlighter.Tokenize(text))
            {
                var range = doc.GetRange(token.Start, token.Start + token.Length);
                range.CharacterFormat.ForegroundColor = PwshHighlighter.ColorFor(token.Kind, ActualTheme == ElementTheme.Dark);
            }

            doc.Selection.SetRange(selStart, selEnd);
        }
        finally
        {
            _suppressHighlight = false;
        }
    }

    private Color DefaultForeground()
    {
        // Use a near-white tone on dark themes, near-black on light themes.
        var isDark = ActualTheme == ElementTheme.Dark;
        return isDark
            ? Color.FromArgb(0xFF, 0xD4, 0xD4, 0xD4)
            : Color.FromArgb(0xFF, 0x1F, 0x1F, 0x1F);
    }
}
