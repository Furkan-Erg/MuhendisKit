using System.Globalization;
using System.Text.Json;

namespace MuhendisKit.Core.Hakedis;

/// <summary>Monthly index values from the Central Bank's EVDS web service (free API key from evds3.tcmb.gov.tr).</summary>
public static class Evds
{
    // "1.Yurt İçi Üretici Fiyat Endeksi" (Yİ-ÜFE genel, source TÜİK), confirmed with serieList on 2026-10-28.
    public const string DefaultSeries = "TP.TUFE1YI.T1";
    // evds2.tcmb.gov.tr now redirects to the EVDS3 site; the web service moved to this path.
    const string BaseUrl = "https://evds3.tcmb.gov.tr/igmevdsms-dis/";

    public static async Task<Dictionary<string, decimal>> FetchMonthlyAsync(HttpClient http, string apiKey, string series,
        int fromYear, int toYear, CancellationToken ct = default)
    {
        string url = $"{BaseUrl}series={Uri.EscapeDataString(series)}&startDate=01-01-{fromYear}&endDate=31-12-{toYear}&type=json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("key", apiKey);
        using var res = await http.SendAsync(req, ct);
        string body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"EVDS {(int)res.StatusCode}: anahtarı ve seri kodunu kontrol edin.");
        // A rejected key or a moved endpoint comes back as the web site's HTML, not as an error status.
        if (!body.TrimStart().StartsWith('{'))
            throw new HttpRequestException("EVDS beklenen veriyi döndürmedi: API anahtarını kontrol edin.");
        return Parse(body, series);
    }

    /// <summary>EVDS returns {"items":[{"Tarih":"2025-3","TP_TUFE1YI_T1":"3861.33"},…]} (dots in the code become underscores).</summary>
    public static Dictionary<string, decimal> Parse(string json, string series)
    {
        string field = series.Replace('.', '_');
        var result = new Dictionary<string, decimal>();
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("Tarih", out var t) || !item.TryGetProperty(field, out var v)) continue;
            string? raw = v.ValueKind == JsonValueKind.Number ? v.GetRawText() : v.GetString();
            if (raw is null || !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
            var parts = (t.GetString() ?? "").Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int y) && int.TryParse(parts[1], out int m))
                result[$"{y:0000}-{m:00}"] = value;
        }
        return result;
    }
}
