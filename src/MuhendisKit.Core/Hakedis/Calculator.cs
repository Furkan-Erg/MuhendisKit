namespace MuhendisKit.Core.Hakedis;

public sealed record ItemRow(
    string ItemId, string PozNo, string Description, string Unit, decimal UnitPrice,
    decimal PrevQty, decimal ThisQty, decimal CumQty, decimal PrevAmount, decimal ThisAmount, decimal CumAmount);

public sealed record DeductionRow(string Name, decimal RatePercent, decimal Amount);

/// <param name="Pn">null when an index is missing; then no price difference is computed</param>
public sealed record PeriodSummary(
    int No, string Month,
    ItemRow[] Items,
    decimal CumAmount, decimal PrevAmount, decimal ThisAmount,
    decimal? G0, decimal? Gn, decimal? Pn, decimal PriceDiff, decimal CumPriceDiff,
    decimal Subtotal, decimal Kdv, DeductionRow[] Deductions, decimal Payable,
    string[] Warnings);

public static class Calculator
{
    public static PeriodSummary[] All(Job job) =>
        job.Periods.OrderBy(p => p.No).Select(p => Period(job, p.No)).ToArray();

    public static PeriodSummary Period(Job job, int no)
    {
        var period = job.Periods.FirstOrDefault(p => p.No == no) ?? throw new ArgumentException($"{no} nolu hakediş yok.");
        var warnings = new List<string>();

        var rows = job.Items.Select(item =>
        {
            var lines = job.Lines.Where(l => l.ItemId == item.Id).ToList();
            decimal prev = lines.Where(l => l.Period < no).Sum(l => l.Quantity);
            decimal cur = lines.Where(l => l.Period == no).Sum(l => l.Quantity);
            decimal prevAmt = Money(prev * item.UnitPrice), cumAmt = Money((prev + cur) * item.UnitPrice);
            return new ItemRow(item.Id, item.PozNo, item.Description, item.Unit, item.UnitPrice,
                prev, cur, prev + cur, prevAmt, cumAmt - prevAmt, cumAmt);
        }).Where(r => r.CumQty != 0 || r.PrevQty != 0).ToArray();

        decimal cum = rows.Sum(r => r.CumAmount), prevTotal = rows.Sum(r => r.PrevAmount), cur = cum - prevTotal;
        foreach (var orphan in job.Lines.Where(l => l.Period <= no && job.Items.All(i => i.Id != l.ItemId)).Select(l => l.Description).Distinct())
            warnings.Add($"Pozu silinmiş metraj satırı hesaba katılmadı: {orphan}");
        if (job.ContractAmount > 0 && cum > job.ContractAmount)
            warnings.Add($"Kümülatif tutar sözleşme bedelini %{(cum / job.ContractAmount - 1) * 100:0.##} aşıyor (iş artışı).");

        var (g0, gn, pn) = Indices(job, period);
        decimal diff = 0;
        if (pn is null)
            warnings.Add("Endeks eksik olduğu için fiyat farkı hesaplanmadı.");
        else
            diff = Money(cur * job.B * (pn.Value - 1));
        decimal cumDiff = diff + job.Periods.Where(p => p.No < no).Sum(p =>
        {
            decimal amount = PeriodAmount(job, p.No);
            var (_, _, ppn) = Indices(job, p);
            return ppn is null ? 0 : Money(amount * job.B * (ppn.Value - 1));
        });

        decimal subtotal = cur + diff;
        decimal kdv = Money(subtotal * job.KdvRate / 100);
        var deds = job.Deductions.Select(d => new DeductionRow(d.Name, d.RatePercent, Money(d.Base switch
        {
            "kdv" => kdv,
            "toplam" => subtotal + kdv,
            _ => subtotal,
        } * d.RatePercent / 100))).ToArray();

        return new PeriodSummary(no, period.Month, rows, cum, prevTotal, cur, g0, gn, pn, diff, cumDiff,
            subtotal, kdv, deds, subtotal + kdv - deds.Sum(d => d.Amount), warnings.ToArray());
    }

    /// <summary>Pn = Gn / G0 with the smaller of the application-month and allowance-month indices.</summary>
    public static (decimal? G0, decimal? Gn, decimal? Pn) Indices(Job job, Period p)
    {
        decimal? Get(string? month) => month is not null && job.Indices.TryGetValue(month, out var v) && v > 0 ? v : null;
        decimal? g0 = Get(job.TenderMonth);
        decimal? gn = Get(p.Month);
        if (gn is not null && Get(p.AllowanceMonth) is { } ga && ga < gn) gn = ga;
        if (g0 is null || gn is null) return (g0, gn, null);
        return (g0, gn, Math.Round(gn.Value / g0.Value, job.PnDigits, MidpointRounding.AwayFromZero));
    }

    static decimal PeriodAmount(Job job, int no) => job.Items.Sum(item =>
    {
        var lines = job.Lines.Where(l => l.ItemId == item.Id);
        decimal prev = lines.Where(l => l.Period < no).Sum(l => l.Quantity);
        decimal cum = prev + lines.Where(l => l.Period == no).Sum(l => l.Quantity);
        return Money(cum * item.UnitPrice) - Money(prev * item.UnitPrice);
    });

    public static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
}
