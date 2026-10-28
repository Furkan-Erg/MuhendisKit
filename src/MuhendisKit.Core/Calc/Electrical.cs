namespace MuhendisKit.Core.Calc;

// ---- Conductors ----

public static class Conductors
{
    /// <summary>Conductivity k in m/(Ω·mm²) as used in Turkish installation practice: copper 56, aluminium 35 (≈ 20 °C).</summary>
    public static double Conductivity(string material) => material switch
    {
        "cu" => 56,
        "al" => 35,
        _ => throw new ArgumentException($"Bilinmeyen iletken: {material} (cu veya al)"),
    };

    /// <summary>Standard cross-sections (mm²), IEC 60228 series.</summary>
    public static readonly double[] Sections = [1.5, 2.5, 4, 6, 10, 16, 25, 35, 50, 70, 95, 120, 150, 185, 240, 300];

    public static bool IsThreePhase(string phase) => phase switch
    {
        "mono" => false,
        "tri" => true,
        _ => throw new ArgumentException($"Faz: mono veya tri olmalı ({phase})."),
    };
}

// ---- Voltage drop ----

/// <param name="Phase">mono (U = phase–neutral, e.g. 230 V) or tri (U = line–line, e.g. 400 V)</param>
/// <param name="LimitPercent">allowed drop; the regulation's value for the circuit type</param>
public sealed record VoltageDropInput(string Phase, double PowerKw, double LengthM, double VoltageV, string Material, double LimitPercent);

public sealed record VoltageDropRow(double SectionMm2, double DropPercent, double DropV, bool Fits);

public sealed record VoltageDropResult(VoltageDropRow[] Rows, double? Suggested, double Conductivity);

public static class VoltageDrop
{
    /// <summary>
    /// Resistive drop with the active power, the form used in Turkish projects:
    /// single phase %e = 200·L·P / (k·S·U²), three phase %e = 100·L·P / (k·S·U²); P in W, L one-way length in m.
    /// Reactance is neglected, which holds for the small sections of building installations.
    /// </summary>
    public static double Percent(bool threePhase, double powerW, double lengthM, double k, double sectionMm2, double voltageV) =>
        (threePhase ? 100 : 200) * lengthM * powerW / (k * sectionMm2 * voltageV * voltageV);

    public static VoltageDropResult Size(VoltageDropInput i)
    {
        bool tri = Conductors.IsThreePhase(i.Phase);
        double k = Conductors.Conductivity(i.Material);
        if (i.VoltageV <= 0 || i.LengthM < 0 || i.PowerKw < 0) throw new ArgumentException("Gerilim, uzunluk ve güç pozitif olmalı.");
        var rows = Conductors.Sections.Select(s =>
        {
            double e = Percent(tri, i.PowerKw * 1000, i.LengthM, k, s, i.VoltageV);
            return new VoltageDropRow(s, e, e / 100 * i.VoltageV, e <= i.LimitPercent);
        }).ToArray();
        return new VoltageDropResult(rows, rows.FirstOrDefault(r => r.Fits)?.SectionMm2, k);
    }
}

// ---- Current and power ----

/// <param name="PowerKw">active power; give this or <paramref name="CurrentA"/></param>
public sealed record PowerInput(string Phase, double VoltageV, double CosPhi, double? PowerKw = null, double? CurrentA = null);

public sealed record PowerResult(double PowerKw, double CurrentA, double ApparentKva, double ReactiveKvar, double CosPhi, double SinPhi);

public static class Power
{
    /// <summary>I = P / (U·cosφ) single phase, I = P / (√3·U·cosφ) three phase; S = P / cosφ; Q = S·sinφ.</summary>
    public static PowerResult Compute(PowerInput i)
    {
        bool tri = Conductors.IsThreePhase(i.Phase);
        if (i.CosPhi is <= 0 or > 1) throw new ArgumentException("cosφ 0 ile 1 arasında olmalı.");
        if (i.VoltageV <= 0) throw new ArgumentException("Gerilim pozitif olmalı.");
        double factor = (tri ? Math.Sqrt(3) : 1) * i.VoltageV * i.CosPhi;   // W per A
        double pW = i.PowerKw is { } p ? p * 1000
            : i.CurrentA is { } a ? a * factor
            : throw new ArgumentException("Güç ya da akım girin.");
        double sin = Math.Sqrt(1 - i.CosPhi * i.CosPhi);
        double s = pW / i.CosPhi;
        return new PowerResult(pW / 1000, pW / factor, s / 1000, s * sin / 1000, i.CosPhi, sin);
    }
}

// ---- Reactive power compensation ----

public sealed record CompensationInput(double PowerKw, double CosPhi1, double CosPhi2);

public sealed record CompensationResult(double Tan1, double Tan2, double CapacitorKvar, double ApparentBeforeKva, double ApparentAfterKva, double CurrentReductionPercent);

public static class Compensation
{
    /// <summary>Qc = P · (tanφ1 − tanφ2). The current falls with the apparent power: ΔI/I = 1 − cosφ1/cosφ2.</summary>
    public static CompensationResult Size(CompensationInput i)
    {
        if (i.CosPhi1 is <= 0 or > 1 || i.CosPhi2 is <= 0 or > 1) throw new ArgumentException("cosφ 0 ile 1 arasında olmalı.");
        if (i.CosPhi2 < i.CosPhi1) throw new ArgumentException("Hedef cosφ mevcut değerden küçük olamaz.");
        static double Tan(double c) => Math.Sqrt(1 - c * c) / c;
        double t1 = Tan(i.CosPhi1), t2 = Tan(i.CosPhi2);
        return new CompensationResult(t1, t2, i.PowerKw * (t1 - t2), i.PowerKw / i.CosPhi1, i.PowerKw / i.CosPhi2, (1 - i.CosPhi1 / i.CosPhi2) * 100);
    }
}
