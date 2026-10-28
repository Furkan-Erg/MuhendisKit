using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;

namespace MuhendisKit.Core.Hakedis;

/// <param name="MountingPrice">montaj bedeli, for mechanical/electrical items (also used for demontaj)</param>
/// <param name="Spec">the item's specification text (tarif) where the list prints it</param>
public sealed record CatalogPoz(string PozNo, string Description, string Unit, decimal UnitPrice, string Source,
    decimal? MountingPrice = null, string? Spec = null);

/// <summary>
/// Unit-price catalogue imported from the ministry's yearly list (or any institution's) as Excel/CSV.
/// The column layout differs between years and publishers, so the header row is detected by its titles.
/// </summary>
public sealed class PozCatalog
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    public List<CatalogPoz> Items { get; private set; } = [];

    public static PozCatalog Load(string path)
    {
        var c = new PozCatalog();
        if (File.Exists(path))
            c.Items = JsonSerializer.Deserialize<List<CatalogPoz>>(File.ReadAllText(path)) ?? [];
        return c;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Items));

    /// <summary>Adds the file's rows, replacing earlier rows from the same source name. Returns how many were read.</summary>
    public int Import(string file, string source)
    {
        var rows = Path.GetExtension(file).ToLowerInvariant() switch
        {
            ".csv" or ".txt" => ReadCsv(file),
            _ => ReadExcel(file),
        };
        var parsed = Parse(rows, source);
        if (parsed.Count == 0)
            throw new InvalidDataException("Dosyada \"Poz No\" ve \"Birim Fiyat\" başlıklı sütunlar bulunamadı.");
        Replace(source, parsed);
        return parsed.Count;
    }

    /// <summary>Replaces all rows of <paramref name="source"/>; the newest source is listed first.</summary>
    public void Replace(string source, IEnumerable<CatalogPoz> items)
    {
        Items.RemoveAll(i => i.Source == source);
        Items.InsertRange(0, items);
    }

    /// <summary>
    /// Every word must occur in the poz number or description. A query that starts like a poz number ("25.130")
    /// lists those pozes first. <paramref name="source"/> limits the search to one imported list.
    /// </summary>
    public IEnumerable<CatalogPoz> Search(string query, int max = 50, string? source = null)
    {
        var pool = source is null ? Items : Items.Where(i => i.Source == source);
        if (string.IsNullOrWhiteSpace(query)) return pool.Take(max);
        string q = query.Trim();
        var words = Norm(q).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Stem).ToArray();
        return pool.Where(i =>
        {
            string hay = Norm(i.PozNo + " " + i.Description);
            return words.All(hay.Contains);
        }).OrderBy(i => i.PozNo.StartsWith(q, StringComparison.Ordinal) ? 0 : 1).Take(max);
    }

    static string Norm(string s) => s.ToLower(Tr);

    // Descriptions inflect nouns ("Kalorifer Kazanları", "boruların"): a query word matches by its stem, so
    // "kazanı" finds "kazanları" and "borusu" finds "boru".
    static readonly string[] Suffixes = ["ları", "leri", "lar", "ler", "sı", "si", "su", "sü", "ı", "i", "u", "ü"];

    static string Stem(string word)
    {
        foreach (string s in Suffixes)
            if (word.Length - s.Length >= 4 && word.EndsWith(s, StringComparison.Ordinal)) return word[..^s.Length];
        return word;
    }

    static List<string[]> ReadExcel(string file)
    {
        using var wb = new XLWorkbook(file);
        var all = new List<string[]>();
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed();
            if (used is null) continue;
            int cols = used.LastColumn().ColumnNumber();
            foreach (var row in used.Rows())
                all.Add(Enumerable.Range(1, cols).Select(c => row.Cell(c).GetFormattedString().Trim()).ToArray());
            all.Add([]);   // sheet boundary: header detection restarts
        }
        return all;
    }

    static List<string[]> ReadCsv(string file)
    {
        var lines = File.ReadAllLines(file);
        char sep = lines.Take(20).Sum(l => l.Count(ch => ch == ';')) >= lines.Take(20).Sum(l => l.Count(ch => ch == ',')) ? ';' : ',';
        return lines.Select(l => l.Split(sep).Select(c => c.Trim().Trim('"')).ToArray()).ToList();
    }

    public static List<CatalogPoz> Parse(List<string[]> rows, string source)
    {
        var result = new List<CatalogPoz>();
        int poz = -1, desc = -1, unit = -1, price = -1;
        foreach (var row in rows)
        {
            if (row.Length == 0) { poz = -1; continue; }
            var lower = row.Select(Norm).ToArray();
            int Find(params string[] keys) => Array.FindIndex(lower, h => keys.Any(h.Contains));
            if (Find("poz") is >= 0 and var p && Find("fiyat", "tutar") is >= 0 and var f && row.Count(c => c.Length > 0) >= 3)
            {
                poz = p; price = f;
                desc = Find("tanım", "tarif", "açıklama", "imalat", "iş kalemi", "cinsi");
                unit = Find("birimi", "ölçü", "birim");
                if (unit == price) unit = Array.FindIndex(lower, h => h.Contains("birim") && !h.Contains("fiyat"));
                continue;
            }
            if (poz < 0 || poz >= row.Length || price >= row.Length) continue;
            string no = row[poz];
            if (no.Length == 0 || !TryMoney(row[price], out var value)) continue;
            result.Add(new CatalogPoz(no, desc >= 0 && desc < row.Length ? row[desc] : "",
                unit >= 0 && unit < row.Length ? row[unit] : "", value, source));
        }
        return result;
    }

    /// <summary>Accepts Turkish ("1.234,56") and invariant ("1234.56") number formats.</summary>
    public static bool TryMoney(string s, out decimal value)
    {
        s = s.Replace("TL", "").Replace("₺", "").Replace(" ", "").Trim();
        if (s.Contains(','))
            return decimal.TryParse(s, NumberStyles.Number, Tr, out value);
        return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
