using MuhendisKit.Core.Insulation;

namespace MuhendisKit.Core.Tests;

public class EnvelopeTests
{
    static Element Wall(bool insulationInside) => new("Dış duvar", "duvar", 0.13, 0.04, insulationInside
        ?
        [
            new("Taş yünü", 0.05, 0.035, 1),
            new("Betonarme", 0.20, 2.5, 80),
        ]
        :
        [
            new("İç sıva", 0.02, 0.7, 10),
            new("Tuğla", 0.19, 0.45, 5),
            new("EPS", 0.05, 0.035, 60),
        ]);

    [Fact]
    public void UValueIsInverseOfResistanceSum()
    {
        var w = Wall(false);
        Assert.Equal(2.04936, w.RTotal, 4);
        Assert.Equal(0.48796, w.U, 4);
    }

    [Fact]
    public void RequiredInsulationThickness()
    {
        var w = Wall(false);
        double d = Envelope.RequiredThickness(w, 2, 0.40);
        Assert.Equal(0.06577, d, 4);
        var check = w with { Layers = [w.Layers[0], w.Layers[1], w.Layers[2] with { ThicknessM = d }] };
        Assert.Equal(0.40, check.U, 6);
    }

    [Fact]
    public void ExternallyInsulatedWallHasNoCondensation()
    {
        var r = Envelope.Glaser(Wall(false), new Climate(20, 0.65, -5, 0.85));
        Assert.Empty(r.Condensation);
        Assert.All(r.Points, p => Assert.True(p.VapourPressure <= p.SaturationPressure + 1e-6));
        Assert.Equal(20 - (25 / Wall(false).RTotal) * 0.13, r.InsideSurfaceTemp, 6);
        Assert.False(r.SurfaceCondensation);
    }

    [Fact]
    public void InternalInsulationCondensesBehindIt()
    {
        var wall = Wall(true);
        var c = new Climate(20, 0.65, -5, 0.85);
        var r = Envelope.Glaser(wall, c);
        var plane = Assert.Single(r.Condensation);
        Assert.Equal(1, plane.Interface);   // between wool and concrete
        Assert.All(r.Points, p => Assert.True(p.VapourPressure <= p.SaturationPressure + 1e-6));

        // Hand calculation: flux in − flux out at the plane with δ0 = 2e-10
        double pi = 0.65 * Envelope.SaturationPressure(20), pe = 0.85 * Envelope.SaturationPressure(-5);
        double ps = r.Points[1].SaturationPressure;
        double gIn = (pi - ps) / 0.05 * 2e-10, gOut = (ps - pe) / (0.20 * 80) * 2e-10;
        Assert.Equal((gIn - gOut) * 3600 * 1000, plane.Rate, 6);
    }

    [Fact]
    public void DewPointInvertsSaturationPressure()
    {
        Assert.Equal(12.0, Envelope.DewPoint(Envelope.SaturationPressure(12.0)), 9);
        Assert.Equal(-8.0, Envelope.DewPoint(Envelope.SaturationPressure(-8.0)), 9);
    }
}
