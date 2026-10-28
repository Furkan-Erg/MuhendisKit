namespace MuhendisKit.Core.Calc;

/// <summary>Moist air, SI units, equations from ASHRAE Handbook — Fundamentals (2017) ch. 1 (as in PsychroLib).</summary>
public static class Psychro
{
    /// <summary>Standard atmospheric pressure at altitude <paramref name="z"/> m, Pa.</summary>
    public static double PressureAtAltitude(double z) => 101325 * Math.Pow(1 - 2.25577e-5 * z, 5.2559);

    /// <summary>Saturation vapour pressure (Hyland–Wexler), Pa. Over ice below the triple point.</summary>
    public static double SaturationPressure(double tC)
    {
        double t = tC + 273.15;
        double ln = tC < 0.01
            ? -5.6745359e3 / t + 6.3925247 - 9.6778430e-3 * t + 6.2215701e-7 * t * t
              + 2.0747825e-9 * t * t * t - 9.4840240e-13 * t * t * t * t + 4.1635019 * Math.Log(t)
            : -5.8002206e3 / t + 1.3914993 - 4.8640239e-2 * t + 4.1764768e-5 * t * t
              - 1.4452093e-8 * t * t * t + 6.5459673 * Math.Log(t);
        return Math.Exp(ln);
    }

    public static double HumidityRatio(double pw, double p) => 0.621945 * pw / (p - pw);

    public static double VapourPressure(double w, double p) => p * w / (0.621945 + w);

    /// <summary>kJ/kg dry air.</summary>
    public static double Enthalpy(double tC, double w) => 1.006 * tC + w * (2501 + 1.86 * tC);

    /// <summary>m³/kg dry air.</summary>
    public static double SpecificVolume(double tC, double w, double p) => 287.042 * (tC + 273.15) * (1 + 1.607858 * w) / p;

    public static double DewPoint(double pw) => Solve(t => SaturationPressure(t) - pw, -100, 200);

    /// <summary>Humidity ratio from dry-bulb and wet-bulb temperatures (ASHRAE eq. 33/35).</summary>
    public static double HumidityRatioFromWetBulb(double t, double twb, double p)
    {
        double ws = HumidityRatio(SaturationPressure(twb), p);
        return twb >= 0
            ? ((2501 - 2.326 * twb) * ws - 1.006 * (t - twb)) / (2501 + 1.86 * t - 4.186 * twb)
            : ((2830 - 0.24 * twb) * ws - 1.006 * (t - twb)) / (2830 + 1.86 * t - 2.1 * twb);
    }

    public static double WetBulb(double t, double w, double p) =>
        Solve(twb => HumidityRatioFromWetBulb(t, twb, p) - w, -100, t);

    /// <summary>All properties from dry-bulb temperature and relative humidity (0–1).</summary>
    public static PsychroState FromRh(double t, double rh, double p)
    {
        double pw = rh * SaturationPressure(t);
        return Build(t, HumidityRatio(pw, p), p);
    }

    public static PsychroState FromWetBulb(double t, double twb, double p) => Build(t, HumidityRatioFromWetBulb(t, twb, p), p);

    public static PsychroState FromDewPoint(double t, double tdp, double p) => Build(t, HumidityRatio(SaturationPressure(tdp), p), p);

    static PsychroState Build(double t, double w, double p)
    {
        double pw = VapourPressure(w, p);
        double v = SpecificVolume(t, w, p);
        return new PsychroState(t, pw / SaturationPressure(t), w, Enthalpy(t, w), DewPoint(pw), WetBulb(t, w, p), v, (1 + w) / v, pw, p);
    }

    // Bisection: every function here is monotonic on the bracket.
    static double Solve(Func<double, double> f, double lo, double hi)
    {
        double flo = f(lo);
        for (int i = 0; i < 200 && hi - lo > 1e-9; i++)
        {
            double mid = (lo + hi) / 2, fm = f(mid);
            if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
        }
        return (lo + hi) / 2;
    }
}

/// <param name="Rh">0–1</param>
/// <param name="W">kg/kg dry air</param>
/// <param name="Enthalpy">kJ/kg dry air</param>
/// <param name="Volume">m³/kg dry air</param>
/// <param name="Density">kg/m³ moist air</param>
public sealed record PsychroState(double DryBulb, double Rh, double W, double Enthalpy, double DewPoint, double WetBulb,
    double Volume, double Density, double VapourPressure, double Pressure);
