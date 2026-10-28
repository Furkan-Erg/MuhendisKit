namespace MuhendisKit.Core.Calc;

public sealed record ChainRoom(string Name, double HeatW, double? RoomC = null);

/// <param name="SupplyC">gidiş; the ministry's heating specification rates boilers at 80/60 °C</param>
/// <param name="Exponent">radiator exponent n from the catalogue</param>
/// <param name="CatalogPerUnitW">ΔT50 output per metre / section / unit of the chosen radiator, optional</param>
/// <param name="CriticalLengthM">en uzak devrenin gidiş + dönüş boru boyu</param>
/// <param name="LocalLossFactor">yerel kayıplar, sürekli kaybın oranı olarak (0,3 = %30) — an assumption the engineer sets</param>
/// <param name="EquipmentKPa">kazan, vana, radyatör vanası vb. kayıplar</param>
public sealed record HeatingChainInput(ChainRoom[] Rooms, double SupplyC, double ReturnC, double RoomC, double Exponent, double? CatalogPerUnitW,
    string Material, double MaxVelocity, double MaxPressureDrop, double CriticalLengthM, double LocalLossFactor, double EquipmentKPa);

public sealed record ChainPipe(string? Size, double FlowM3h, double? Velocity, double? PressureDropPaM);

public sealed record ChainRoomResult(string Name, double HeatW, double RequiredNominalW, double? Units, ChainPipe Pipe);

public sealed record ChainPump(double FlowM3h, double PipeLossKPa, double LocalLossKPa, double EquipmentKPa, double HeadKPa, double HeadM);

public sealed record HeatingChainResult(ChainRoomResult[] Rooms, double TotalHeatW, ChainPipe Main, ChainPump Pump, string[] Notes);

/// <summary>
/// Room heat losses → radiator ΔT50 output (EN 442 exponent law) → water flow and branch pipe → main pipe → pump
/// flow and head. A pre-sizing chain: the pump head uses the main pipe's pressure drop over the critical circuit.
/// </summary>
public static class HeatingChain
{
    public static HeatingChainResult Compute(HeatingChainInput i)
    {
        if (i.Rooms.Length == 0) throw new ArgumentException("Mahal yok: önce ısı kaybını hesaplayın.");
        if (i.SupplyC <= i.ReturnC) throw new ArgumentException("Gidiş sıcaklığı dönüşten yüksek olmalı.");
        double dt = i.SupplyC - i.ReturnC, tMean = (i.SupplyC + i.ReturnC) / 2;
        var notes = new List<string>();

        ChainPipe Pipe(double kw)
        {
            double flow = PipeSizing.FlowFromHeat(kw, dt, tMean);
            var sized = PipeSizing.Size(new PipeSizingInput(i.Material, flow, tMean, i.MaxVelocity, i.MaxPressureDrop));
            var row = sized.Rows.FirstOrDefault(r => r.Name == sized.Suggested);
            return new ChainPipe(sized.Suggested, flow, row?.Velocity, row?.PressureDropPerM);
        }

        var rooms = i.Rooms.Where(r => r.HeatW > 0).Select(r =>
        {
            var rad = Radiator.Size(new RadiatorInput(r.HeatW, i.SupplyC, i.ReturnC, r.RoomC ?? i.RoomC, i.Exponent, i.CatalogPerUnitW));
            return new ChainRoomResult(r.Name, r.HeatW, rad.RequiredNominalW, rad.Units, Pipe(r.HeatW / 1000));
        }).ToArray();
        double total = rooms.Sum(r => r.HeatW);
        var main = Pipe(total / 1000);
        if (main.Size is null) notes.Add("Ana hat için seçilen malzemede sınırları sağlayan çap yok; hız/basınç kaybı sınırlarını ya da malzemeyi değiştirin.");
        foreach (var r in rooms.Where(r => r.Pipe.Size is null)) notes.Add($"{r.Name}: branşman için uygun çap yok.");

        double pipeKPa = (main.PressureDropPaM ?? 0) * i.CriticalLengthM / 1000;
        double localKPa = pipeKPa * i.LocalLossFactor;
        double head = pipeKPa + localKPa + i.EquipmentKPa;
        double rho = Water.Density(tMean);
        var pump = new ChainPump(main.FlowM3h, pipeKPa, localKPa, i.EquipmentKPa, head, head * 1000 / (rho * 9.81));
        if (i.CriticalLengthM <= 0) notes.Add("Kritik devre uzunluğu girilmedi: pompa basma yüksekliği yalnız ekipman kayıplarından oluşuyor.");
        notes.Add("Ön boyutlandırma: pompa basma yüksekliği ana hattın birim kaybıyla kritik devre boyunca hesaplandı; kesin seçimde devre devre basınç kaybı hesabı yapın.");
        return new HeatingChainResult(rooms, total, main, pump, [.. notes]);
    }
}
