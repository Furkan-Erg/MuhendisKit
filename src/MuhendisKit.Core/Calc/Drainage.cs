namespace MuhendisKit.Core.Calc;

/// <summary>
/// Values from the Yağmursuyu Toplama, Depolama ve Deşarj Sistemleri Hakkında Yönetmelik (RG 23.06.2017, 30105),
/// EK-1, read from the Resmî Gazete scan on 2026-10-28: Çizelge 1.3 (C), Çizelge 1.4 (concrete slopes),
/// Çizelge 1.5 (Manning n), denklem 1.2 (r = 166,7 i) and 1.8 (Q = C r A), 1.3.3.1 (0,5–5 m/s), 1.3.3.3 (≤ %90 dolu).
/// </summary>
public static class RainRegulation
{
    public sealed record AreaType(string Id, string Name, double CMin, double CMax);

    /// <summary>Çizelge 1.3: Çeşitli alanlarda C katsayısı.</summary>
    public static readonly AreaType[] AreaTypes =
    [
        new("sehir-merkezi", "Ticari: şehir merkezleri", 0.70, 0.95),
        new("tali-merkez", "Ticari: tali merkezler", 0.50, 0.70),
        new("tek-katli", "İkamet: tek katlı konut alanları", 0.30, 0.50),
        new("ayrik", "İkamet: çok katlı ayrık nizam konut", 0.40, 0.60),
        new("bitisik", "İkamet: çok katlı bitişik nizam konut", 0.60, 0.75),
        new("mucavir", "Mücavir alanlar", 0.25, 0.40),
        new("apartman", "Çok katlı apartman alanları", 0.50, 0.70),
        new("endustri", "Endüstriyel alanlar", 0.50, 0.80),
        new("hafif-sanayi", "Hafif sanayi alanları", 0.50, 0.80),
        new("agir-sanayi", "Ağır sanayi alanları", 0.60, 0.90),
        new("park", "Parklar", 0.20, 0.35),
        new("oyun", "Oyun alanları", 0.20, 0.40),
        new("gelismemis", "Gelişmemiş alanlar", 0.10, 0.30),
    ];

    public sealed record PipeMaterial(string Id, string Name, double Manning);

    /// <summary>Çizelge 1.5: Manning katsayıları (beton 0,3–3 mm pürüzlülük için tek n verilmiş).</summary>
    public static readonly PipeMaterial[] Materials =
    [
        new("asbest", "Asbestli çimento", 0.011),
        new("beton", "Beton", 0.013),
        new("font", "Font (dökme demir)", 0.012),
        new("ctp", "CTP", 0.009),
        new("celik", "Çelik", 0.012),
    ];

    public const double MinVelocity = 0.5, MaxVelocity = 5.0, MaxFilling = 0.90;

    /// <summary>Çizelge 1.4: beton kanallarda çapa göre en küçük ve en büyük eğim, 1:A biçiminde (A değerleri).</summary>
    public static readonly (int DiameterMm, int MinA, int? ExceptionalMinA, int MaxA)[] ConcreteSlopes =
    [
        (200, 300, null, 7), (300, 500, null, 7), (400, 600, 900, 25), (500, 800, 1000, 25), (600, 1000, 1500, 25),
        (700, 1000, 1500, 50), (800, 1200, 1800, 50), (900, 1500, 1800, 50), (1000, 2000, 2500, 75), (1200, 2050, 2500, 75),
        (1400, 2100, 2500, 75), (1600, 2150, 2500, 75), (2000, 2250, 2500, 75), (3000, 2500, 2500, 75),
    ];

    /// <summary>Denklem 1.2: yağmur verimi r (l/s·ha) = 166,7 × yağış şiddeti i (mm/dak).</summary>
    public static double YieldFromIntensity(double mmPerMin) => 166.7 * mmPerMin;
}

/// <param name="AreaM2">drenaj alanı</param>
/// <param name="C">yüzeysel akış katsayısı (Çizelge 1.3)</param>
/// <param name="IntensityMmMin">yağış şiddeti i (mm/dak), from the local süre-şiddet-frekans data (MGM)</param>
/// <param name="SlopePercent">boru/kanal eğimi (%)</param>
public sealed record RainDrainageInput(double AreaM2, double C, double IntensityMmMin, string Material, double SlopePercent,
    double MaxFilling = RainRegulation.MaxFilling, double? ManningN = null);

public sealed record RainPipeRow(int DiameterMm, double CapacityLps, double? Filling, double? Velocity, bool Fits, string? Warning);

public sealed record RainDrainageResult(double YieldLpsHa, double FlowLps, double ManningN, RainPipeRow[] Rows, int? Suggested);

public static class RainDrainage
{
    /// <summary>Internal diameters evaluated (mm); the regulation's concrete table starts at 200.</summary>
    public static readonly int[] Diameters = [100, 125, 150, 200, 250, 300, 400, 500, 600, 700, 800, 900, 1000, 1200, 1400, 1600, 2000];

    /// <summary>Manning for a part-full circular pipe at filling ratio y = h/D: Q = (1/n)·A·R^(2/3)·S^(1/2). Returns (Q m³/s, A m²).</summary>
    public static (double Q, double Area) PartFull(double dM, double y, double n, double slope)
    {
        double theta = 2 * Math.Acos(1 - 2 * y);
        double area = dM * dM / 8 * (theta - Math.Sin(theta));
        double wetted = dM * theta / 2;
        double r = area / wetted;
        return (area * Math.Pow(r, 2.0 / 3) * Math.Sqrt(slope) / n, area);
    }

    /// <summary>Filling ratio carrying <paramref name="q"/> (bisection; Q(y) rises up to y ≈ 0.94).</summary>
    public static double FillingFor(double q, double dM, double n, double slope)
    {
        double lo = 1e-6, hi = 0.938;
        for (int k = 0; k < 80; k++)
        {
            double mid = (lo + hi) / 2;
            if (PartFull(dM, mid, n, slope).Q < q) lo = mid; else hi = mid;
        }
        return (lo + hi) / 2;
    }

    public static RainDrainageResult Size(RainDrainageInput i)
    {
        if (i.AreaM2 <= 0 || i.IntensityMmMin <= 0 || i.SlopePercent <= 0) throw new ArgumentException("Alan, yağış şiddeti ve eğim pozitif olmalı.");
        if (i.C is <= 0 or > 1) throw new ArgumentException("C katsayısı 0 ile 1 arasında olmalı.");
        double n = i.ManningN ?? RainRegulation.Materials.FirstOrDefault(m => m.Id == i.Material)?.Manning
            ?? throw new ArgumentException($"Bilinmeyen boru malzemesi: {i.Material}");
        double r = RainRegulation.YieldFromIntensity(i.IntensityMmMin);
        double qLps = i.C * r * i.AreaM2 / 10_000;                 // denklem 1.8, A in ha
        double s = i.SlopePercent / 100, q = qLps / 1000;
        var rows = Diameters.Select(dmm =>
        {
            double d = dmm / 1000.0;
            double cap = PartFull(d, i.MaxFilling, n, s).Q;
            double y = cap >= q ? FillingFor(q, d, n, s) : i.MaxFilling;
            double? v = cap >= q ? q / PartFull(d, y, n, s).Area : null;
            var warn = new List<string>();
            if (cap >= q && v < RainRegulation.MinVelocity) warn.Add("hız < 0,5 m/s (çökelme)");
            if (cap >= q && v > RainRegulation.MaxVelocity) warn.Add("hız > 5 m/s");
            if (i.Material == "beton" && RainRegulation.ConcreteSlopes.FirstOrDefault(c => c.DiameterMm == dmm) is { DiameterMm: > 0 } lim)
            {
                double a = 1 / s;   // slope as 1:A
                if (a > lim.MinA) warn.Add($"eğim 1:{a:0} < en küçük 1:{lim.MinA}" + (lim.ExceptionalMinA is { } ex && a <= ex ? $" (istisnai 1:{ex} sınırında)" : ""));
                if (a < lim.MaxA) warn.Add($"eğim 1:{a:0} > en büyük 1:{lim.MaxA}");
            }
            bool fits = cap >= q && v >= RainRegulation.MinVelocity && v <= RainRegulation.MaxVelocity;
            return new RainPipeRow(dmm, cap * 1000, cap >= q ? y : null, v, fits, warn.Count > 0 ? string.Join("; ", warn) : null);
        }).ToArray();
        return new RainDrainageResult(r, qLps, n, rows, rows.FirstOrDefault(x => x.Fits)?.DiameterMm);
    }
}
