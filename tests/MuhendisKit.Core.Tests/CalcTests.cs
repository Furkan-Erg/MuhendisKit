using MuhendisKit.Core.Calc;

namespace MuhendisKit.Core.Tests;

public class CalcTests
{
    [Fact]
    public void WaterDensityInterpolates()
    {
        Assert.Equal(998.21, Water.Density(20), 2);
        Assert.Equal(996.93, Water.Density(25), 2);
    }

    [Theory]
    [InlineData(1000, 0.0001, 0.064)]       // laminar 64/Re
    [InlineData(1e5, 0.0001, 0.0185)]       // Moody chart
    [InlineData(1e6, 0.001, 0.0199)]
    public void FrictionFactorMatchesMoody(double re, double rr, double f) =>
        Assert.InRange(Hydraulics.FrictionFactor(re, rr), f * 0.99, f * 1.01);

    [Fact]
    public void PipeSizingPicksSmallestFittingSize()
    {
        // 50 kW at ΔT 20 K ≈ 2.17 m³/h
        double q = PipeSizing.FlowFromHeat(50, 20, 70);
        Assert.Equal(2.20, q, 1);
        var r = PipeSizing.Size(new PipeSizingInput("celik", q, 70, 1.0, 150));
        Assert.Equal("DN32 (1¼\")", r.Suggested);
        Assert.All(r.Rows.TakeWhile(x => x.Name != r.Suggested), x => Assert.False(x.Fits));
    }

    [Theory]
    [InlineData(25, 3169.9, 1.0)]     // ASHRAE Fundamentals table
    [InlineData(-20, 103.26, 0.1)]
    [InlineData(100, 101418, 30)]
    public void SaturationPressureMatchesAshrae(double t, double p, double tol) =>
        Assert.InRange(Psychro.SaturationPressure(t), p - tol, p + tol);

    [Fact]
    public void PsychroStateAt25C50Percent()
    {
        var s = Psychro.FromRh(25, 0.5, 101325);
        Assert.Equal(0.00988, s.W, 4);
        Assert.Equal(50.3, s.Enthalpy, 1);
        Assert.Equal(13.86, s.DewPoint, 1);
        Assert.Equal(17.9, s.WetBulb, 1);
        // Round trip through the wet bulb gives the same humidity ratio
        Assert.Equal(s.W, Psychro.FromWetBulb(25, s.WetBulb, 101325).W, 6);
    }

    [Fact]
    public void HuebscherEquivalentDiameter() =>
        Assert.Equal(420, Duct.EquivalentDiameter(300, 500), 0);

    [Fact]
    public void DuctSizingEqualFriction()
    {
        var r = Duct.Size(new DuctInput(1000, 1.0));
        Assert.InRange(r.RoundDiameterMm, 240, 280);   // ASHRAE friction chart
        Assert.True(r.StandardDiameterMm >= r.RoundDiameterMm);
        Assert.True(r.PressureDropPerM <= 1.0);
        Assert.True(r.RectEquivalentMm >= r.RoundDiameterMm);
    }

    [Fact]
    public void ExpansionTankEn12828()
    {
        var r = ExpansionTank.Size(new ExpansionTankInput(500, 80, 10, 3));
        Assert.Equal(0.02872, r.ExpansionCoeff, 4);
        Assert.Equal(3, r.WaterReserveL);
        Assert.Equal(1.2, r.PrechargeBar, 6);
        Assert.Equal(2.5, r.FinalPressureBar, 6);
        Assert.Equal(46.74, r.NominalVolumeL, 1);
        Assert.Equal(50, r.StandardSizeL);
    }

    [Fact]
    public void RenouardLowPressure()
    {
        Assert.Equal(0.2668, GasPipe.Drop(2.5, 21.7, 10, 0.6), 3);
        var r = GasPipe.Size(new GasPipeInput("celik", 2.5, 10, 0.5));
        Assert.Equal("DN20 (¾\")", r.Suggested);
    }

    [Fact]
    public void RoomHeatLoss()
    {
        var r = HeatLoss.Room(new HeatLossRoom("Salon",
            [new("Dış duvar", 10, 0.5, 25), new("Pencere", 2, 2.0, 25)], 50, 0.5, 20, -5, 10));
        Assert.Equal(225, r.TransmissionW, 6);
        Assert.Equal(22.5, r.IncrementW, 6);
        Assert.Equal(212.5, r.InfiltrationW, 6);
        Assert.Equal(460, r.TotalW, 6);
    }
}
