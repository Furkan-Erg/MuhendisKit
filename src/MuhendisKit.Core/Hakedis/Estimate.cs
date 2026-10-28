using ClosedXML.Excel;

namespace MuhendisKit.Core.Hakedis;

/// <summary>
/// Yaklaşık maliyet (keşif): unit-price items with their measurement lines, priced from one catalogue source.
/// The ministry's poz prices already contain the contractor's profit and overheads (%25) and exclude VAT.
/// </summary>
public sealed class Estimate
{
    public string Title { get; set; } = "";
    public string PreparedBy { get; set; } = "";
    public string Date { get; set; } = "";          // "yyyy-MM-dd"
    public string PriceSource { get; set; } = "";   // e.g. "ÇŞİDB 2026 Ekim"
    public decimal KdvRate { get; set; } = 20;
    public List<PozItem> Items { get; set; } = [];
    /// <summary>Metraj lines; <see cref="MeasureLine.Period"/> is not used here.</summary>
    public List<MeasureLine> Lines { get; set; } = [];
}

public sealed record EstimateRow(string Id, string PozNo, string Description, string Unit, decimal UnitPrice, decimal Quantity, decimal Amount, int LineCount);

/// <param name="Discipline">İnşaat, Mekanik, Elektrik or Diğer, from the poz number family</param>
public sealed record EstimateGroup(string Discipline, int Items, decimal Amount);

public sealed record EstimateResult(EstimateRow[] Rows, EstimateGroup[] Groups, decimal Total, decimal Kdv, decimal GrandTotal, string[] Warnings);

public sealed record RepriceChange(string Id, string PozNo, decimal OldPrice, decimal? NewPrice, string? NewUnit);

public static class EstimateCalculator
{
    /// <summary>Ministry poz families: 15/19 inşaat, 25 mekanik, 35 elektrik; rayiç families 10/20/30 likewise.</summary>
    public static string Discipline(string pozNo) => pozNo.Length >= 2 ? pozNo[..2] switch
    {
        "10" or "15" or "19" => "İnşaat",
        "20" or "25" => "Mekanik",
        "30" or "35" => "Elektrik",
        _ => "Diğer",
    } : "Diğer";

    static readonly string[] DisciplineOrder = ["İnşaat", "Mekanik", "Elektrik", "Diğer"];

    public static EstimateResult Compute(Estimate e)
    {
        var rows = e.Items.Select(it =>
        {
            var lines = e.Lines.Where(l => l.ItemId == it.Id).ToList();
            decimal qty = lines.Sum(l => l.Quantity);
            return new EstimateRow(it.Id, it.PozNo, it.Description, it.Unit, it.UnitPrice, qty, Math.Round(qty * it.UnitPrice, 2), lines.Count);
        }).ToArray();
        var groups = rows.GroupBy(r => Discipline(r.PozNo)).OrderBy(g => Array.IndexOf(DisciplineOrder, g.Key))
            .Select(g => new EstimateGroup(g.Key, g.Count(), g.Sum(r => r.Amount))).ToArray();
        decimal total = rows.Sum(r => r.Amount);
        decimal kdv = Math.Round(total * e.KdvRate / 100, 2);
        var warnings = new List<string>();
        foreach (var r in rows)
        {
            if (r.UnitPrice <= 0) warnings.Add($"{Label(r)}: birim fiyat yok.");
            if (r.Quantity == 0) warnings.Add($"{Label(r)}: metraj girilmemiş.");
            if (r.Quantity < 0) warnings.Add($"{Label(r)}: metraj toplamı negatif.");
        }
        if (e.Lines.Any(l => e.Items.All(i => i.Id != l.ItemId))) warnings.Add("Pozu silinmiş metraj satırları var; toplama girmedi.");
        return new EstimateResult(rows, groups, total, kdv, total + kdv, [.. warnings]);
    }

    static string Label(EstimateRow r) => string.IsNullOrWhiteSpace(r.PozNo) ? r.Description : r.PozNo;

    /// <summary>New unit prices from <paramref name="source"/> (e.g. a later month); items not in that list keep their price.</summary>
    public static List<RepriceChange> Reprice(Estimate e, PozCatalog catalog, string source, bool apply)
    {
        var bySource = catalog.Items.Where(i => i.Source == source).GroupBy(i => i.PozNo).ToDictionary(g => g.Key, g => g.First());
        var changes = new List<RepriceChange>();
        foreach (var it in e.Items.Where(i => i.PozNo.Length > 0))
        {
            var hit = bySource.GetValueOrDefault(it.PozNo);
            changes.Add(new RepriceChange(it.Id, it.PozNo, it.UnitPrice, hit?.UnitPrice, hit?.Unit));
            if (apply && hit is not null)
            {
                it.UnitPrice = hit.UnitPrice;
                if (it.Unit.Length == 0) it.Unit = hit.Unit;
            }
        }
        if (apply) e.PriceSource = source;
        return changes;
    }
}

public static class EstimateExcel
{
    public static void Write(Estimate e, string projectName, string path)
    {
        var s = EstimateCalculator.Compute(e);
        using var wb = new XLWorkbook();
        string title = string.IsNullOrWhiteSpace(e.Title) ? projectName : e.Title;

        var ws = wb.AddWorksheet("İcmal");
        ExcelExport.Title(ws, $"{title} — Yaklaşık Maliyet İcmali", 3);
        int r = 3;
        ws.Cell(r, 1).Value = "Fiyat kaynağı"; ws.Cell(r++, 2).Value = e.PriceSource;
        ws.Cell(r, 1).Value = "Tarih"; ws.Cell(r++, 2).Value = e.Date;
        if (e.PreparedBy.Length > 0) { ws.Cell(r, 1).Value = "Hazırlayan"; ws.Cell(r++, 2).Value = e.PreparedBy; }
        r++;
        ExcelExport.Header(ws, r++, ["İş grubu", "Poz sayısı", "Tutar (TL)"]);
        int first = r;
        foreach (var g in s.Groups) { ws.Cell(r, 1).Value = g.Discipline; ws.Cell(r, 2).Value = g.Items; ws.Cell(r, 3).Value = g.Amount; r++; }
        void Sum(string label, decimal v, bool bold = true)
        {
            ws.Cell(r, 1).Value = label; ws.Cell(r, 3).Value = v;
            if (bold) ws.Range(r, 1, r, 3).Style.Font.Bold = true;
            r++;
        }
        Sum("Toplam (KDV hariç)", s.Total);
        Sum($"KDV (%{e.KdvRate:0.##})", s.Kdv, bold: false);
        Sum("Yaklaşık maliyet (KDV dahil)", s.GrandTotal);
        ws.Range(first, 3, r, 3).Style.NumberFormat.Format = ExcelExport.MoneyFmt;
        r++;
        ws.Cell(r++, 1).Value = "Birim fiyatlar yüklenici kârı ve genel giderleri (%25) içerir, KDV hariçtir.";
        foreach (var w in s.Warnings) { ws.Cell(r, 1).Value = "Uyarı: " + w; ws.Cell(r++, 1).Style.Font.FontColor = XLColor.DarkRed; }
        ExcelExport.Widths(ws, 40, 14, 22);

        ws = wb.AddWorksheet("Yaklaşık Maliyet");
        ExcelExport.Title(ws, "Yaklaşık Maliyet Hesap Cetveli", 7);
        ExcelExport.Header(ws, 3, ["Sıra", "Poz No", "İmalatın Cinsi", "Birim", "Miktar", "Birim Fiyat", "Tutar"]);
        r = 4;
        int no = 1;
        foreach (var row in s.Rows)
        {
            ws.Cell(r, 1).Value = no++; ws.Cell(r, 2).Value = row.PozNo; ws.Cell(r, 3).Value = row.Description; ws.Cell(r, 4).Value = row.Unit;
            ws.Cell(r, 5).Value = row.Quantity; ws.Cell(r, 6).Value = row.UnitPrice; ws.Cell(r, 7).Value = row.Amount;
            r++;
        }
        ws.Cell(r, 3).Value = "TOPLAM (KDV hariç)"; ws.Cell(r, 7).Value = s.Total; ws.Row(r).Style.Font.Bold = true;
        ws.Range(4, 5, r, 5).Style.NumberFormat.Format = ExcelExport.QtyFmt;
        ws.Range(4, 6, r, 7).Style.NumberFormat.Format = ExcelExport.MoneyFmt;
        ws.Column(3).Style.Alignment.WrapText = true;
        ExcelExport.Widths(ws, 6, 14, 60, 8, 14, 14, 18);

        ws = wb.AddWorksheet("Metraj");
        ExcelExport.Title(ws, "Metraj Cetveli", 8);
        ExcelExport.Header(ws, 3, ["Poz No", "Açıklama", "Adet", "En", "Boy", "Yükseklik", "Miktar", "Kaynak"]);
        r = 4;
        foreach (var item in e.Items)
        {
            var lines = e.Lines.Where(l => l.ItemId == item.Id).ToList();
            if (lines.Count == 0) continue;
            ws.Cell(r, 1).Value = item.PozNo; ws.Cell(r, 2).Value = $"{item.Description} ({item.Unit})";
            ws.Range(r, 1, r, 8).Style.Font.Bold = true;
            ws.Range(r, 1, r, 8).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF4EA");
            r++;
            foreach (var l in lines)
            {
                ws.Cell(r, 2).Value = (l.Deduct ? "(−) " : "") + l.Description;
                if (l.Fixed is null)
                {
                    ws.Cell(r, 3).Value = l.Count;
                    if (l.Width is { } w) ws.Cell(r, 4).Value = w;
                    if (l.Length is { } b) ws.Cell(r, 5).Value = b;
                    if (l.Height is { } h) ws.Cell(r, 6).Value = h;
                }
                ws.Cell(r, 7).Value = l.Quantity; ws.Cell(r, 8).Value = l.Source;
                r++;
            }
            ws.Cell(r, 2).Value = "Toplam"; ws.Cell(r, 7).Value = lines.Sum(l => l.Quantity);
            ws.Range(r, 2, r, 7).Style.Font.Bold = true;
            r += 2;
        }
        ws.Range(4, 3, r, 7).Style.NumberFormat.Format = ExcelExport.QtyFmt;
        ExcelExport.Widths(ws, 14, 50, 9, 9, 9, 10, 13, 24);

        wb.SaveAs(path);
    }
}
