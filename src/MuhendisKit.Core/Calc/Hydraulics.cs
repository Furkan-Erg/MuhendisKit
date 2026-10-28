namespace MuhendisKit.Core.Calc;

public static class Hydraulics
{
    /// <summary>Darcy friction factor. Laminar: 64/Re; turbulent: Colebrook–White solved by fixed-point iteration.</summary>
    public static double FrictionFactor(double re, double relRoughness)
    {
        if (re <= 0) return 0;
        if (re < 2300) return 64 / re;
        // Swamee–Jain as the starting point, then Colebrook converges in a few steps.
        double f = 0.25 / Math.Pow(Math.Log10(relRoughness / 3.7 + 5.74 / Math.Pow(re, 0.9)), 2);
        for (int i = 0; i < 50; i++)
        {
            double next = Math.Pow(-2 * Math.Log10(relRoughness / 3.7 + 2.51 / (re * Math.Sqrt(f))), -2);
            if (Math.Abs(next - f) < 1e-12) return next;
            f = next;
        }
        return f;
    }

    /// <summary>Flow in a full round pipe. Units: d m, q m³/s, ε m, ν m²/s, ρ kg/m³.</summary>
    public static FlowResult Pipe(double d, double q, double roughness, double nu, double rho)
    {
        double area = Math.PI * d * d / 4;
        double v = q / area;
        double re = v * d / nu;
        double f = FrictionFactor(re, roughness / d);
        double r = f / d * rho * v * v / 2;   // Pa/m
        return new FlowResult(v, re, f, r);
    }
}

public readonly record struct FlowResult(double Velocity, double Reynolds, double Friction, double PressureDropPerM);

public sealed record PipeSize(string Name, double InnerDiameterMm);

public sealed record PipeMaterial(string Id, string Name, double RoughnessMm, PipeSize[] Sizes);

public static class PipeCatalog
{
    // Inner diameters from nominal OD and wall: EN 10255 medium series, EN 1057 copper, PP-R PN20 (SDR 6).
    public static readonly PipeMaterial[] All =
    [
        new("celik", "Çelik boru (EN 10255 orta seri)", 0.045,
        [
            new("DN15 (½\")", 16.1), new("DN20 (¾\")", 21.7), new("DN25 (1\")", 27.3), new("DN32 (1¼\")", 36.0),
            new("DN40 (1½\")", 41.9), new("DN50 (2\")", 53.1), new("DN65 (2½\")", 68.9), new("DN80 (3\")", 80.9),
            new("DN100 (4\")", 105.3), new("DN125 (5\")", 129.7), new("DN150 (6\")", 155.1),
        ]),
        new("bakir", "Bakır boru (EN 1057)", 0.0015,
        [
            new("15×1", 13.0), new("18×1", 16.0), new("22×1", 20.0), new("28×1", 26.0),
            new("35×1,2", 32.6), new("42×1,2", 39.6), new("54×1,5", 51.0),
        ]),
        new("ppr", "PP-R boru PN20 (SDR 6)", 0.007,
        [
            new("Ø20", 13.2), new("Ø25", 16.6), new("Ø32", 21.2), new("Ø40", 26.6), new("Ø50", 33.4),
            new("Ø63", 42.0), new("Ø75", 50.0), new("Ø90", 60.0), new("Ø110", 73.4),
        ]),
    ];

    public static PipeMaterial Find(string id) =>
        All.FirstOrDefault(m => m.Id == id) ?? throw new ArgumentException($"Bilinmeyen boru malzemesi: {id}");
}

public sealed record PipeSizingInput(
    string Material,
    double FlowM3h,           // volumetric flow; or derive from heat load in the UI
    double TempC,
    double MaxVelocity,       // m/s
    double MaxPressureDrop);  // Pa/m

public sealed record PipeSizingRow(string Name, double InnerDiameterMm, double Velocity, double Reynolds, double Friction, double PressureDropPerM, bool Fits);

public sealed record PipeSizingResult(PipeSizingRow[] Rows, string? Suggested);

public static class PipeSizing
{
    /// <summary>Flow (m³/h) carrying <paramref name="kw"/> with a supply/return difference of <paramref name="dt"/> K.</summary>
    public static double FlowFromHeat(double kw, double dt, double tempC) =>
        kw * 1000 / (Water.Density(tempC) * Water.Cp * dt) * 3600;

    public static PipeSizingResult Size(PipeSizingInput i)
    {
        var mat = PipeCatalog.Find(i.Material);
        double q = i.FlowM3h / 3600;
        double rho = Water.Density(i.TempC), nu = Water.KinematicViscosity(i.TempC);
        var rows = mat.Sizes.Select(s =>
        {
            var r = Hydraulics.Pipe(s.InnerDiameterMm / 1000, q, mat.RoughnessMm / 1000, nu, rho);
            bool fits = r.Velocity <= i.MaxVelocity && r.PressureDropPerM <= i.MaxPressureDrop;
            return new PipeSizingRow(s.Name, s.InnerDiameterMm, r.Velocity, r.Reynolds, r.Friction, r.PressureDropPerM, fits);
        }).ToArray();
        return new PipeSizingResult(rows, rows.FirstOrDefault(r => r.Fits)?.Name);
    }
}
