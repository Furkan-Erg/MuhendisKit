using System.Globalization;
using System.Text.RegularExpressions;

namespace MuhendisKit.Core.Hakedis;

/// <param name="Kind">length (m), area (m²) or count (adet)</param>
/// <param name="Name">layer name, or block name for counts</param>
public sealed record TakeoffRow(string Key, string Kind, string Name, string? Layer, double Quantity);

/// <param name="Reason">saved (earlier choice in this project), name (words of the layer/block in the description) or null</param>
public sealed record TakeoffSuggestion(string Key, string? ItemId, string? Reason);

/// <summary>Matches drawing quantities (layer lengths, closed areas, block counts) to the pozes of an estimate or a job.</summary>
public static class Takeoff
{
    static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Whether a poz unit can take the quantity: lengths go to m, areas to m², counts to adet/takım.</summary>
    public static bool UnitFits(string kind, string unit)
    {
        string u = unit.Trim().TrimEnd('.').ToLower(Tr);
        return kind switch
        {
            "length" => u is "m" or "mt" or "metre" or "mtül" or "m.tül",
            "area" => u is "m²" or "m2" or "metrekare",
            "count" => u is "ad" or "adet" or "takım" or "tk" or "set" or "tane",
            _ => false,
        };
    }

    // CAD layer names are usually ASCII capitals ("KOMBI", "PPR_BORULAR"), which Turkish lower-casing turns into "kombı":
    // fold both sides to plain letters before comparing.
    static string Fold(string s) => s.ToLower(Tr).Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c');

    static HashSet<string> Words(string s) =>
        Regex.Split(Fold(s), @"[^\p{L}\d]+").Where(w => w.Length >= 3 && !w.All(char.IsDigit)).Select(Stem).ToHashSet();

    // Same suffix trimming as the catalogue search, so "boruları" meets "boru".
    static string Stem(string w)
    {
        foreach (string s in new[] { "lari", "leri", "lar", "ler", "si", "su", "i", "u" })   // folded forms
            if (w.Length - s.Length >= 4 && w.EndsWith(s, StringComparison.Ordinal)) return w[..^s.Length];
        return w;
    }

    /// <param name="saved">key → poz number chosen earlier in this project</param>
    public static TakeoffSuggestion[] Suggest(IEnumerable<TakeoffRow> rows, IReadOnlyList<PozItem> items, IReadOnlyDictionary<string, string>? saved = null)
    {
        return rows.Select(r =>
        {
            if (saved?.GetValueOrDefault(r.Key) is { } pozNo && items.FirstOrDefault(i => i.PozNo == pozNo) is { } kept)
                return new TakeoffSuggestion(r.Key, kept.Id, "saved");
            var words = Words(r.Name + " " + r.Layer);
            var best = items.Where(i => UnitFits(r.Kind, i.Unit))
                .Select(i => (Item: i, Score: Words(i.Description).Count(w => words.Any(x => x.StartsWith(w, StringComparison.Ordinal) || w.StartsWith(x, StringComparison.Ordinal)))))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            return best.Item is null ? new TakeoffSuggestion(r.Key, null, null) : new TakeoffSuggestion(r.Key, best.Item.Id, "name");
        }).ToArray();
    }
}
