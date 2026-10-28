namespace MuhendisKit.Core.Calc;

/// <param name="Hazard">bina tehlike sınıfı (BYKHY md. 19): dusuk, orta1 … orta4, yuksek1 … yuksek4</param>
/// <param name="DrySprinkler">kuru veya değişken (alternatif) yağmurlama sistemi; false = ıslak / ön uyarılı</param>
/// <param name="SprinklerHeightM">en alttaki ve en üstteki yağmurlama başlıkları arasındaki yükseklik (Ek-8/A)</param>
/// <param name="CabinetType">yari-sert (TS EN 671-1) or yassi (TS EN 671-2)</param>
public sealed record FireWaterInput(string Hazard, bool Sprinkler, bool DrySprinkler, double? SprinklerHeightM,
    bool Cabinets, string CabinetType, bool Hydrant);

public sealed record FireWaterPart(string Name, double FlowLpm, double Minutes, double VolumeM3, string Source);

/// <param name="TableVolumeM3">Ek-8/A en az depo hacmi (yağmurlamalı sistemlerde ön hesap için), null = hidrolik hesap gerekir</param>
public sealed record FireWaterResult(FireWaterPart[] Parts, double FlowLpm, double Minutes, double VolumeM3,
    double? TableVolumeM3, double DesignVolumeM3, string[] Requirements, string[] Notes);

/// <summary>
/// Fire water reserve per the Binaların Yangından Korunması Hakkında Yönetmelik (BYKHY, 2007/12937 with its amendments),
/// read from the mevzuat.gov.tr text on 2026-10-28: md. 92 (reserve), md. 94 (cabinets), md. 95 (hydrants), Ek-8/A, 8/B, 8/C.
/// </summary>
public static class FireWater
{
    public static readonly string[] Hazards = ["dusuk", "orta1", "orta2", "orta3", "orta4", "yuksek1", "yuksek2", "yuksek3", "yuksek4"];

    public static string HazardName(string h) => h switch
    {
        "dusuk" => "Düşük tehlike",
        "orta1" => "Orta tehlike-1", "orta2" => "Orta tehlike-2", "orta3" => "Orta tehlike-3", "orta4" => "Orta tehlike-4",
        "yuksek1" => "Yüksek tehlike-1", "yuksek2" => "Yüksek tehlike-2", "yuksek3" => "Yüksek tehlike-3", "yuksek4" => "Yüksek tehlike-4",
        _ => throw new ArgumentException($"Bilinmeyen tehlike sınıfı: {h}"),
    };

    static string Group(string h) => h.StartsWith("dusuk") ? "dusuk" : h.StartsWith("orta") ? "orta" : "yuksek";

    /// <summary>md. 92 (3): düşük 30, orta 60, yüksek 90 dakika.</summary>
    public static double Minutes(string hazard) => Group(hazard) switch { "dusuk" => 30, "orta" => 60, _ => 90 };

    /// <summary>
    /// Ek-8/B: tasarım yoğunluğu (mm/dak) and koruma alanı (m²), wet / dry. A dry system in düşük tehlike uses the
    /// orta tehlike-1 row, in orta tehlike-4 the yüksek tehlike-1 row; yüksek tehlike-4 needs TS EN 12845 ("yoğun su").
    /// </summary>
    public static (double DensityMmMin, double AreaM2, string Row) SprinklerDesign(string hazard, bool dry) => (hazard, dry) switch
    {
        ("dusuk", false) => (2.25, 84, "Düşük tehlike, ıslak"),
        ("dusuk", true) => (5.0, 90, "Orta tehlike-1, kuru (düşük tehlike kuru sistemde OT-1 kullanılır)"),
        ("orta1", false) => (5.0, 72, "Orta tehlike-1, ıslak"),
        ("orta1", true) => (5.0, 90, "Orta tehlike-1, kuru"),
        ("orta2", false) => (5.0, 144, "Orta tehlike-2, ıslak"),
        ("orta2", true) => (5.0, 180, "Orta tehlike-2, kuru"),
        ("orta3", false) => (5.0, 216, "Orta tehlike-3, ıslak"),
        ("orta3", true) => (5.0, 270, "Orta tehlike-3, kuru"),
        ("orta4", false) => (5.0, 360, "Orta tehlike-4, ıslak"),
        ("orta4", true) => (7.7, 325, "Yüksek tehlike-1, kuru (orta tehlike-4 kuru sistemde YT-1 kullanılır)"),
        ("yuksek1", false) => (7.7, 260, "Yüksek tehlike-1, ıslak"),
        ("yuksek1", true) => (7.7, 325, "Yüksek tehlike-1, kuru"),
        ("yuksek2", false) => (10.0, 260, "Yüksek tehlike-2, ıslak"),
        ("yuksek2", true) => (10.0, 325, "Yüksek tehlike-2, kuru"),
        ("yuksek3", false) => (12.5, 260, "Yüksek tehlike-3, ıslak"),
        ("yuksek3", true) => (12.5, 325, "Yüksek tehlike-3, kuru"),
        ("yuksek4", _) => throw new ArgumentException("Yüksek tehlike-4 için Ek-8/B \"yoğun su\" der: TS EN 12845'e göre hidrolik hesap yapılmalı."),
        _ => throw new ArgumentException($"Bilinmeyen tehlike sınıfı: {hazard}"),
    };

    /// <summary>Ek-8/C: ilave edilecek yangın dolabı debisi (l/dak).</summary>
    public static double CabinetAddition(string hazard) => Group(hazard) == "yuksek" ? 200 : 100;

    /// <summary>Ek-8/C: ilave edilecek hidrant debisi (l/dak): düşük 400, OT-1/2 400, OT-3/4 1000, yüksek 1500.</summary>
    public static double HydrantAddition(string hazard) => hazard switch
    {
        "dusuk" or "orta1" or "orta2" => 400,
        "orta3" or "orta4" => 1000,
        _ => 1500,
    };

    /// <summary>
    /// Ek-8/A: en az depo hacmi (m³) by group and sprinkler height band (h ≤ 15, ≤ 30, ≤ 45 m); null = hidrolik hesap.
    /// Rows: düşük ıslak; OT-1 ıslak; OT-1 kuru = OT-2 ıslak; OT-2 kuru = OT-3 ıslak; OT-3 kuru = OT-4 ıslak; OT-4 kuru and yüksek: hidrolik.
    /// </summary>
    public static double? TableVolume(string hazard, bool dry, double heightM)
    {
        if (heightM > 45) return null;
        int band = heightM <= 15 ? 0 : heightM <= 30 ? 1 : 2;
        double[]? row = (hazard, dry) switch
        {
            ("dusuk", false) => [9, 10, 11],
            ("orta1", false) => [55, 70, 80],
            ("orta1", true) or ("orta2", false) => [105, 125, 140],
            ("orta2", true) or ("orta3", false) => [135, 160, 185],
            ("orta3", true) or ("orta4", false) => [160, 185, 200],
            _ => null,   // düşük kuru is not in the table; OT-4 kuru and yüksek tehlike: hidrolik hesap
        };
        return row?[band];
    }

    public static FireWaterResult Size(FireWaterInput i)
    {
        string name = HazardName(i.Hazard);
        double minutes = Minutes(i.Hazard);
        var parts = new List<FireWaterPart>();
        var notes = new List<string>();
        var req = new List<string>();
        double? table = null;
        bool yassi = i.CabinetType == "yassi";

        if (i.Sprinkler)
        {
            // md. 92 (5): Ek-8/B yağmurlama debisi + Ek-8/C dolap ve hidrant debisi, × md. 92 (3) süresi.
            var (d, a, row) = SprinklerDesign(i.Hazard, i.DrySprinkler);
            parts.Add(Part($"Yağmurlama: {d:0.##} mm/dak × {a:0} m² ({row})", d * a, minutes, "Ek-8/B, md. 92 (5)"));
            if (i.Cabinets) parts.Add(Part("Yangın dolabı (ilave)", CabinetAddition(i.Hazard), minutes, "Ek-8/C"));
            if (i.Hydrant) parts.Add(Part("Hidrant (ilave)", HydrantAddition(i.Hazard), minutes, "Ek-8/C"));
            if (i.SprinklerHeightM is { } hgt)
            {
                table = TableVolume(i.Hazard, i.DrySprinkler, hgt);
                notes.Add(table is null
                    ? "Ek-8/A bu sınıf/sistem/yükseklik için tablo değeri vermiyor: hidrolik hesap yapılmalı."
                    : $"Ek-8/A ön hesap tablosu: en az {table:0} m³ (h = {hgt:0.#} m).");
            }
            notes.Add("md. 92 (4): Ön hesap Ek-8/A tablosuyla ya da Ek-8/B + Ek-8/C debileriyle yapılabilir; hidrolik hesap yapılırsa onun sonucu esas alınır.");
        }
        else
        {
            if (i.Cabinets)
            {
                // md. 92 (6): yalnız yangın dolabı sistemi: md. 94 tasarım debisi × md. 92 (3) süresi.
                parts.Add(Part(yassi ? "Yangın dolabı, yassı hortum (TS EN 671-2)" : "Yangın dolabı, yarı-sert hortum (TS EN 671-1)",
                    yassi ? 400 : 100, minutes, yassi ? "md. 94 (b-6), md. 92 (6)" : "md. 94 (b-5), md. 92 (6)"));
                if (!i.Hazard.StartsWith("yuksek"))
                    notes.Add("md. 92 (6): Yüksek tehlike sınıfı değilse ve yapıda yalnız yangın dolabı varsa dolaplar kullanma suyu sistemine bağlanabilir; ayrı sistem istenmez.");
            }
            if (i.Hydrant)
            {
                // md. 92 (7): yalnız çevre hidrant sistemi: en az 1900 l/dak × 90 dakika.
                parts.Add(Part("Çevre hidrant sistemi (en az)", 1900, 90, "md. 92 (7), md. 95 (2)"));
                notes.Add("md. 92 (7): Hidrant su ihtiyacı en az 1900 l/dak × 90 dk; kesin değer tehlike sınıfına göre hidrolik hesapla belirlenir. md. 7 (12) kapsamındaki alanlarda dış hidrant için en az 5700 l/dak × 60 dk.");
                if (i.Cabinets) notes.Add("Yönetmelik dolap + hidrant (yağmurlamasız) birleşimini ayrıca tanımlamaz; bileşenler ayrı ayrı hesaplanıp toplandı.");
            }
        }
        if (parts.Count == 0) throw new ArgumentException("En az bir sistem seçin: yağmurlama, yangın dolabı veya hidrant.");

        if (i.Cabinets)
        {
            req.Add($"Yangın dolabı tasarım debisi {(yassi ? 400 : 100)} l/dak, tasarım basıncı {(yassi ? "en az " : "")}400 kPa; lüle girişinde 900 kPa'yı aşarsa basınç düşürücü (md. 94).");
            req.Add("Dolaplar her katta ve yangın duvarıyla ayrılmış her bölümde aralarında en fazla 30 m olacak şekilde (md. 94 b-2).");
        }
        if (i.Hydrant)
        {
            req.Add("Hidrant dizayn debisi en az 1900 l/dak, hidrant çıkışında 700 kPa (md. 95 (2)).");
            req.Add("Hidrantlar arası uzaklık: çok riskli 50 m, riskli 100 m, orta riskli 125 m, az riskli 150 m; binalardan 5–15 m uzakta (md. 95).");
        }

        double flow = parts.Sum(p => p.FlowLpm);
        double volume = parts.Sum(p => p.VolumeM3);
        double design = table is { } t ? Math.Max(t, volume) : volume;
        if (table is not null && table > volume)
            notes.Add("Tasarım hacmi olarak iki ön hesaptan büyüğü gösterildi.");
        return new FireWaterResult([.. parts], flow, parts.Max(p => p.Minutes), volume, table, design, [.. req], [$"{name}: süre {minutes:0} dk (md. 92 (3)).", .. notes]);
    }

    static FireWaterPart Part(string name, double lpm, double minutes, string source) => new(name, lpm, minutes, lpm * minutes / 1000, source);
}
