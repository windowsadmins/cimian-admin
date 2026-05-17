namespace CimianStudio.Views;

using CimianStudio.Core.Models.Git;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

public sealed partial class GraphCanvas : UserControl
{
    private const double ColW = 12.0;   // pixels per lane column
    private const double RowH = 28.0;   // must match ListView item height
    private const double DotR = 3.5;    // commit dot radius

    // 8-color palette shared across all rows.
    private static readonly Color[] Palette =
    [
        Color.FromArgb(0xFF, 0x4E, 0xC9, 0x70),  // green
        Color.FromArgb(0xFF, 0x77, 0x9D, 0xFF),  // blue
        Color.FromArgb(0xFF, 0xFF, 0xB8, 0x40),  // orange
        Color.FromArgb(0xFF, 0xE7, 0x6F, 0x6F),  // red
        Color.FromArgb(0xFF, 0xC0, 0x82, 0xFF),  // purple
        Color.FromArgb(0xFF, 0x4D, 0xD0, 0xE1),  // cyan
        Color.FromArgb(0xFF, 0xFF, 0xD7, 0x00),  // yellow
        Color.FromArgb(0xFF, 0xFF, 0x79, 0xC6),  // pink
    ];

    public static readonly DependencyProperty LaneRowProperty =
        DependencyProperty.Register(
            nameof(LaneRow),
            typeof(LaneGraphRow),
            typeof(GraphCanvas),
            new PropertyMetadata(null, static (d, _) => ((GraphCanvas)d).Rebuild()));

    public LaneGraphRow? LaneRow
    {
        get => (LaneGraphRow?)GetValue(LaneRowProperty);
        set => SetValue(LaneRowProperty, value);
    }

    public GraphCanvas()
    {
        InitializeComponent();
    }

    private void Rebuild()
    {
        DrawingCanvas.Children.Clear();
        var row = LaneRow;
        if (row is null) return;

        var cols = Math.Max(1, row.TotalColumns);
        DrawingCanvas.Width = cols * ColW;
        DrawingCanvas.Height = RowH;

        // Lines first so the dot renders on top.
        foreach (var lane in row.Lines)
        {
            var fromX = lane.FromColumn * ColW + ColW / 2.0;
            var toX   = lane.ToColumn   * ColW + ColW / 2.0;
            var fromY = lane.IsTopHalf ? 0.0 : RowH / 2.0;
            var toY   = lane.IsTopHalf ? RowH / 2.0 : RowH;

            var line = new Line
            {
                X1 = fromX,
                Y1 = fromY,
                X2 = toX,
                Y2 = toY,
                StrokeThickness = 1.5,
                Stroke = new SolidColorBrush(Palette[lane.ColorIndex % Palette.Length]),
            };
            DrawingCanvas.Children.Add(line);
        }

        // Commit dot.
        var dotX = row.CommitColumn * ColW + ColW / 2.0;
        var dotY = RowH / 2.0;
        var dotColor = Palette[row.ColorIndex % Palette.Length];

        var dot = new Ellipse
        {
            Width  = DotR * 2,
            Height = DotR * 2,
            Fill   = new SolidColorBrush(dotColor),
        };
        Canvas.SetLeft(dot, dotX - DotR);
        Canvas.SetTop(dot, dotY - DotR);
        DrawingCanvas.Children.Add(dot);
    }
}
