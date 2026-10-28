using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace MuhendisKit.Core.Hakedis;

/// <summary>A word on a PDF page: left/right edge and baseline in points from the bottom-left corner.</summary>
public readonly record struct PdfWord(string Text, double Left, double Right, double Bottom, bool Bold);

/// <summary>
/// Reads the unit-price lists the ministry (ÇŞİDB Yüksek Fen Kurulu) publishes as PDF: the yearly book
/// "İnşaat ve Tesisat Birim Fiyatları" and the monthly "güncel" lists. Every page is a table
/// Poz No | Tanım | (Birim) | Birim Fiyat | (Montaj Bedeli), but the two differ:
/// - the book top-aligns each row; mechanical/electrical items have a bold title, then the specification (tarif) in
///   regular type, and their unit is only written in the text ("Ölçü: Ad."). Priced items under an unpriced group
///   poz (25.202.1100 → 25.202.1101) only name the variant ("(40.000 Kcal/h) 46 kW"), so the group title is prepended.
/// - the monthly lists have a unit column and centre each row's text vertically around the poz number.
/// </summary>
public static class CsbPriceList
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    static readonly Regex PozRx = new(@"^\d{2}\.\d{3}\.\d{4}$", RegexOptions.Compiled);
    static readonly Regex MoneyRx = new(@"^\d{1,3}(\.\d{3})*,\d{2}$", RegexOptions.Compiled);
    // "Ölçü: Ad.", "(Ölçü m2)", "ÖLÇÜ: (Adet, Birimi; …", "(birimi m²)"
    static readonly Regex UnitRx = new(@"\b(?:ölçü|birimi)(?![a-zçğıöşü])\s*:?\s*\(?\s*([a-zçğıöşü]+[²³23]?\.?)(?![a-zçğıöşü])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    const double LineTolerance = 2.5;
    const double PozMaxLeft = 75;      // poz numbers start the row (≈ 45–52 pt); ones further right are references in the text
    const double PriceMinLeft = 360;   // prices and units sit right of the description column (≈ 100–460 pt)
    const int MaxTitleLines = 4, MaxTitle = 400, MaxSpec = 2000, MaxGroupTitle = 140;

    public static List<CatalogPoz> Read(string pdfPath, string source)
    {
        using var pdf = PdfDocument.Open(pdfPath);
        return Parse(pdf.GetPages().Select(p => p.GetWords().Select(w => new PdfWord(w.Text,
            w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true))), source);
    }

    sealed record Line(double Y, PdfWord[] Words)
    {
        public string? Poz => Words.Length > 0 && Words[0].Left < PozMaxLeft && PozRx.IsMatch(Words[0].Text) ? Words[0].Text : null;
        public double Left => Words[0].Left;
    }

    sealed class Row
    {
        public required string Poz;
        public List<(double Y, string Text)> Title = [];
        public List<string> Spec = [];
        public bool Bold, InTitle = true;
        public decimal? Price, Mounting;
        public string? Unit;
    }

    sealed record Group(string Poz, string Stem, string Title, string? Unit);

    public static List<CatalogPoz> Parse(IEnumerable<IEnumerable<PdfWord>> pages, string source)
    {
        var result = new List<CatalogPoz>();
        var groups = new List<Group>();
        bool centred = false;
        double descX = 0;
        Row? open = null;

        foreach (var page in pages)
        {
            // The vertical "Sıra No" caption sits left of the poz column and would merge into the first row.
            var words = page.ToList();
            double pozX = words.Where(w => w.Left < PozMaxLeft && PozRx.IsMatch(w.Text)).Select(w => w.Left).DefaultIfEmpty(0).Min();
            var lines = Lines(words.Where(w => w.Left >= pozX - 1));
            // The column header starts at the left edge ("Poz No" ≈ 58–62 pt); "Poz No" also occurs inside notes.
            int header = lines.FindIndex(l => l.Words.Length > 1 && l.Words[0].Text == "Poz" && l.Words[0].Left < PozMaxLeft && l.Words[1].Text == "No");
            if (header >= 0)
            {
                // Header rows span ~±12 pt around "Poz No"; the monthly lists head their price columns "Güncel …".
                double hy = lines[header].Y;
                centred = lines.Any(l => Math.Abs(l.Y - hy) <= 20 && l.Words.Any(w => w.Text.StartsWith("Güncel")));
                lines = lines.Where(l => l.Y < hy - 12).ToList();
            }
            var rows = lines.Where(l => l.Poz is not null).ToList();
            if (rows.Count == 0 && open is null) continue;
            if (rows.Count > 0)
                descX = rows.Select(r => r.Words.Skip(1).FirstOrDefault(w => w.Left < PriceMinLeft).Left).Where(x => x > 0).DefaultIfEmpty(descX).Min();
            bool IsText(Line l) => descX > 0 && l.Left >= descX - 2 && l.Left <= descX + 6;

            if (centred)
            {
                // Rows are vertically centred: each text line belongs to the nearest poz line on the page.
                if (open is not null) { Emit(open, groups, result, source); open = null; }
                var built = rows.ToDictionary(r => r, r => Start(r));
                foreach (var l in lines.Where(l => l.Poz is null && IsText(l)))
                {
                    var near = rows.MinBy(r => Math.Abs(r.Y - l.Y));
                    if (near is null || Math.Abs(near.Y - l.Y) > 30) continue;
                    built[near].Title.Add((l.Y, Join(l.Words)));
                }
                foreach (var r in rows) Emit(built[r], groups, result, source);
                continue;
            }

            foreach (var l in lines)
            {
                if (l.Poz is not null)
                {
                    if (open is not null) Emit(open, groups, result, source);
                    open = Start(l);
                    continue;
                }
                if (open is null || !IsText(l)) continue;
                bool bold = l.Words.Count(w => w.Bold) * 2 > l.Words.Length;
                if (open.InTitle && (open.Title.Count == 0 || bold == open.Bold) && open.Title.Count < MaxTitleLines)
                {
                    if (open.Title.Count == 0) open.Bold = bold;
                    var (text, unit, price, mounting) = Cells(l.Words);
                    open.Title.Add((l.Y, text));
                    if (open.Price is null && price is not null) (open.Price, open.Mounting, open.Unit) = (price, mounting, unit);
                }
                else
                {
                    open.InTitle = false;
                    // Only bold-titled (mechanical/electrical) items are followed by their specification.
                    if (open.Bold) open.Spec.Add(Join(l.Words));
                }
            }
        }
        if (open is not null) Emit(open, groups, result, source);
        return result;
    }

    static Row Start(Line l)
    {
        var (text, unit, price, mounting) = Cells(l.Words[1..]);
        var row = new Row { Poz = l.Poz!, Unit = unit, Price = price, Mounting = mounting };
        if (text.Length > 0)
        {
            row.Title.Add((l.Y, text));
            row.Bold = l.Words[1..].Count(w => w.Bold) * 2 > l.Words.Length - 1;
        }
        return row;
    }

    /// <summary>
    /// Splits a line into description text and the right-hand columns: up to two prices, before them the unit and,
    /// in the material rayiç lists, the place of purchase ("Fabrikada", "Ocakta"), which is kept in the text.
    /// </summary>
    static (string Text, string? Unit, decimal? Price, decimal? Mounting) Cells(PdfWord[] words)
    {
        int end = words.Length;
        var prices = new List<decimal>();
        while (end > 0 && prices.Count < 2 && words[end - 1].Left >= PriceMinLeft && MoneyRx.IsMatch(words[end - 1].Text))
            prices.Insert(0, decimal.Parse(words[--end].Text, NumberStyles.Number, Tr));
        // A column word stands far right, well apart from the description before it.
        bool Column(int i) => i >= 0 && words[i].Left >= PriceMinLeft && words[i].Text.Length <= 14 && !MoneyRx.IsMatch(words[i].Text)
            && (i == 0 || words[i].Left - words[i - 1].Right > 15);
        string? unit = null, place = null;
        if (Column(end - 1) && Column(end - 2)) { place = words[end - 1].Text; unit = words[end - 2].Text; end -= 2; }
        else if (Column(end - 1) && words[end - 1].Text.Length <= 8) unit = words[--end].Text;
        string text = Join(words[..end]);
        if (place is not null) text = $"{text} ({place})";
        return (text, unit, prices.Count > 0 ? prices[0] : null, prices.Count > 1 ? prices[1] : null);
    }

    static void Emit(Row row, List<Group> groups, List<CatalogPoz> result, string source)
    {
        string title = Clip(Squash(string.Join(" ", row.Title.OrderByDescending(t => t.Y).Select(t => t.Text))), MaxTitle);
        string? spec = row.Spec.Count > 0 ? Clip(Squash(string.Join(" ", row.Spec)), MaxSpec) : null;
        string? textUnit = UnitIn(title) ?? UnitIn(spec);
        if (row.Price is null)
        {
            // Unpriced poz = group heading for the items below it; a new group closes the ones it is not part of.
            groups.RemoveAll(g => !row.Poz.StartsWith(g.Stem, StringComparison.Ordinal));
            groups.Add(new Group(row.Poz, Stem(row.Poz), title, textUnit));
            return;
        }
        var parents = groups.Where(g => g.Poz != row.Poz && row.Poz.StartsWith(g.Stem, StringComparison.Ordinal))
            .OrderByDescending(g => g.Stem.Length).ToList();
        string description = title;
        if (parents.FirstOrDefault() is { } parent && !Norm(title).Contains(Norm(Head(parent.Title))))
            description = (GroupTitle(parent.Title) + " " + title).Trim();
        string unit = row.Unit ?? textUnit ?? parents.Select(p => p.Unit).FirstOrDefault(u => u is not null) ?? "";
        result.Add(new CatalogPoz(row.Poz, description, NormUnit(unit), row.Price.Value, source, row.Mounting, spec));
    }

    static List<Line> Lines(IEnumerable<PdfWord> words)
    {
        var lines = new List<Line>();
        foreach (var g in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderByDescending(w => w.Bottom).ThenBy(w => w.Left)
            .Aggregate(new List<List<PdfWord>>(), (acc, w) =>
            {
                if (acc.Count > 0 && Math.Abs(acc[^1][0].Bottom - w.Bottom) <= LineTolerance) acc[^1].Add(w);
                else acc.Add([w]);
                return acc;
            }))
            lines.Add(new Line(g[0].Bottom, g.OrderBy(w => w.Left).ToArray()));
        return lines;
    }

    /// <summary>"25.470.1600" → "25.470.16": items 25.470.16xx belong to that group.</summary>
    static string Stem(string poz) => poz[..7] + poz[7..].TrimEnd('0');

    static string? UnitIn(string? text) => text is null ? null : UnitRx.Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The group heading without the boilerplate after it, short enough to prefix each item.</summary>
    static string GroupTitle(string title)
    {
        foreach (var cut in new[] { " Diğer özellik", " diğer özellik", "(Ölçü", " Ölçü:", " Ölçü :" })
            if (title.IndexOf(cut, StringComparison.Ordinal) is > 0 and var i) title = title[..i];
        return Clip(title.Trim(), MaxGroupTitle);
    }

    static string NormUnit(string unit)
    {
        unit = unit.TrimEnd('.');
        return unit.Length >= 2 && unit[^1] is '2' or '3' && char.IsLetter(unit[^2]) ? unit[..^1] + (unit[^1] == '2' ? "²" : "³") : unit;
    }

    static string Head(string title) => string.Join(" ", title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));

    static string Join(IEnumerable<PdfWord> words) => string.Join(" ", words.Select(w => w.Text));

    static string Squash(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    static string Clip(string s, int max)
    {
        if (s.Length <= max) return s;
        int cut = s.LastIndexOf(' ', max);
        return s[..(cut > max / 2 ? cut : max)] + "…";
    }

    static string Norm(string s) => s.ToLower(Tr);
}
