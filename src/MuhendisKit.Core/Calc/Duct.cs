namespace MuhendisKit.Core.Calc;

public sealed record DuctInput(
    double FlowM3h,
    double TargetPressureDrop,   // Pa/m (equal-friction method)
    double RoughnessMm = 0.09,   // galvanised sheet
    double AspectRatio = 2,      // rectangular b/a for the suggested equivalent
    double AirDensity = 1.2,
    double AirKinematicViscosity = 1.5e-5);

public sealed record DuctResult(
    double RoundDiameterMm,       // exact diameter for the target friction
    double StandardDiameterMm,    // next standard round size up
    double Velocity,              // in the standard size
    double PressureDropPerM,      // in the standard size
    double RectWidthMm,           // equivalent rectangle (Huebscher), rounded up to 50 mm
    double RectHeightMm,
    double RectEquivalentMm,
    double RectVelocity);

public static class Duct
{
    public static readonly int[] StandardRound = [80, 100, 125, 140, 160, 180, 200, 224, 250, 280, 315, 355, 400, 450, 500, 560, 630, 710, 800, 900, 1000, 1120, 1250];

    /// <summary>Huebscher equivalent diameter of an a×b rectangle (same flow and friction).</summary>
    public static double EquivalentDiameter(double a, double b) => 1.30 * Math.Pow(a * b, 0.625) / Math.Pow(a + b, 0.25);

    public static DuctResult Size(DuctInput i)
    {
        double q = i.FlowM3h / 3600;
        double eps = i.RoughnessMm / 1000;
        double Drop(double d) => Hydraulics.Pipe(d, q, eps, i.AirKinematicViscosity, i.AirDensity).PressureDropPerM;

        // Pressure drop falls monotonically with diameter: bisect on log scale.
        double lo = 0.01, hi = 5;
        for (int k = 0; k < 100; k++)
        {
            double mid = Math.Sqrt(lo * hi);
            if (Drop(mid) > i.TargetPressureDrop) lo = mid; else hi = mid;
        }
        double exactMm = hi * 1000;
        double std = StandardRound.Select(s => (double)s).FirstOrDefault(s => s >= exactMm - 0.5, Math.Ceiling(exactMm / 50) * 50);
        var round = Hydraulics.Pipe(std / 1000, q, eps, i.AirKinematicViscosity, i.AirDensity);

        // Smallest 50 mm-step rectangle with ratio ≈ AspectRatio whose equivalent diameter reaches the exact one.
        double a = 50, b;
        while (true)
        {
            b = Math.Ceiling(a * i.AspectRatio / 50) * 50;
            if (EquivalentDiameter(a, b) >= exactMm || a > 3000) break;
            a += 50;
        }
        double de = EquivalentDiameter(a, b);
        return new DuctResult(exactMm, std, round.Velocity, round.PressureDropPerM, b, a, de, q / (a * b / 1e6));
    }
}
