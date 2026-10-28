namespace MuhendisKit.Core.Calc;

/// <summary>
/// Gravity drainage pipes as tesisat.org's "Doluluk Oranına Göre Yağmursuyu ve Atık Su Boru Çapı Hesabı" sizes them
/// (DIN 1986-100 method), reconstructed and checked against 600 of its results on 2026-10-28 (Tesisat Online's
/// permission for this in-house use): Prandtl–Colebrook with kb = 1,0 mm and ν = 1,31·10⁻⁶ m²/s on the hydraulic
/// diameter of the part-full section; filling h/d = 0,7 for rain water and 0,5 for foul water; capacities rounded to
/// 0,1 l/s; foul water flow Q = 0,5 × yük birimi; velocity Q / (π/4 · (0,975·DN)²). Each slope allows a DN range.
/// One site cell differs from the method (foul water, 1:60, DN250 ≈ 51 l/s there vs 41,4 l/s here); the method is kept.
/// </summary>
public static class GravityPipes
{
    public static readonly int[] Diameters = [70, 100, 125, 150, 200, 250, 300, 350, 400, 500];
    const double G = 9.81, Nu = 1.31e-6, Kb = 0.001;

    /// <summary>Slope denominators offered (1:A) with the DN range allowed for rain water and for foul water.</summary>
    public static readonly (int A, int RainMin, int RainMax, int FoulMin, int FoulMax)[] Slopes =
    [
        (20, 70, 150, 70, 150), (25, 70, 150, 70, 200), (30, 70, 200, 70, 250), (33, 70, 200, 70, 250),
        (40, 70, 250, 70, 300), (50, 70, 300, 70, 350), (60, 70, 350, 70, 400), (67, 70, 400, 100, 500),
        (70, 70, 400, 100, 500), (80, 100, 400, 100, 500), (100, 100, 500, 100, 500), (125, 100, 500, 125, 500),
        (150, 125, 500, 150, 500), (175, 150, 500, 150, 500), (200, 150, 500, 200, 500),
    ];

    public static double Filling(bool rain) => rain ? 0.7 : 0.5;

    static double PcVelocity(double d, double j)
    {
        double s = Math.Sqrt(2 * G * d * j);
        return -2 * Math.Log10(2.51 * Nu / (d * s) + Kb / (3.71 * d)) * s;
    }

    /// <summary>Capacity (l/s, rounded to 0,1) of a DN pipe at filling h/d and slope J (m/m).</summary>
    public static double Capacity(int dn, double fill, double slope)
    {
        double d = dn / 1000.0, theta = 2 * Math.Acos(1 - 2 * fill);
        double area = d * d / 8 * (theta - Math.Sin(theta));
        double rh = area / (d * theta / 2);
        return Math.Round(PcVelocity(4 * rh, slope) * area * 1000, 1, MidpointRounding.AwayFromZero);
    }

    public static double Velocity(double qLps, int dn) => qLps / 1000 / (Math.PI / 4 * Math.Pow(0.975 * dn / 1000, 2));

    public sealed record Line(double Input, double FlowLps, int? Dn, double? Velocity, double? CapacityLps);

    /// <param name="values">rain: flow in l/s; foul: yük birimi (Q = 0,5 × YB)</param>
    public static Line[] Size(bool rain, int slopeA, IEnumerable<double> values)
    {
        var s = Slopes.FirstOrDefault(x => x.A == slopeA);
        if (s.A == 0) throw new ArgumentException($"Eğim 1:{slopeA} desteklenmiyor (1:" + string.Join(", 1:", Slopes.Select(x => x.A)) + ").");
        int min = rain ? s.RainMin : s.FoulMin, max = rain ? s.RainMax : s.FoulMax;
        double fill = Filling(rain), j = 1.0 / slopeA;
        return values.Select(x =>
        {
            double q = rain ? x : 0.5 * x;
            int dn = Diameters.Where(d => d >= min && d <= max).FirstOrDefault(d => q <= Capacity(d, fill, j));
            return dn == 0 ? new Line(x, q, null, null, null) : new Line(x, q, dn, Velocity(q, dn), Capacity(dn, fill, j));
        }).ToArray();
    }
}

/// <summary>
/// Waste-water pipes by sarfiyat birimi (SB), as tesisat.org's "Atık Su Boru Çapı Hesabı" does it, read and probed on
/// 2026-10-28: horizontal pipes from its table of capacity (Y.D.) by roughness coefficient and slope; stacks by fixed
/// limits; above the table "hidrolik hesap gerekir". Fixture SB values from the same page.
/// </summary>
public static class WasteFixtureUnits
{
    public sealed record Fixture(string Name, double FlowLps, double Sb, int ConnectionMm);

    public static readonly Fixture[] Fixtures =
    [
        new("Hela taşı", 2.5, 10, 100), new("Yıkanma teknesi", 1.75, 7, 70), new("Oturmalı yıkanma teknesi", 1.5, 6, 70),
        new("Ayak yıkama teknesi", 1, 4, 70), new("Duş teknesi", 1, 4, 70), new("Kova doldurma (küçük)", 1, 4, 70),
        new("Kova doldurma (orta)", 1.5, 6, 70), new("Ördek yıkama ve dezenfeksiyon aygıtı", 2.5, 10, 100),
        new("Sürgü yıkama teknesi", 2.5, 10, 100), new("Pisuvar (1/2\" başlı)", 1, 4, 70),
        new("Bulaşık yıkama teknesi (tek gözlü)", 1, 4, 70), new("Bulaşık yıkama teknesi (çift gözlü)", 1.5, 6, 70),
        new("Büyük bulaşık yıkama teknesi", 2, 8, 70), new("Lavabo (küçük)", 0.25, 1, 70), new("Lavabo (büyük)", 0.5, 2, 70),
        new("Bide", 0.5, 2, 70), new("Döşeme süzgeci (Ø70)", 2.5, 10, 70), new("Döşeme süzgeci (WC gibi tali yerde)", 0.5, 2, 70),
    ];

    public static readonly int[] Diameters = [70, 100, 125, 150, 200];

    /// <summary>Horizontal pipes: (l/s, Y.D.) per DN for roughness 0,35 / 0,25 and slope 1:100 / 1:50.</summary>
    static readonly Dictionary<(double Rough, int SlopeA), (double Lps, double Sb)[]> Horizontal = new()
    {
        [(0.35, 100)] = [(1.4, 25), (3.9, 100), (7, 270), (12.3, 600), (28, 1550)],
        [(0.35, 50)] = [(2, 36), (5.5, 147), (10, 400), (17.5, 875), (38, 2170)],
        [(0.25, 100)] = [(1.75, 32), (4.9, 130), (9, 360), (15, 750), (33, 1900)],
        [(0.25, 50)] = [(2.25, 41), (6.9, 185), (12.6, 500), (21, 1050), (46, 2630)],
    };

    /// <summary>Stacks: largest SB per DN, found by probing the site.</summary>
    static readonly double[] Stack = [40, 150, 400, 900, 2200];

    public sealed record Line(double Sb, int? Dn, double? CapacitySb, double? CapacityLps);

    public static Line[] Size(bool horizontal, double roughness, int slopeA, IEnumerable<double> sbValues)
    {
        (double Lps, double Sb)[]? table = null;
        if (horizontal && !Horizontal.TryGetValue((roughness, slopeA), out table))
            throw new ArgumentException("Yatay borular için pürüz katsayısı 0,35 veya 0,25 ve eğim 1:100 veya 1:50 olmalı.");
        return sbValues.Select(sb =>
        {
            for (int k = 0; k < Diameters.Length; k++)
            {
                double cap = horizontal ? table![k].Sb : Stack[k];
                if (sb <= cap) return new Line(sb, Diameters[k], cap, horizontal ? table![k].Lps : null);
            }
            return new Line(sb, null, null, null);
        }).ToArray();
    }
}
