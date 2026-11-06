namespace AutoPortal.Guardian.Analysis.Cooling;

public sealed record FanPoint(double TempC, double Percent);

/// <summary>
/// A user-configurable temperature-to-fan-speed curve. The default is an EXAMPLE, not a universal recommendation.
/// Safety: speeds are clamped to a minimum floor (fans are never turned off) and the curve must reach 100% by its last point.
/// </summary>
public sealed class FanCurve
{
    public const double MinimumPercent = 20;
    public IReadOnlyList<FanPoint> Points { get; }

    public FanCurve(IEnumerable<FanPoint> points)
    {
        var p = points.OrderBy(x => x.TempC).ToList();
        if (p.Count < 2) throw new ArgumentException("A fan curve needs at least two points.");
        if (p.Any(x => x.Percent < MinimumPercent || x.Percent > 100)) throw new ArgumentException($"Fan speeds must be between {MinimumPercent}% and 100%.");
        for (int i = 1; i < p.Count; i++)
            if (p[i].Percent < p[i - 1].Percent) throw new ArgumentException("A fan curve must not decrease as temperature rises.");
        if (p[^1].Percent < 100) throw new ArgumentException("The final point must be 100% so cooling is never capped under extreme heat.");
        Points = p;
    }

    public static FanCurve Example => new(new[]
    {
        new FanPoint(40, 30), new FanPoint(50, 45), new FanPoint(60, 60),
        new FanPoint(70, 75), new FanPoint(80, 90), new FanPoint(85, 100)
    });

    public double Evaluate(double tempC)
    {
        if (tempC <= Points[0].TempC) return Points[0].Percent;
        if (tempC >= Points[^1].TempC) return Points[^1].Percent;
        for (int i = 1; i < Points.Count; i++)
            if (tempC <= Points[i].TempC)
            {
                var a = Points[i - 1]; var b = Points[i];
                return a.Percent + (b.Percent - a.Percent) * (tempC - a.TempC) / (b.TempC - a.TempC);
            }
        return 100;
    }
}
