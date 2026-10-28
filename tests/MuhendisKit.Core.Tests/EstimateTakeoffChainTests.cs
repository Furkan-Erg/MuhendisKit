using MuhendisKit.Core.Calc;
using MuhendisKit.Core.Hakedis;

namespace MuhendisKit.Core.Tests;

public class EstimateTests
{
    static Estimate Sample()
    {
        var kazi = new PozItem { PozNo = "15.115.1001", Description = "El ile yumuşak toprak kazılması", Unit = "m³", UnitPrice = 607.70m };
        var boru = new PozItem { PozNo = "25.305.2101", Description = "PN 20 Polipropilen Borular; 1/2\"", Unit = "m", UnitPrice = 88.98m };
        var pano = new PozItem { PozNo = "35.100.6101", Description = "Sac pano 800 mm", Unit = "Ad", UnitPrice = 54063.80m };
        var bos = new PozItem { PozNo = "25.130.1302", Description = "Musluk", Unit = "Ad", UnitPrice = 0 };
        return new Estimate
        {
            KdvRate = 20,
            Items = [kazi, boru, pano, bos],
            Lines =
            [
                new() { ItemId = kazi.Id, Count = 2, Width = 1.5m, Length = 10, Height = 1.2m },   // 36 m³
                new() { ItemId = kazi.Id, Fixed = 4, Deduct = true },                              // −4 m³
                new() { ItemId = boru.Id, Fixed = 125.5m },
                new() { ItemId = pano.Id, Fixed = 1 },
            ],
        };
    }

    [Fact]
    public void ComputesAmountsGroupsAndVat()
    {
        var r = EstimateCalculator.Compute(Sample());
        Assert.Equal(32m, r.Rows[0].Quantity);
        Assert.Equal(19446.40m, r.Rows[0].Amount);      // 32 × 607,70
        Assert.Equal(11166.99m, r.Rows[1].Amount);      // 125,5 × 88,98 = 11 166,99
        Assert.Equal(["İnşaat", "Mekanik", "Elektrik"], r.Groups.Select(g => g.Discipline));
        Assert.Equal(19446.40m + 11166.99m + 54063.80m, r.Total);
        Assert.Equal(Math.Round(r.Total * 0.2m, 2), r.Kdv);
        Assert.Contains(r.Warnings, w => w.Contains("25.130.1302") && w.Contains("birim fiyat"));
        Assert.Contains(r.Warnings, w => w.Contains("25.130.1302") && w.Contains("metraj"));
    }

    [Fact]
    public void RepricesFromAnotherMonthAndKeepsMissing()
    {
        var e = Sample();
        var cat = new PozCatalog();
        cat.Replace("ÇŞİDB 2026 Ekim", [new CatalogPoz("15.115.1001", "Kazı", "m³", 700m, "ÇŞİDB 2026 Ekim"), new CatalogPoz("25.305.2101", "Boru", "m", 95m, "ÇŞİDB 2026 Ekim")]);
        var changes = EstimateCalculator.Reprice(e, cat, "ÇŞİDB 2026 Ekim", apply: true);
        Assert.Equal(700m, e.Items[0].UnitPrice);
        Assert.Equal(95m, e.Items[1].UnitPrice);
        Assert.Equal(54063.80m, e.Items[2].UnitPrice);                 // not in that list: unchanged
        Assert.Null(changes.Single(c => c.PozNo == "35.100.6101").NewPrice);
        Assert.Equal("ÇŞİDB 2026 Ekim", e.PriceSource);
    }

    [Fact]
    public void WritesWorkbook()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mk-est-{Guid.NewGuid():n}.xlsx");
        try
        {
            EstimateExcel.Write(Sample(), "Deneme", path);
            using var wb = new ClosedXML.Excel.XLWorkbook(path);
            Assert.Equal(["İcmal", "Yaklaşık Maliyet", "Metraj"], wb.Worksheets.Select(w => w.Name));
        }
        finally { File.Delete(path); }
    }
}

public class TakeoffTests
{
    static readonly PozItem Boru = new() { PozNo = "25.305.2101", Description = "PN 20 Polipropilen Borular 20 mm", Unit = "m" };
    static readonly PozItem Radyator = new() { PozNo = "25.225.3004", Description = "Panel radyatörler Tip 10", Unit = "m" };
    static readonly PozItem Kombi = new() { PozNo = "25.200.1100", Description = "Kombi cihazı", Unit = "Ad" };
    static readonly PozItem Seramik = new() { PozNo = "15.270.1001", Description = "Seramik kaplama yapılması", Unit = "m²" };

    [Fact]
    public void UnitsMustFitTheQuantityKind()
    {
        Assert.True(Takeoff.UnitFits("length", "m"));
        Assert.True(Takeoff.UnitFits("area", "m²"));
        Assert.True(Takeoff.UnitFits("count", "Ad."));
        Assert.False(Takeoff.UnitFits("length", "m²"));
        Assert.False(Takeoff.UnitFits("count", "m"));
    }

    [Fact]
    public void SuggestsSavedChoiceThenMatchingNames()
    {
        PozItem[] items = [Boru, Radyator, Kombi, Seramik];
        TakeoffRow[] rows =
        [
            new("L:PPR_BORULAR", "length", "PPR_BORULAR", "PPR_BORULAR", 125.4),
            new("B:KOMBI|MEKANIK", "count", "KOMBI", "MEKANIK", 3),
            new("A:SERAMIK", "area", "SERAMIK", "SERAMIK", 42),
            new("L:DUVAR", "length", "DUVAR", "DUVAR", 80),
            new("L:RADYATOR_HAT", "length", "RADYATOR_HAT", "RADYATOR_HAT", 12),
        ];
        var saved = new Dictionary<string, string> { ["L:RADYATOR_HAT"] = "25.305.2101" };
        var s = Takeoff.Suggest(rows, items, saved);
        Assert.Equal((Boru.Id, "name"), (s[0].ItemId, s[0].Reason));        // "borular" ~ "Borular"
        Assert.Equal(Kombi.Id, s[1].ItemId);                                // count → Ad
        Assert.Equal(Seramik.Id, s[2].ItemId);                              // area → m²
        Assert.Null(s[3].ItemId);                                           // nothing about "duvar"
        Assert.Equal((Boru.Id, "saved"), (s[4].ItemId, s[4].Reason));       // the earlier choice wins over the name
    }
}

public class HeatingChainTests
{
    static HeatingChainInput Input(double length = 60) => new(
        [new ChainRoom("Salon", 2000), new ChainRoom("Oda", 1000), new ChainRoom("Banyo", 500, 24)],
        80, 60, 20, 1.3, null, "celik", 1.0, 150, length, 0.3, 20);

    [Fact]
    public void RadiatorsAndFlowsPerRoom()
    {
        var r = HeatingChain.Compute(Input());
        // 80/60/20: ΔTlm = 20 / ln(60/40) = 49,33 K; f = (49,33 / 49,83)^1,3 = 0,987 → 2000 W needs ≈ 2027 W at ΔT50
        Assert.Equal(Radiator.Size(new RadiatorInput(2000, 80, 60, 20, 1.3)).RequiredNominalW, r.Rooms[0].RequiredNominalW, 9);
        Assert.InRange(r.Rooms[0].RequiredNominalW, 2020, 2035);
        // The 24 °C bathroom needs relatively more catalogue output than the 20 °C room
        Assert.True(r.Rooms[2].RequiredNominalW / 500 > r.Rooms[1].RequiredNominalW / 1000);
        Assert.Equal(PipeSizing.FlowFromHeat(2, 20, 70), r.Rooms[0].Pipe.FlowM3h, 9);
        Assert.Equal(3500, r.TotalHeatW);
        Assert.Equal(PipeSizing.FlowFromHeat(3.5, 20, 70), r.Main.FlowM3h, 9);
        Assert.NotNull(r.Main.Size);
    }

    [Fact]
    public void PumpHeadFromCriticalCircuit()
    {
        var r = HeatingChain.Compute(Input(80));
        double pipe = r.Main.PressureDropPaM!.Value * 80 / 1000;
        Assert.Equal(pipe, r.Pump.PipeLossKPa, 9);
        Assert.Equal(pipe * 1.3 + 20, r.Pump.HeadKPa, 9);
        Assert.Equal(r.Pump.HeadKPa * 1000 / (Water.Density(70) * 9.81), r.Pump.HeadM, 9);
        Assert.Equal(r.Main.FlowM3h, r.Pump.FlowM3h);
    }
}
