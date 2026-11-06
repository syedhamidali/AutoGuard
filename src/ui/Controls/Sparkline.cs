using System.Windows;
using System.Windows.Media;

namespace AutoPortal.Guardian.App.Controls;

/// <summary>Minimal line chart. Gaps (null values) are drawn as breaks, never interpolated, so missing sensors are never faked.</summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(IReadOnlyList<double?>), typeof(Sparkline), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(Sparkline), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FixedMaxProperty =
        DependencyProperty.Register(nameof(FixedMax), typeof(double), typeof(Sparkline), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double?>? Series { get => (IReadOnlyList<double?>?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    /// <summary>When &gt; 0 the Y axis spans 0..FixedMax (e.g. 100 for percentages).</summary>
    public double FixedMax { get => (double)GetValue(FixedMaxProperty); set => SetValue(FixedMaxProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth; var h = ActualHeight;
        var bg = new SolidColorBrush(Color.FromRgb(0x1b, 0x1f, 0x27));
        dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, w, h), 6, 6);
        var data = Series;
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(0x2a, 0x30, 0x3b)), 1);
        for (int i = 1; i < 4; i++) dc.DrawLine(grid, new Point(0, h * i / 4), new Point(w, h * i / 4));
        var tf = new Typeface("Segoe UI");
        FormattedText T(string s, Brush b) => new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, b, 1.0);
        var dim = new SolidColorBrush(Color.FromRgb(0x8b, 0x94, 0xa3));
        var vals = data?.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (data is null || vals is null || vals.Count < 2)
        {
            dc.DrawText(T("Not available", dim), new Point(10, h / 2 - 8));
            return;
        }
        double max = FixedMax > 0 ? FixedMax : Math.Max(vals.Max() * 1.1, vals.Min() + 10);
        double min = FixedMax > 0 ? 0 : Math.Max(0, Math.Min(vals.Min() * 0.9, max - 10));
        var pen = new Pen(Stroke, 1.8) { LineJoin = PenLineJoin.Round };
        double dx = w / Math.Max(1, data.Count - 1);
        StreamGeometry? geo = null; StreamGeometryContext? ctx = null; bool open = false;
        void Flush() { if (ctx != null) { ctx.Close(); geo!.Freeze(); dc.DrawGeometry(null, pen, geo); ctx = null; open = false; } }
        for (int i = 0; i < data.Count; i++)
        {
            if (data[i] is not double v) { Flush(); continue; }
            var p = new Point(i * dx, h - 4 - (v - min) / (max - min) * (h - 8));
            if (!open) { geo = new StreamGeometry(); ctx = geo.Open(); ctx.BeginFigure(p, false, false); open = true; }
            else ctx!.LineTo(p, true, false);
        }
        Flush();
        dc.DrawText(T($"{max:0}{Unit}", dim), new Point(6, 2));
        dc.DrawText(T($"{min:0}{Unit}", dim), new Point(6, h - 16));
        var last = data.LastOrDefault(v => v.HasValue);
        if (last.HasValue) { var ft = T($"{last.Value:0.#}{Unit}", Brushes.White); dc.DrawText(ft, new Point(w - ft.Width - 8, 2)); }
    }
}
