namespace MuhendisKit.Core.Calc;

/// <param name="CapacityKcalh">kazan kapasitesi Q (kcal/h)</param>
/// <param name="HeightM">bina (baca) yüksekliği H</param>
/// <param name="FuelGroup">Katı Yakıtlı, Sıvı Yakıtlı or Gaz Yakıtlı</param>
public sealed record ChimneyInput(double CapacityKcalh, double HeightM, string FuelGroup, string Fuel);

public sealed record ChimneyResult(double K, double SectionCm2, double DiameterCm, double CapacityKw, string? Warning);

/// <summary>
/// Practical natural-draught chimney section, as tesisat.org's "Baca Çapı Hesabı" (read and checked against its results
/// 2026-10-28, Tesisat Online's permission): Fb = k · Q / √H, D = √(4·Fb/π); k by fuel from the same page. The site limits
/// it to pre-sizing small appliances (natural gas 30–70 kW); larger ones need TS 2165 (DIN 4705) / TS EN 13384-1.
/// </summary>
public static class PracticalChimney
{
    public const double KcalhPerKw = 859.845;

    public static readonly Dictionary<string, Dictionary<string, double>> Coefficients = new()
    {
        ["Katı Yakıtlı"] = new()
        {
            ["Kömür"] = 0.04, ["Tütün ve Biomas"] = 0.035, ["Pelet"] = 0.025, ["Odun"] = 0.03, ["Kuru Odun"] = 0.028,
            ["Kül Çıkışı Yüksek Yakıtlar"] = 0.045, ["Biyokütle"] = 0.03, ["Taşkömürü"] = 0.038, ["Ağaç Talaşı"] = 0.027, ["Torf"] = 0.032,
        },
        ["Sıvı Yakıtlı"] = new()
        {
            ["Mazot"] = 0.018, ["LPG"] = 0.015, ["Füzelaj"] = 0.02, ["Süper Ağır Yağ"] = 0.022, ["Biodizel"] = 0.017, ["Kükürt İçeren Yakıtlar"] = 0.019,
        },
        ["Gaz Yakıtlı"] = new()
        {
            ["Doğalgaz"] = 0.012, ["Biyogaz"] = 0.015, ["Kombine Çevrim Gazı"] = 0.01, ["Doğalgazın Karbon Kötüleme"] = 0.014,
            ["Sıvılaştırılmış Doğalgaz (LNG)"] = 0.011, ["Hidrojensiz Gazlar"] = 0.013, ["Metan"] = 0.011, ["Propan"] = 0.012,
        },
    };

    public static ChimneyResult Size(ChimneyInput i)
    {
        if (i.CapacityKcalh <= 0 || i.HeightM <= 0) throw new ArgumentException("Kapasite ve yükseklik pozitif olmalı.");
        if (!Coefficients.TryGetValue(i.FuelGroup, out var group) || !group.TryGetValue(i.Fuel, out double k))
            throw new ArgumentException($"Bilinmeyen yakıt: {i.FuelGroup} / {i.Fuel}");
        double fb = k * i.CapacityKcalh / Math.Sqrt(i.HeightM);
        double kw = i.CapacityKcalh / KcalhPerKw;
        string? warn = kw >= 70
            ? $"{kw:0} kW: pratik yöntem 70 kW altındaki cihazlar içindir; TS 2165 (DIN 4705) / TS EN 13384-1'e göre ayrıntılı hesap gerekir."
            : i.FuelGroup == "Gaz Yakıtlı" && kw < 30 ? "Kaynak, doğalgazda bu yöntemi 30–70 kW aralığı için verir." : null;
        return new ChimneyResult(k, fb, Math.Sqrt(4 * fb / Math.PI), kw, warn);
    }
}
