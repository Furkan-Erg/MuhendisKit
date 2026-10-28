namespace MuhendisKit.Core.Insulation;

/// <param name="ThicknessM">m</param>
/// <param name="Lambda">W/(m·K); ignored when <paramref name="FixedR"/> is set (air layers)</param>
/// <param name="Mu">water vapour diffusion resistance factor μ</param>
/// <param name="FixedR">m²K/W, for layers given by resistance instead of conductivity</param>
public sealed record Layer(string Name, double ThicknessM, double Lambda, double Mu, double? FixedR = null)
{
    public double R => FixedR ?? (Lambda > 0 ? ThicknessM / Lambda : 0);
    public double Sd => Mu * ThicknessM;
}

/// <param name="Rsi">inside surface resistance, m²K/W (TS EN ISO 6946: wall 0.13, heat flow up 0.10, down 0.17)</param>
/// <param name="Rse">outside surface resistance (0.04; 0 for ground contact)</param>
/// <param name="FixedU">for windows/doors: U given by the manufacturer, layers ignored</param>
public sealed record Element(string Name, string Kind, double Rsi, double Rse, Layer[] Layers, double? FixedU = null)
{
    public double RTotal => Rsi + Layers.Sum(l => l.R) + Rse;
    public double U => FixedU ?? 1 / RTotal;
}

public sealed record Climate(double InsideTemp, double InsideRh, double OutsideTemp, double OutsideRh);

/// <param name="X">position from the inside surface, m</param>
/// <param name="Sd">cumulative equivalent air layer thickness, m</param>
public sealed record GlaserPoint(double X, double Sd, double Temp, double SaturationPressure, double VapourPressure);

/// <param name="Rate">condensation rate, g/(m²·h)</param>
public sealed record CondensationPlane(int Interface, double X, double Rate);

public sealed record GlaserResult(
    GlaserPoint[] Points,
    CondensationPlane[] Condensation,
    double InsideSurfaceTemp,
    double InsideDewPoint,
    bool SurfaceCondensation);

public static class Envelope
{
    /// <summary>Water vapour permeability of still air, kg/(m·s·Pa) (TS EN ISO 13788).</summary>
    public const double Delta0 = 2e-10;

    /// <summary>Saturation pressure as used in TS EN ISO 13788, Pa.</summary>
    public static double SaturationPressure(double t) => t >= 0
        ? 610.5 * Math.Exp(17.269 * t / (237.3 + t))
        : 610.5 * Math.Exp(21.875 * t / (265.5 + t));

    /// <summary>Inverse of <see cref="SaturationPressure"/>.</summary>
    public static double DewPoint(double p)
    {
        double ln = Math.Log(p / 610.5);
        return p >= 610.5 ? 237.3 * ln / (17.269 - ln) : 265.5 * ln / (21.875 - ln);
    }

    /// <summary>Thickness (m) the layer at <paramref name="layerIndex"/> needs so the element reaches <paramref name="targetU"/>.</summary>
    public static double RequiredThickness(Element e, int layerIndex, double targetU)
    {
        var layer = e.Layers[layerIndex];
        double others = e.RTotal - layer.R;
        return Math.Max(0, layer.Lambda * (1 / targetU - others));
    }

    public static GlaserResult Glaser(Element e, Climate c)
    {
        if (e.FixedU is not null || e.Layers.Length == 0)
            throw new ArgumentException("Yoğuşma analizi katmanlı elemanlar için yapılır.");

        double dt = c.InsideTemp - c.OutsideTemp;
        double q = dt / e.RTotal;   // W/m²
        var pts = new List<GlaserPoint>();
        double x = 0, sd = 0, temp = c.InsideTemp - q * e.Rsi;
        double pi = c.InsideRh * SaturationPressure(c.InsideTemp);
        double pe = c.OutsideRh * SaturationPressure(c.OutsideTemp);
        pts.Add(new(x, sd, temp, SaturationPressure(temp), 0));
        foreach (var l in e.Layers)
        {
            x += l.ThicknessM;
            sd += l.Sd;
            temp -= q * l.R;
            pts.Add(new(x, sd, temp, SaturationPressure(temp), 0));
        }

        // Real vapour pressure = taut string from (0, pi) to (sdTotal, pe) that never exceeds saturation:
        // the lower convex hull of the start, the interface saturation points and the end.
        var cand = new List<(double sd, double p, int idx)> { (0, pi, 0) };
        for (int k = 1; k < pts.Count - 1; k++) cand.Add((pts[k].Sd, pts[k].SaturationPressure, k));
        cand.Add((sd, pe, pts.Count - 1));
        var hull = new List<(double sd, double p, int idx)>();
        foreach (var pt in cand.OrderBy(p => p.sd).ThenBy(p => p.p))
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], pt) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(pt);
        }
        // A hull vertex at the surfaces with zero sd step would divide by zero; guard slopes.
        double Slope((double sd, double p, int) a, (double sd, double p, int) b) => b.sd - a.sd > 1e-12 ? (b.p - a.p) / (b.sd - a.sd) : 0;

        for (int k = 0; k < pts.Count; k++)
        {
            double s = pts[k].Sd;
            int h = hull.FindLastIndex(v => v.sd <= s + 1e-12);
            double p = h >= hull.Count - 1 ? hull[^1].p : hull[h].p + Slope(hull[h], hull[h + 1]) * (s - hull[h].sd);
            pts[k] = pts[k] with { VapourPressure = p };
        }

        var planes = new List<CondensationPlane>();
        for (int h = 1; h < hull.Count - 1; h++)
        {
            // Flux in minus flux out at the plane, g = δ0·(Δp/Δsd)
            double gIn = -Slope(hull[h - 1], hull[h]) * Delta0;
            double gOut = -Slope(hull[h], hull[h + 1]) * Delta0;
            double rate = (gIn - gOut) * 3600 * 1000;
            if (rate > 1e-9) planes.Add(new(hull[h].idx, pts[hull[h].idx].X, rate));
        }

        double tsi = pts[0].Temp;
        double dew = DewPoint(pi);
        return new GlaserResult(pts.ToArray(), planes.ToArray(), tsi, dew, tsi < dew);
    }

    static double Cross((double x, double y, int) o, (double x, double y, int) a, (double x, double y, int) b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
}
