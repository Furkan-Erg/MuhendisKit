namespace MuhendisKit.Core.Calc;

/// <param name="HeatW">mahal ısı kaybı (tasarım ısı yükü)</param>
/// <param name="Exponent">radyatör üssü n, üretici kataloğundan</param>
/// <param name="CatalogPerUnitW">katalogdaki ΔT50 ısı verimi (W) per metre / dilim / adet; null = only the required ΔT50 output</param>
public sealed record RadiatorInput(double HeatW, double SupplyC, double ReturnC, double RoomC, double Exponent, double? CatalogPerUnitW = null);

public sealed record RadiatorResult(double DeltaTLog, double Factor, double RequiredNominalW, double? Units, string? Warning);

public static class Radiator
{
    /// <summary>EN 442 nominal conditions 75/65/20 °C: logarithmic mean temperature difference ≈ 49,83 K ("ΔT50").</summary>
    public static readonly double NominalDeltaT = LogMeanDeltaT(75, 65, 20);

    public static double LogMeanDeltaT(double supply, double ret, double room)
    {
        double a = supply - room, b = ret - room;
        if (a <= 0 || b <= 0) throw new ArgumentException("Gidiş ve dönüş sıcaklığı oda sıcaklığından yüksek olmalı.");
        return Math.Abs(a - b) < 1e-9 ? a : (a - b) / Math.Log(a / b);
    }

    /// <summary>Q = Qn · (ΔT / ΔTn)^n, so the catalogue (ΔT50) output needed for the design load is Q / factor.</summary>
    public static RadiatorResult Size(RadiatorInput i)
    {
        if (i.SupplyC < i.ReturnC) throw new ArgumentException("Gidiş sıcaklığı dönüşten düşük olamaz.");
        if (i.Exponent <= 0) throw new ArgumentException("Radyatör üssü n pozitif olmalı.");
        double dt = LogMeanDeltaT(i.SupplyC, i.ReturnC, i.RoomC);
        double f = Math.Pow(dt / NominalDeltaT, i.Exponent);
        double qn = i.HeatW / f;
        string? warn = (i.ReturnC - i.RoomC) / (i.SupplyC - i.RoomC) < 0.3 ? "Dönüş sıcaklığı oda sıcaklığına çok yakın: seçim sonucu hassas." : null;
        return new RadiatorResult(dt, f, qn, i.CatalogPerUnitW is > 0 ? qn / i.CatalogPerUnitW : null, warn);
    }
}
