using MuhendisKit.Core.Calc;

namespace MuhendisKit.Core.Tests;

/// <summary>Expected values are tesisat.org's own results for the same inputs (queried 2026-10-28).</summary>
public class TesisatOrgTests
{
    [Fact]
    public void GravityPipeCapacitiesMatchSiteTableAtOnePercent()
    {
        // Site edges at 1:100 rain water: Ø100 ≤ 4,70 < Ø125 ≤ 8,50 < Ø150 ≤ 13,8 < Ø200 ≤ 29,6 < Ø250 ≤ 53,4 l/s
        double[] caps = [4.7, 8.5, 13.8, 29.6, 53.4];
        int[] dns = [100, 125, 150, 200, 250];
        for (int k = 0; k < dns.Length; k++)
            Assert.Equal(caps[k], GravityPipes.Capacity(dns[k], 0.7, 0.01));
        var lines = GravityPipes.Size(rain: true, 100, [4.70, 4.71, 8.5, 8.55, 29.6, 29.61, 53.4, 53.45]);
        Assert.Equal([100, 125, 125, 150, 200, 250, 250, 300], lines.Select(l => l.Dn!.Value));
    }

    [Fact]
    public void GravityPipesFollowSlopeRangesAndVelocity()
    {
        // 1:50 rain water: 1 → Ø70 0,27 m/s; 4 → Ø100 0,54; 9 → Ø125 0,77; 16 → Ø150 0,95; 100 → Ø300 1,49
        var rain = GravityPipes.Size(true, 50, [1, 4, 9, 16, 100]);
        Assert.Equal([70, 100, 125, 150, 300], rain.Select(l => l.Dn!.Value));
        Assert.Equal([0.27, 0.54, 0.77, 0.95, 1.49], rain.Select(l => Math.Round(l.Velocity!.Value, 2)));
        // Foul water 1:50 (Q = 0,5 × YB): YB 4 → 2 l/s Ø100 0,27; 9 → Ø125 0,39; 16 → Ø150 0,48; 100 → Ø300 0,74
        var foul = GravityPipes.Size(false, 50, [1, 4, 9, 16, 100]);
        Assert.Equal([0.5, 2, 4.5, 8, 50], foul.Select(l => l.FlowLps));
        Assert.Equal([70, 100, 125, 150, 300], foul.Select(l => l.Dn!.Value));
        Assert.Equal([0.39, 0.48, 0.74], foul.Skip(2).Select(l => Math.Round(l.Velocity!.Value, 2)));
        // 1:200 rain water starts at Ø150; 1:20 stops at Ø150 ("hidrolik hesap gerekir" beyond)
        Assert.Equal(150, GravityPipes.Size(true, 200, [0.3]).Single().Dn);
        Assert.Null(GravityPipes.Size(true, 20, [200]).Single().Dn);
        // Foul water 1:60: Ø200 up to 22,75 on the site, Ø250 from 23,0
        Assert.Equal([200, 250], GravityPipes.Size(false, 60, [45.5, 46]).Select(l => l.Dn!.Value));
    }

    [Fact]
    public void FixtureUnitMethodMatchesSite()
    {
        // Horizontal, 0,35, 1:100: 1→Ø70, 25→Ø70, 26→Ø100, 100→Ø100, 101→Ø125, 600→Ø150, 1550→Ø200, 1600→hidrolik
        var h = WasteFixtureUnits.Size(true, 0.35, 100, [1, 25, 26, 100, 101, 600, 1550, 1600]);
        Assert.Equal([70, 70, 100, 100, 125, 150, 200, null], h.Select(l => l.Dn));
        // Horizontal, 0,25, 1:50: 26→Ø70, 101→Ø100, 2500→Ø200, 5000→hidrolik
        Assert.Equal([70, 100, 200, null], WasteFixtureUnits.Size(true, 0.25, 50, [26, 101, 2500, 5000]).Select(l => l.Dn));
        // Stack: 40→Ø70, 41→Ø100, 150→Ø100, 151→Ø125, 400→Ø125, 401→Ø150, 900→Ø150, 901→Ø200, 2200→Ø200, 2201→hidrolik
        var v = WasteFixtureUnits.Size(false, 0, 0, [40, 41, 150, 151, 400, 401, 900, 901, 2200, 2201]);
        Assert.Equal([70, 100, 100, 125, 125, 150, 150, 200, 200, null], v.Select(l => l.Dn));
    }

    [Fact]
    public void CityDataAndRainYield()
    {
        var ankara = Cities.Find("Çankaya, Ankara")!;
        Assert.Equal(("Ankara", -12.0, 308.0), (ankara.Name, ankara.WinterDesignC, ankara.RainYieldLpsHa!.Value));
        Assert.Equal(80, Cities.All.Length);
        // Site: Ankara, 1000 m², K = 1 → 30,80 l/s. Our rational method with C = 1 gives the same flow.
        var r = RainDrainage.Size(new RainDrainageInput(1000, 1.0, 0, "beton", 1, YieldLpsHa: ankara.RainYieldLpsHa));
        Assert.Equal(30.8, r.FlowLps, 6);
    }
}
