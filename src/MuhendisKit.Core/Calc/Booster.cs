namespace MuhendisKit.Core.Calc;

/// <summary>
/// Pressure criteria of the ministry's Makina Tesisatı Genel Teknik Şartnamesi (ÇŞİDB YFK, bölüm 2.4.2, read 2026-10-28):
/// normal taps need 1 bar flow pressure; static pressure 1 … 4 bar; tall buildings are split into pressure zones of at
/// most 20 floors (60 m); flow pressure difference within a zone ≈ 3 bar; velocity in risers and mains ≤ 1,5 m/s.
/// </summary>
public static class WaterSupplyCriteria
{
    public const double TapFlowPressureBar = 1.0, MinStaticBar = 1.0, MaxStaticBar = 4.0, MaxZoneHeightM = 60, MaxVelocity = 1.5;
}

/// <param name="FlowM3h">pik debi (eş zamanlı kullanım), from the fixture calculation</param>
/// <param name="HeightM">hidrofor çıkışı ile en üst musluk arası kot farkı</param>
/// <param name="LossBar">en uzak musluğa kadar boru, sayaç, filtre vb. kayıplar</param>
/// <param name="TapPressureBar">en üst muslukta istenen akma basıncı (şartname: 1 bar)</param>
/// <param name="DifferentialBar">pompa devreye girme–çıkma basınç farkı (hidrofor ayarı)</param>
/// <param name="StartsPerHour">pompa motorunun saatte izin verilen en çok kalkış sayısı (üretici)</param>
/// <param name="LowestFixtureBelowM">hidrofor çıkışının altındaki en alt musluğun derinliği (0 = aynı kot)</param>
public sealed record BoosterInput(double FlowM3h, double HeightM, double LossBar, double TapPressureBar, double DifferentialBar,
    int StartsPerHour, int Pumps = 1, double LowestFixtureBelowM = 0);

public sealed record BoosterResult(double CutInBar, double CutOutBar, double PumpHeadM, double DrawdownL, double TankVolumeL,
    double MaxStaticAtLowestBar, int ZonesNeeded, string[] Warnings);

public static class Booster
{
    /// <summary>
    /// Cut-in pressure = static height + losses + required tap pressure; cut-out = cut-in + differential.
    /// Membrane tank (Boyle, isothermal, precharge at cut-in): drawdown Vd = Q·(3600 / z) / 4 per pump,
    /// total volume V = Vd · (p_out + 1) / (p_out − p_in) (absolute pressures).
    /// </summary>
    public static BoosterResult Size(BoosterInput i)
    {
        if (i.FlowM3h <= 0 || i.StartsPerHour <= 0 || i.DifferentialBar <= 0) throw new ArgumentException("Debi, kalkış sayısı ve basınç farkı pozitif olmalı.");
        double pin = i.HeightM / 10.2 + i.LossBar + i.TapPressureBar;   // 1 bar ≈ 10,2 mSS
        double pout = pin + i.DifferentialBar;
        double qPerPumpLs = i.FlowM3h / 3.6 / Math.Max(1, i.Pumps);
        double vd = qPerPumpLs * (3600.0 / i.StartsPerHour) / 4;
        double vt = vd * (pout + 1) / (pout - pin);
        double staticLowest = pout + i.LowestFixtureBelowM / 10.2;
        int zones = (int)Math.Ceiling((i.HeightM + i.LowestFixtureBelowM) / WaterSupplyCriteria.MaxZoneHeightM);
        var warn = new List<string>();
        if (staticLowest > WaterSupplyCriteria.MaxStaticBar)
            warn.Add($"En alt muslukta statik basınç {staticLowest:0.0} bar > 4 bar (şartname): basınç düşürücü vana ya da zonlama gerekir.");
        if (zones > 1)
            warn.Add($"Toplam yükseklik {i.HeightM + i.LowestFixtureBelowM:0} m: şartnameye göre en çok 60 m'de (20 kat) bir basınç zonu → {zones} zon.");
        if (i.TapPressureBar < WaterSupplyCriteria.TapFlowPressureBar)
            warn.Add("Şartname normal musluklar için 1 bar akma basıncı öngörür.");
        return new BoosterResult(pin, pout, pout * 10.2, vd, vt, staticLowest, Math.Max(1, zones), [.. warn]);
    }
}
