using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PcOptimizer.Presentation.Charts;

namespace PcOptimizer.App.Controls;

/// <summary>
/// Grafico a linee leggero (un punto per secondo di misura): griglia, asse Y con il massimo, una linea per serie
/// e legenda. Le serie arrivano già pronte da <see cref="ChartData"/>.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IReadOnlyList<ChartSeries>), typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(LineChart),
        new FrameworkPropertyMetadata("Nessun dato da mostrare", FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LeftMargin = 48;
    private const double BottomMargin = 18;
    private const double LegendHeight = 22;

    public IReadOnlyList<ChartSeries>? Series
    {
        get => (IReadOnlyList<ChartSeries>?)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var muted = Brush("TextMuted", Brushes.Gray);
        var grid = new Pen(Brush("Border", Brushes.DimGray), 1);
        var series = Series?.Where(s => s.Values.Count > 0).ToList() ?? [];
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (series.Count == 0)
        {
            dc.DrawText(Text(EmptyText, muted, 12), new Point(8, ActualHeight / 2 - 8));
            return;
        }

        var plot = new Rect(LeftMargin, LegendHeight, Math.Max(0, ActualWidth - LeftMargin - 8), Math.Max(0, ActualHeight - LegendHeight - BottomMargin));
        var max = series.SelectMany(s => s.Values).Where(double.IsFinite).DefaultIfEmpty(0).Max();
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = max * 1.1 * (4 - i) / 4;
            dc.DrawText(Text(value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture).Replace('.', ','), muted, 10), new Point(4, y - 7));
        }

        var x = 4.0;
        foreach (var s in series)
        {
            var brush = Brush("Chart." + s.ColorKey, Brushes.DeepSkyBlue);
            var points = ChartData.Scale(s.Values, plot.Width, plot.Height, max);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(plot.Left + points[0].X, plot.Top + points[0].Y), false, false);
                ctx.PolyLineTo(points.Skip(1).Select(p => new Point(plot.Left + p.X, plot.Top + p.Y)).ToList(), true, true);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(brush, 1.6), geometry);

            dc.DrawRectangle(brush, null, new Rect(x, 6, 10, 10));
            var label = Text(s.Name, muted, 11);
            dc.DrawText(label, new Point(x + 14, 3));
            x += label.Width + 30;
        }

        dc.DrawText(Text("secondi →", muted, 10), new Point(plot.Right - 50, plot.Bottom + 3));
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private FormattedText Text(string text, Brush brush, double size) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
