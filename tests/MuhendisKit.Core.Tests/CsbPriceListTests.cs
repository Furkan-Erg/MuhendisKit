using MuhendisKit.Core.Hakedis;

namespace MuhendisKit.Core.Tests;

/// <summary>Synthetic pages laid out like the ministry's PDFs (x and baseline in points, as PdfPig reports them).</summary>
public class CsbPriceListTests
{
    // A line of words starting at x; words are ~5 pt per character apart, like the 9 pt body font.
    static IEnumerable<PdfWord> L(double y, double x, string text, bool bold = false)
    {
        foreach (string word in text.Split(' '))
        {
            yield return new PdfWord(word, x, x + word.Length * 4.5, y, bold);
            x += word.Length * 4.5 + 3;
        }
    }

    static PdfWord W(double y, double x, string text, bool bold = false) => new(text, x, x + text.Length * 4.5, y, bold);

    static IEnumerable<PdfWord> BookHeader(bool mounting) =>
    [
        W(780, 41, "S", true), W(748, 41, "a", true), W(748, 62, "Poz", true), W(748, 78, "No", true),
        W(748, 243, "Yapılacak", true), W(748, 459, "Birim", true), W(748, 484, "Fiyat", true),
        .. mounting ? new[] { W(758, 466, "Montajlı", true), W(752, 512, "Montaj", true), W(752, 543, "Bedeli", true) } : [],
    ];

    [Fact]
    public void BookPrependsGroupTitleAndTakesUnitFromItsText()
    {
        var page = BookHeader(mounting: true)
            .Append(W(726, 40, "N", true))   // vertical "Sıra No" caption, level with the first row
            .Concat([W(724, 45, "25.104.1000", true), .. L(724, 110, "AYNALAR: Ölçü: Ad. (TS EN 1036)", bold: true)])
            .Concat(L(712, 110, "Cam kalınlığı 5 mm, ayna kenarı rodajlı olacaktır."))
            .Concat(L(700, 110, "25.104.0000 pozuna uygun."))    // a poz number inside the text is no row
            .Concat([W(688, 45, "25.104.1001"), .. L(688, 110, "Takriben 40x50 cm."), W(688, 478, "1.074,38"), W(688, 541, "261,88")])
            .Concat([W(674, 45, "25.104.1003"), .. L(674, 110, "Engelli kullanımına uygun ayna", bold: true), W(674, 478, "2.537,50"), W(674, 541, "337,50")])
            .Concat(L(662, 110, "Ayarlanabilir açılı paslanmaz çelik çerçeveli."));

        var items = CsbPriceList.Parse([page], "test");

        Assert.Equal(["25.104.1001", "25.104.1003"], items.Select(i => i.PozNo));
        var small = items[0];
        Assert.Equal("AYNALAR: Takriben 40x50 cm.", small.Description);
        Assert.Equal("Ad", small.Unit);
        Assert.Equal((1074.38m, 261.88m), (small.UnitPrice, small.MountingPrice!.Value));
        Assert.Null(small.Spec);
        // Bold title, then the specification in regular type.
        Assert.Equal("Ayarlanabilir açılı paslanmaz çelik çerçeveli.", items[1].Spec);
        Assert.Equal("AYNALAR: Engelli kullanımına uygun ayna", items[1].Description);
    }

    [Fact]
    public void BookReadsUnitColumnAndContinuationLines()
    {
        var page = BookHeader(mounting: false)
            .Concat([W(724, 45, "15.115.1001"), .. L(724, 110, "El ile yumuşak toprak kazılması"), W(724, 489, "m³"), W(724, 541, "512,50")])
            .Concat(L(714, 110, "(gevşek ve bitkisel toprak)"))
            .Concat(L(702, 110, "B) DERİN KAZILAR", bold: true));
        // A page without a column header, where a note mentions "Poz No" below the first rows.
        var next = new[] { W(774, 45, "15.115.1011") }.Concat(L(774, 110, "El ile batak kazılması")).Concat([W(774, 489, "m³"), W(774, 535, "1.537,50")])
            .Concat(L(604, 110, "(Derinliği 2.00 metreden fazla olan kazılara ayrıca Poz No : 14.040 uygulanır.)", bold: true))
            .Concat([W(580, 45, "15.115.1201"), .. L(580, 110, "El ile derin kazı yapılması"), W(580, 489, "m³"), W(580, 535, "1.057,04")]);

        var items = CsbPriceList.Parse([page, next], "test");

        Assert.Equal(["15.115.1001", "15.115.1011", "15.115.1201"], items.Select(i => i.PozNo));
        Assert.Equal("El ile yumuşak toprak kazılması (gevşek ve bitkisel toprak)", items[0].Description);
        Assert.Equal(("m³", 512.50m), (items[0].Unit, items[0].UnitPrice));
        Assert.Equal(1057.04m, items[2].UnitPrice);
    }

    [Fact]
    public void RayicPlaceOfPurchaseStaysInTheDescription()
    {
        var page = BookHeader(mounting: false)
            .Concat([W(724, 45, "10.130.1202"), .. L(724, 110, "Portland çimentosu (Dökme)"), W(724, 427, "Ton"), W(724, 462, "Fabrikada"), W(724, 535, "2.790,00")]);

        var item = Assert.Single(CsbPriceList.Parse([page], "test"));

        Assert.Equal("Portland çimentosu (Dökme) (Fabrikada)", item.Description);
        Assert.Equal(("Ton", 2790m), (item.Unit, item.UnitPrice));
    }

    [Fact]
    public void MonthlyListCentresTextAroundThePozLine()
    {
        IEnumerable<PdfWord> header =
        [
            W(758, 58, "Poz"), W(758, 74, "No"), W(758, 230, "Tanım"), W(758, 401, "Birim"),
            W(758, 447, "Güncel"), W(758, 473, "Birim"), W(758, 509, "Güncel"), W(758, 535, "Montaj"),
        ];
        var page = header
            .Concat(L(700, 102, "1/2\" Kromajlı Çamaşır- Bulaşık Musluğu, klasik salmastralı, musluk rozeti"))
            .Concat([W(694, 49, "25.130.1302"), W(694, 407, "Ad"), W(694, 474, "627,69"), W(694, 542, "93,01")])
            .Concat(L(690, 102, "dahil."))
            .Concat(L(652, 127, "TEK VE ÇİFT KUMANDALI ARMATÜRLER: (Ölçü: Ad.)"))   // centred heading, not part of a row
            .Concat([W(626, 49, "25.130.3101"), .. L(626, 102, "Tek kumandalı eviye bataryası:"), W(626, 407, "Ad"), W(626, 467, "3.476,48"), W(626, 538, "415,76")]);
        // Next page has no header: still a monthly list.
        var next = new[] { W(774, 52, "25.130.3102") }.Concat(L(774, 102, "Duvardan çıkışlı eviye bataryası:"))
            .Concat([W(774, 407, "Ad"), W(774, 467, "4.322,53"), W(774, 538, "415,76")])
            .Concat(L(780, 102, "Tek kumandalı,"));

        var items = CsbPriceList.Parse([page, next], "test");

        Assert.Equal(["25.130.1302", "25.130.3101", "25.130.3102"], items.Select(i => i.PozNo));
        Assert.Equal("1/2\" Kromajlı Çamaşır- Bulaşık Musluğu, klasik salmastralı, musluk rozeti dahil.", items[0].Description);
        Assert.Equal(("Ad", 627.69m, 93.01m), (items[0].Unit, items[0].UnitPrice, items[0].MountingPrice!.Value));
        Assert.Equal("Tek kumandalı eviye bataryası:", items[1].Description);
        Assert.Equal("Tek kumandalı, Duvardan çıkışlı eviye bataryası:", items[2].Description);
    }

    [Fact]
    public void CatalogSearchMatchesInflectedWordsAndListsPozPrefixFirst()
    {
        var catalog = new PozCatalog();
        catalog.Replace("A", [
            new CatalogPoz("25.202.1101", "Çelik Malzemeden Kaynaklı Kalorifer Kazanları: 46 kW", "Adet", 104250m, "A"),
            new CatalogPoz("25.130.1302", "Kromajlı Çamaşır musluğu, 25.202 kazanına bağlantı", "Ad", 627.69m, "A"),
            new CatalogPoz("25.202.1102", "Çelik Malzemeden Kaynaklı Kalorifer Kazanları: 70 kW", "Adet", 127195m, "A"),
        ]);
        catalog.Replace("B", [new CatalogPoz("25.202.1101", "Kalorifer kazanı 46 kW", "Adet", 99000m, "B")]);

        Assert.Equal(["B", "A"], catalog.Search("kalorifer kazanı 46").Select(i => i.Source));
        Assert.Equal(["25.202.1101", "25.202.1102", "25.130.1302"], catalog.Search("25.202", source: "A").Select(i => i.PozNo));
    }

    [Fact]
    public void MonthlyItemTakesBookTextOnlyWhenItContainsTheMonthlyLine()
    {
        var monthly = new CatalogPoz("25.470.1601", "20 mm yalıtım kalınlık 80/80 micron Al kaplama", "m²", 2429.36m, "ÇŞİDB 2026 Ekim", 952.33m);
        var book = new CatalogPoz("25.470.1601", "Ön izoleli hava kanalı yapılması: 20 mm yalıtım kalınlık 80/80 micron Al kaplama", "", 2058.13m,
            "ÇŞİDB 2026 Ocak", 803.13m, "En az 20 mm kalınlıkta …");

        var merged = CsbSite.Enrich(monthly, book);
        Assert.Equal(book.Description, merged.Description);
        Assert.Equal(book.Spec, merged.Spec);
        Assert.Equal(("m²", 2429.36m, 952.33m, "ÇŞİDB 2026 Ekim"), (merged.Unit, merged.UnitPrice, merged.MountingPrice!.Value, merged.Source));

        var changed = CsbSite.Enrich(monthly with { Description = "Yeni tanım" }, book);
        Assert.Equal("Yeni tanım", changed.Description);
        Assert.Null(changed.Spec);
    }
}
