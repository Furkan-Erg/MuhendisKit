using MuhendisKit.Core.Calc;

namespace MuhendisKit.Core.Tests;

public class PlumbingTests
{
    [Fact]
    public void RainFlowByRationalMethodAndPipeChoice()
    {
        // r = 166,7 · 2 = 333,4 l/s·ha; Q = 0,9 · 333,4 · 0,1 ha = 30,006 l/s.
        // Beton n = 0,013, %1: Ø150 carries ≈ 16 l/s at %90 filling, Ø200 ≈ 35 l/s → Ø200.
        var r = RainDrainage.Size(new RainDrainageInput(1000, 0.9, 2, "beton", 1));
        Assert.Equal(333.4, r.YieldLpsHa, 6);
        Assert.Equal(30.006, r.FlowLps, 6);
        Assert.Equal(200, r.Suggested);
        var d200 = r.Rows.Single(x => x.DiameterMm == 200);
        Assert.InRange(d200.Filling!.Value, 0.5, 0.9);
        Assert.InRange(d200.Velocity!.Value, 0.5, 5);
        Assert.False(r.Rows.Single(x => x.DiameterMm == 150).Fits);
    }

    [Fact]
    public void ManningHalfFullCarriesHalfOfFull()
    {
        // At h/D = 0,5 the hydraulic radius equals the full pipe's (D/4) and the area is half.
        double full = 1 / 0.013 * (Math.PI * 0.04 / 4) * Math.Pow(0.05, 2.0 / 3) * Math.Sqrt(0.01);
        Assert.Equal(full / 2, RainDrainage.PartFull(0.2, 0.5, 0.013, 0.01).Q, 9);
        Assert.Equal(0.5, RainDrainage.FillingFor(full / 2, 0.2, 0.013, 0.01), 6);
    }

    [Fact]
    public void ConcreteSlopeOutsideTableIsFlagged()
    {
        // Çizelge 1.4: Ø400 en küçük eğim 1:600 (istisnai 1:900); 1:1000 (%0,1) is flatter than both.
        var r = RainDrainage.Size(new RainDrainageInput(20_000, 0.7, 2, "beton", 0.1));
        Assert.Contains("1:600", r.Rows.Single(x => x.DiameterMm == 400).Warning);
    }

    [Fact]
    public void RadiatorNeedsMoreCatalogueOutputAtLowerTemperatures()
    {
        // 70/55/20: ΔTlm = 15 / ln(50/35) = 42,055 K; f = (42,055 / 49,833)^1,3 = 0,8020 → 1000 W needs 1246,8 W at ΔT50.
        var r = Radiator.Size(new RadiatorInput(1000, 70, 55, 20, 1.3, CatalogPerUnitW: 100));
        Assert.Equal(49.83, Radiator.NominalDeltaT, 2);
        Assert.Equal(42.0551, r.DeltaTLog, 4);
        Assert.Equal(1246.83, r.RequiredNominalW, 2);
        Assert.Equal(12.468, r.Units!.Value, 3);
        Assert.Equal(1000, Radiator.Size(new RadiatorInput(1000, 75, 65, 20, 1.3)).RequiredNominalW, 6);
    }

    [Fact]
    public void BoosterPressuresTankAndSpecificationChecks()
    {
        // pin = 20,4/10,2 + 0,5 + 1 = 3,5 bar, pout = 4,5 bar; 1 l/s, 20 starts/h → Vd = 1·180/4 = 45 L, V = 45 · 5,5 / 1 = 247,5 L.
        var r = Booster.Size(new BoosterInput(3.6, 20.4, 0.5, 1.0, 1.0, 20));
        Assert.Equal(3.5, r.CutInBar, 9);
        Assert.Equal(4.5, r.CutOutBar, 9);
        Assert.Equal(45, r.DrawdownL, 9);
        Assert.Equal(247.5, r.TankVolumeL, 9);
        Assert.Contains(r.Warnings, w => w.Contains("4 bar"));
        Assert.Equal(2, Booster.Size(new BoosterInput(3.6, 75, 0.5, 1, 1, 20)).ZonesNeeded);
    }
}
