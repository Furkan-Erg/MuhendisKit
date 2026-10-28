namespace MuhendisKit.Core.Calc;

// ---- Expansion vessel (TS EN 12828, closed membrane vessel) ----

public sealed record ExpansionTankInput(
    double SystemVolumeL,
    double MaxTempC,          // max flow temperature
    double StaticHeightM,     // height of the highest point above the vessel
    double SafetyValveBar,
    double FillTempC = 10);

public sealed record ExpansionTankResult(
    double ExpansionCoeff, double ExpansionVolumeL, double WaterReserveL,
    double PrechargeBar, double FinalPressureBar, double NominalVolumeL, int? StandardSizeL);

public static class ExpansionTank
{
    public static readonly int[] StandardSizes = [8, 12, 18, 24, 35, 50, 80, 100, 150, 200, 250, 300, 400, 500, 600, 800, 1000];

    public static ExpansionTankResult Size(ExpansionTankInput i)
    {
        double e = Water.Expansion(i.MaxTempC, i.FillTempC);
        double ve = e * i.SystemVolumeL;
        double vwr = Math.Max(0.005 * i.SystemVolumeL, 3);
        double p0 = i.StaticHeightM / 10 + 0.2;                                   // gauge, bar
        double pe = i.SafetyValveBar <= 5 ? i.SafetyValveBar - 0.5 : 0.9 * i.SafetyValveBar;
        if (pe <= p0)
            throw new ArgumentException("Emniyet ventili basıncı, statik yüksekliğe göre çok düşük (pe ≤ p0).");
        double vn = (ve + vwr) * (pe + 1) / (pe - p0);
        int? std = StandardSizes.Where(s => s >= vn).Select(s => (int?)s).FirstOrDefault();
        return new ExpansionTankResult(e, ve, vwr, p0, pe, vn, std);
    }
}

// ---- Natural gas, low pressure (Renouard, linear form) ----

public sealed record GasPipeInput(
    string Material,
    double FlowM3h,
    double LengthM,           // equivalent length including fittings
    double MaxDropMbar,
    double RelativeDensity = 0.6);

public sealed record GasPipeRow(string Name, double InnerDiameterMm, double DropMbar, double Velocity, double QOverD, bool Fits);

public sealed record GasPipeResult(GasPipeRow[] Rows, string? Suggested);

public static class GasPipe
{
    /// <summary>ΔP (mbar) = 23200 · ρr · L(m) · Q(m³/h)^1.82 · D(mm)^-4.82. Recommended range Q/D &lt; 150.</summary>
    public static double Drop(double q, double d, double l, double rhoR) =>
        23200 * rhoR * l * Math.Pow(q, 1.82) * Math.Pow(d, -4.82);

    public static GasPipeResult Size(GasPipeInput i)
    {
        var mat = PipeCatalog.Find(i.Material);
        var rows = mat.Sizes.Select(s =>
        {
            double dp = Drop(i.FlowM3h, s.InnerDiameterMm, i.LengthM, i.RelativeDensity);
            double v = i.FlowM3h / 3600 / (Math.PI * Math.Pow(s.InnerDiameterMm / 1000, 2) / 4);
            return new GasPipeRow(s.Name, s.InnerDiameterMm, dp, v, i.FlowM3h / s.InnerDiameterMm, dp <= i.MaxDropMbar);
        }).ToArray();
        return new GasPipeResult(rows, rows.FirstOrDefault(r => r.Fits)?.Name);
    }
}

// ---- Room heat loss (TS 2164 structure; design temperatures and increments are entered by the engineer) ----

public sealed record HeatLossSurface(string Name, double Area, double U, double TempDiff);

public sealed record HeatLossRoom(
    string Name,
    HeatLossSurface[] Surfaces,
    double VolumeM3,
    double AirChangesPerHour,
    double InsideTempC,
    double OutsideTempC,
    double IncrementPercent);   // sum of the TS 2164 increments (orientation, intermittent operation …)

public sealed record HeatLossRoomResult(string Name, double TransmissionW, double IncrementW, double InfiltrationW, double TotalW);

public static class HeatLoss
{
    /// <summary>ρ·cp of air ≈ 0.34 Wh/(m³·K).</summary>
    public const double AirHeatCapacity = 0.34;

    public static HeatLossRoomResult Room(HeatLossRoom r)
    {
        double q0 = r.Surfaces.Sum(s => s.Area * s.U * s.TempDiff);
        double z = q0 * r.IncrementPercent / 100;
        double inf = AirHeatCapacity * r.AirChangesPerHour * r.VolumeM3 * (r.InsideTempC - r.OutsideTempC);
        return new HeatLossRoomResult(r.Name, q0, z, inf, q0 + z + inf);
    }
}
