namespace MuhendisKit.Core.Calc;

/// <param name="WinterDesignC">kış dış hava tasarım sıcaklığı (DH)</param>
/// <param name="SummerDryBulbC">yaz kuru termometre (KT)</param>
/// <param name="SummerWetBulbC">yaz yaş termometre (YT)</param>
/// <param name="RainYieldLpsHa">akma yağış miktarı i (l/s·ha), for roof drainage</param>
public sealed record City(string Name, double AltitudeM, double Latitude, double WinterDesignC, double SummerDryBulbC, double SummerWetBulbC, double? RainYieldLpsHa);

/// <summary>
/// Province climate data from tesisat.org (İklim Verileri page) and the rain yield its Yağmur Borusu Çap Hesabı uses per
/// province, read 2026-10-28 with Tesisat Online's permission for this non-commercial, in-house use. Düzce is not listed there.
/// </summary>
public static class Cities
{
    public const string Source = "tesisat.org (İklim Verileri, Yağmur Borusu Çap Hesabı), 28.10.2026";

    public static readonly City[] All =
    [
        new("Adana", 21, 37, 0, 38, 26, 407.0),
        new("Adıyaman", 678, 37.76, -9, 38, 22, 457.0),
        new("Afyonkarahisar", 1019, 38.76, -12, 34, 21, 261.0),
        new("Ağrı", 1585, 39.72, -24, 34, 25, 211.0),
        new("Aksaray", 980, 38.37, -15, 34, 20, 246.0),
        new("Amasya", 412, 40.65, -12, 31, 21, 278.0),
        new("Ankara", 895, 39.92, -12, 35, 21, 308.0),
        new("Antalya", 43, 36.88, 3, 39, 28, 551.0),
        new("Ardahan", 1829, 41.11, -21, 27, 18, 279.0),
        new("Artvin", 597, 41.18, -9, 30, 26, 185.0),
        new("Aydın", 70, 37.84, -3, 39, 26, 202.0),
        new("Balıkesir", 103, 39.65, -3, 38, 27, 302.0),
        new("Bartın", 30, 41.63, -3, 31, 21, 289.0),
        new("Batman", 540, 37.88, -9, 40, 23, 174.0),
        new("Bayburt", 1550, 40.26, -15, 33, 23, 281.0),
        new("Bilecik", 526, 40.15, -9, 34, 23, 311.0),
        new("Bingöl", 1177, 38.89, -18, 33, 21, 167.0),
        new("Bitlis", 1559, 38.4, -15, 34, 22, 206.0),
        new("Bolu", 728, 40.74, -15, 34, 24, 337.0),
        new("Burdur", 1025, 37.73, -9, 36, 21, 285.0),
        new("Bursa", 99, 40.18, -6, 37, 25, 372.0),
        new("Çanakkale", 3, 40.16, -3, 34, 25, 262.0),
        new("Çankırı", 730, 40.6, -15, 34, 26, 241.0),
        new("Çorum", 803, 40.55, -15, 29, 19, 469.0),
        new("Denizli", 420, 37.78, -6, 38, 24, 361.0),
        new("Diyarbakır", 652, 37.91, -9, 42, 23, 189.0),
        new("Edirne", 47, 41.67, -9, 36, 25, 376.0),
        new("Elazığ", 1090, 38.68, -12, 38, 21, 243.0),
        new("Erzincan", 1157, 39.75, -18, 36, 22, 159.0),
        new("Erzurum", 1893, 39.9, -21, 31, 23, 239.0),
        new("Eskişehir", 790, 39.78, -12, 34, 24, 276.0),
        new("Gaziantep", 849, 37.07, -9, 39, 23, 311.0),
        new("Giresun", 40, 40.91, -3, 29, 25, 338.0),
        new("Gümüşhane", 1219, 40.46, -12, 33, 23, 259.0),
        new("Hakkari", 1720, 37.58, -24, 34, 20, 125.0),
        new("Hatay", 3, 36.4, 3, 37, 29, 702.0),
        new("Iğdır", 855, 39.92, -18, 36, 25, 250.0),
        new("Isparta", 1050, 37.77, -9, 34, 21, 277.0),
        new("İstanbul", 40, 41.01, -3, 33, 24, 310.0),
        new("İzmir", 3, 38.42, 0, 37, 25, 416.0),
        new("Kahramanmaraş", 549, 37.58, -9, 36, 22, 232.0),
        new("Karabük", 354, 41.2, -12, 32, 25, 223.0),
        new("Karaman", 1025, 37.18, -12, 34, 21, 316.0),
        new("Kars", 1750, 40.59, -27, 30, 20, 273.0),
        new("Kastamonu", 800, 41.39, -12, 34, 22, 419.0),
        new("Kayseri", 1058, 38.73, -15, 36, 23, 244.0),
        new("Kırıkkale", 725, 39.85, -12, 35, 21, 329.0),
        new("Kırklareli", 232, 41.73, -9, 35, 25, 375.0),
        new("Kırşehir", 980, 39.15, -12, 35, 21, 386.0),
        new("Kilis", 638, 36.72, -6, 39, 23, 303.0),
        new("Kocaeli", 77, 40.85, -3, 36, 25, 408.0),
        new("Konya", 1024, 37.87, -12, 34, 22, 249.0),
        new("Kütahya", 935, 39.42, -12, 33, 21, 398.0),
        new("Malatya", 915, 38.36, -12, 38, 21, 284.0),
        new("Manisa", 42, 38.62, -3, 40, 26, 307.0),
        new("Mardin", 1150, 37.31, -6, 38, 23, 500.0),
        new("Mersin", 6, 36.8, 3, 35, 29, 370.0),
        new("Muğla", 648, 37.22, -3, 37, 22, 357.0),
        new("Muş", 1283, 38.74, -18, 32, 20, 173.0),
        new("Nevşehir", 1260, 38.62, -15, 28, 17, 229.0),
        new("Niğde", 1239, 37.97, -15, 34, 20, 196.0),
        new("Ordu", 4, 40.98, -3, 30, 22, 328.0),
        new("Osmaniye", 121, 37.07, -3, 38, 26, 495.0),
        new("Rize", 60, 41.02, -3, 30, 26, 445.0),
        new("Sakarya", 30, 40.76, -3, 35, 25, 351.0),
        new("Samsun", 40, 41.29, -3, 32, 25, 440.0),
        new("Siirt", 875, 37.94, -9, 40, 23, 249.0),
        new("Sinop", 25, 42.03, -3, 30, 25, 307.0),
        new("Sivas", 1285, 39.75, -18, 33, 20, 187.0),
        new("Şanlıurfa", 515, 37.15, -6, 43, 24, 320.0),
        new("Şırnak", 1343, 37.52, -6, 34, 20, 433.0),
        new("Tekirdağ", 55, 40.98, -6, 33, 25, 371.0),
        new("Tokat", 608, 40.32, -15, 29, 20, 234.0),
        new("Trabzon", 109, 41, -3, 31, 25, 317.0),
        new("Tunceli", 979, 39.11, -18, 37, 22, 200.0),
        new("Uşak", 911, 38.68, -9, 35, 22, 264.0),
        new("Van", 1732, 38.49, -15, 33, 21, 223.0),
        new("Yalova", 2, 40.65, -3, 33, 24, 506.0),
        new("Yozgat", 1320, 39.82, -15, 32, 20, 317.0),
        new("Zonguldak", 42, 41.46, -3, 32, 25, 507.0),
    ];

    public static City? Find(string? name) => name is null ? null
        : All.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase))
          ?? All.FirstOrDefault(c => name.Contains(c.Name, StringComparison.CurrentCultureIgnoreCase));
}
