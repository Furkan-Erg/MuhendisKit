namespace MuhendisKit.Core.Calc;

/// <summary>Liquid water at atmospheric pressure, 0–100 °C (IAPWS reference values, linear interpolation).</summary>
public static class Water
{
    static readonly double[] T = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];
    static readonly double[] Rho = [999.84, 999.70, 998.21, 995.65, 992.22, 988.03, 983.20, 977.76, 971.79, 965.31, 958.35];
    static readonly double[] Mu = [1.792e-3, 1.306e-3, 1.002e-3, 0.7972e-3, 0.6527e-3, 0.5465e-3, 0.4660e-3, 0.4035e-3, 0.3540e-3, 0.3142e-3, 0.2816e-3];

    /// <summary>Density, kg/m³.</summary>
    public static double Density(double tC) => Interp(Rho, tC);

    /// <summary>Dynamic viscosity, Pa·s.</summary>
    public static double Viscosity(double tC) => Interp(Mu, tC);

    /// <summary>Kinematic viscosity, m²/s.</summary>
    public static double KinematicViscosity(double tC) => Viscosity(tC) / Density(tC);

    /// <summary>Specific heat, J/(kg·K). Varies by &lt;1% over 0–100 °C.</summary>
    public const double Cp = 4186;

    /// <summary>Relative volume increase from <paramref name="fromC"/> to <paramref name="toC"/> (EN 12828 "e").</summary>
    public static double Expansion(double toC, double fromC = 10) => Density(fromC) / Density(toC) - 1;

    static double Interp(double[] ys, double t)
    {
        if (t < 0 || t > 100)
            throw new ArgumentOutOfRangeException(nameof(t), "Su sıcaklığı 0–100 °C arasında olmalı.");
        int i = Math.Min((int)(t / 10), 9);
        double f = (t - T[i]) / 10;
        return ys[i] + (ys[i + 1] - ys[i]) * f;
    }
}
