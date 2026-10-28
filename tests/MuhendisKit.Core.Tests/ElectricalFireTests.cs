using MuhendisKit.Core.Calc;

namespace MuhendisKit.Core.Tests;

public class ElectricalFireTests
{
    [Fact]
    public void VoltageDropSinglePhasePicksFirstSectionWithinLimit()
    {
        // %e = 200·30·2000 / (56·2,5·230²) = 1,620 % > 1,5 → 4 mm² (1,013 %)
        var r = VoltageDrop.Size(new VoltageDropInput("mono", 2, 30, 230, "cu", 1.5));
        Assert.Equal(1.6203, r.Rows.Single(x => x.SectionMm2 == 2.5).DropPercent, 4);
        Assert.Equal(4, r.Suggested);
        Assert.Equal(1.0127, r.Rows.Single(x => x.SectionMm2 == 4).DropPercent, 4);
    }

    [Fact]
    public void VoltageDropThreePhase()
    {
        // %e = 100·50·10000 / (56·4·400²) = 1,395 %; aluminium (k = 35) drops 56/35 times more
        Assert.Equal(1.3951, VoltageDrop.Percent(true, 10_000, 50, 56, 4, 400), 4);
        var al = VoltageDrop.Size(new VoltageDropInput("tri", 10, 50, 400, "al", 3));
        Assert.Equal(1.3951 * 56 / 35, al.Rows.Single(x => x.SectionMm2 == 4).DropPercent, 3);
    }

    [Fact]
    public void PowerFromActivePowerOrCurrent()
    {
        var r = Power.Compute(new PowerInput("tri", 400, 0.85, PowerKw: 10));
        Assert.Equal(16.981, r.CurrentA, 3);   // 10000 / (√3·400·0,85)
        Assert.Equal(11.765, r.ApparentKva, 3);
        Assert.Equal(6.197, r.ReactiveKvar, 3);
        var back = Power.Compute(new PowerInput("mono", 230, 1, CurrentA: 10));
        Assert.Equal(2.3, back.PowerKw, 6);
    }

    [Fact]
    public void CompensationCapacitorPower()
    {
        // Qc = 100 · (tan(acos 0,70) − tan(acos 0,95)) = 100 · (1,0202 − 0,3287) = 69,15 kVAr
        var r = Compensation.Size(new CompensationInput(100, 0.70, 0.95));
        Assert.Equal(69.15, r.CapacitorKvar, 2);
        Assert.Throws<ArgumentException>(() => Compensation.Size(new CompensationInput(100, 0.95, 0.9)));
    }

    [Fact]
    public void FireWaterSprinklerAddsCabinetAndHydrantAndComparesWithTable()
    {
        // OT-2 ıslak: 5 mm/dak × 144 m² = 720 l/dak (Ek-8/B) + dolap 100 + hidrant 400 (Ek-8/C) = 1220 l/dak × 60 dk = 73,2 m³.
        // Ek-8/A, OT-2 ıslak, 15 < h ≤ 30: 125 m³.
        var r = FireWater.Size(new FireWaterInput("orta2", Sprinkler: true, DrySprinkler: false, SprinklerHeightM: 20, Cabinets: true, CabinetType: "yari-sert", Hydrant: true));
        Assert.Equal(1220, r.FlowLpm, 6);
        Assert.Equal(73.2, r.VolumeM3, 6);
        Assert.Equal(125, r.TableVolumeM3);
        Assert.Equal(125, r.DesignVolumeM3);
    }

    [Fact]
    public void FireWaterWithoutSprinklerUsesArticles94And92()
    {
        var cabinets = FireWater.Size(new FireWaterInput("dusuk", false, false, null, Cabinets: true, CabinetType: "yari-sert", Hydrant: false));
        Assert.Equal(3, cabinets.DesignVolumeM3, 6);   // 100 l/dak × 30 dk
        var flat = FireWater.Size(new FireWaterInput("orta3", false, false, null, Cabinets: true, CabinetType: "yassi", Hydrant: false));
        Assert.Equal(24, flat.DesignVolumeM3, 6);      // 400 l/dak × 60 dk
        var hydrant = FireWater.Size(new FireWaterInput("orta1", false, false, null, Cabinets: false, CabinetType: "yari-sert", Hydrant: true));
        Assert.Equal(171, hydrant.DesignVolumeM3, 6);  // 1900 l/dak × 90 dk
    }

    [Fact]
    public void FireWaterTableRowsAndSpecialCases()
    {
        Assert.Equal(9, FireWater.TableVolume("dusuk", false, 10));
        Assert.Equal(140, FireWater.TableVolume("orta1", true, 40));   // OT-1 kuru = OT-2 ıslak row
        Assert.Null(FireWater.TableVolume("orta4", true, 10));         // hidrolik hesap
        Assert.Equal((7.7, 325.0), (FireWater.SprinklerDesign("orta4", true).DensityMmMin, FireWater.SprinklerDesign("orta4", true).AreaM2));
        Assert.Throws<ArgumentException>(() => FireWater.SprinklerDesign("yuksek4", false));
    }
}
