using ClosedXML.Excel;

namespace MuhendisKit.Core.Hakedis;

public static class ExcelExport
{
    internal const string MoneyFmt = "#,##0.00";
    internal const string QtyFmt = "#,##0.000";

    public static void Write(Job job, string projectName, int periodNo, string path)
    {
        var s = Calculator.Period(job, periodNo);
        using var wb = new XLWorkbook();

        // İcmal
        var ws = wb.AddWorksheet("İcmal");
        Title(ws, $"{projectName} — {periodNo} Nolu Hakediş İcmali", 2);
        int r = 3;
        void Row(string label, object? value, bool money = true, bool bold = false)
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 2).Value = value switch { decimal d => d, string t => t, null => "", _ => value.ToString() };
            if (money && value is decimal) ws.Cell(r, 2).Style.NumberFormat.Format = MoneyFmt;
            if (bold) ws.Range(r, 1, r, 2).Style.Font.Bold = true;
            r++;
        }
        Row("İdare", job.Owner); Row("Yüklenici", job.Contractor); Row("Sözleşme no", job.ContractNo);
        Row("Sözleşme bedeli", job.ContractAmount); Row("Uygulama ayı", s.Month);
        r++;
        Row("A — Sözleşme fiyatları ile yapılan işin tutarı (kümülatif)", s.CumAmount);
        Row("B — Önceki hakedişler toplamı", s.PrevAmount);
        Row("C — Bu hakediş tutarı (A − B)", s.ThisAmount, bold: true);
        Row("G0 (temel endeks)", s.G0, money: false); Row("Gn (güncel endeks)", s.Gn, money: false);
        Row("Pn = Gn / G0", s.Pn, money: false); Row("Sabit katsayı B", job.B, money: false);
        Row("D — Fiyat farkı (C × B × (Pn − 1))", s.PriceDiff);
        Row("E — Toplam (C + D)", s.Subtotal, bold: true);
        Row($"F — KDV (%{job.KdvRate:0.##})", s.Kdv);
        Row("G — KDV dahil tutar", s.Subtotal + s.Kdv, bold: true);
        foreach (var d in s.Deductions) Row($"   Kesinti: {d.Name} (%{d.RatePercent:0.###})", d.Amount);
        Row("Yükleniciye ödenecek tutar", s.Payable, bold: true);
        foreach (var w in s.Warnings) { ws.Cell(++r, 1).Value = "Uyarı: " + w; ws.Cell(r, 1).Style.Font.FontColor = XLColor.DarkRed; }
        ws.Column(1).Width = 58; ws.Column(2).Width = 22;

        // Hakediş raporu (poz bazında)
        ws = wb.AddWorksheet("Hakediş Raporu");
        Title(ws, $"{periodNo} Nolu Hakediş — Yapılan İşler Listesi", 10);
        Header(ws, 3, ["Poz No", "İmalatın Cinsi", "Birim", "Birim Fiyat", "Önceki Miktar", "Bu Dönem Miktar", "Toplam Miktar",
            "Önceki Tutar", "Bu Dönem Tutar", "Toplam Tutar"]);
        r = 4;
        foreach (var it in s.Items)
        {
            ws.Cell(r, 1).Value = it.PozNo; ws.Cell(r, 2).Value = it.Description; ws.Cell(r, 3).Value = it.Unit;
            ws.Cell(r, 4).Value = it.UnitPrice; ws.Cell(r, 5).Value = it.PrevQty; ws.Cell(r, 6).Value = it.ThisQty;
            ws.Cell(r, 7).Value = it.CumQty; ws.Cell(r, 8).Value = it.PrevAmount; ws.Cell(r, 9).Value = it.ThisAmount;
            ws.Cell(r, 10).Value = it.CumAmount;
            r++;
        }
        ws.Cell(r, 2).Value = "TOPLAM";
        ws.Cell(r, 8).Value = s.PrevAmount; ws.Cell(r, 9).Value = s.ThisAmount; ws.Cell(r, 10).Value = s.CumAmount;
        ws.Row(r).Style.Font.Bold = true;
        ws.Range(4, 4, r, 4).Style.NumberFormat.Format = MoneyFmt;
        ws.Range(4, 5, r, 7).Style.NumberFormat.Format = QtyFmt;
        ws.Range(4, 8, r, 10).Style.NumberFormat.Format = MoneyFmt;
        Widths(ws, 14, 50, 8, 14, 14, 14, 14, 16, 16, 16);

        // Yeşil defter / ataşman: every measurement line up to this period, grouped by item
        ws = wb.AddWorksheet("Yeşil Defter");
        Title(ws, "Yeşil Defter (Metraj Cetveli)", 10);
        Header(ws, 3, ["Hakediş", "Poz No", "Açıklama", "Adet", "En", "Boy", "Yükseklik", "Miktar", "Birim", "Kaynak"]);
        r = 4;
        foreach (var item in job.Items)
        {
            var lines = job.Lines.Where(l => l.ItemId == item.Id && l.Period <= periodNo).OrderBy(l => l.Period).ToList();
            if (lines.Count == 0) continue;
            ws.Cell(r, 2).Value = item.PozNo; ws.Cell(r, 3).Value = item.Description;
            ws.Range(r, 1, r, 10).Style.Font.Bold = true;
            ws.Range(r, 1, r, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#EAF4EA");
            r++;
            foreach (var l in lines)
            {
                ws.Cell(r, 1).Value = l.Period; ws.Cell(r, 3).Value = (l.Deduct ? "(−) " : "") + l.Description;
                if (l.Fixed is null)
                {
                    ws.Cell(r, 4).Value = l.Count;
                    if (l.Width is { } w) ws.Cell(r, 5).Value = w;
                    if (l.Length is { } b) ws.Cell(r, 6).Value = b;
                    if (l.Height is { } h) ws.Cell(r, 7).Value = h;
                }
                ws.Cell(r, 8).Value = l.Quantity; ws.Cell(r, 9).Value = item.Unit; ws.Cell(r, 10).Value = l.Source;
                r++;
            }
            ws.Cell(r, 3).Value = "Toplam"; ws.Cell(r, 8).Value = lines.Sum(l => l.Quantity);
            ws.Range(r, 3, r, 8).Style.Font.Bold = true;
            r += 2;
        }
        ws.Range(4, 4, r, 8).Style.NumberFormat.Format = QtyFmt;
        Widths(ws, 9, 14, 50, 9, 9, 9, 10, 13, 8, 18);

        // Fiyat farkı: one row per period up to this one
        ws = wb.AddWorksheet("Fiyat Farkı");
        Title(ws, "Fiyat Farkı Hesabı — F = An × B × (Pn − 1)", 8);
        Header(ws, 3, ["Hakediş", "Uygulama ayı", "An (bu dönem tutar)", "G0", "Gn", "Pn", "B", "F"]);
        r = 4;
        foreach (var p in job.Periods.Where(p => p.No <= periodNo).OrderBy(p => p.No))
        {
            var ps = Calculator.Period(job, p.No);
            ws.Cell(r, 1).Value = p.No; ws.Cell(r, 2).Value = p.Month; ws.Cell(r, 3).Value = ps.ThisAmount;
            ws.Cell(r, 4).Value = ps.G0; ws.Cell(r, 5).Value = ps.Gn; ws.Cell(r, 6).Value = ps.Pn;
            ws.Cell(r, 7).Value = job.B; ws.Cell(r, 8).Value = ps.PriceDiff;
            r++;
        }
        ws.Cell(r, 2).Value = "TOPLAM"; ws.Cell(r, 8).Value = s.CumPriceDiff; ws.Row(r).Style.Font.Bold = true;
        ws.Range(4, 3, r, 3).Style.NumberFormat.Format = MoneyFmt;
        ws.Range(4, 8, r, 8).Style.NumberFormat.Format = MoneyFmt;
        Widths(ws, 10, 14, 20, 12, 12, 10, 8, 18);

        wb.SaveAs(path);
    }

    internal static void Title(IXLWorksheet ws, string text, int cols)
    {
        ws.Cell(1, 1).Value = text;
        ws.Range(1, 1, 1, cols).Merge().Style.Font.SetBold().Font.SetFontSize(13);
    }

    internal static void Header(IXLWorksheet ws, int row, string[] cols)
    {
        for (int i = 0; i < cols.Length; i++) ws.Cell(row, i + 1).Value = cols[i];
        var range = ws.Range(row, 1, row, cols.Length);
        range.Style.Font.Bold = true;
        range.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE6F0");
        range.Style.Alignment.WrapText = true;
    }

    internal static void Widths(IXLWorksheet ws, params double[] w)
    {
        for (int i = 0; i < w.Length; i++) ws.Column(i + 1).Width = w[i];
    }
}
