using System.Net;
using System.Text.RegularExpressions;

namespace MuhendisKit.Core.Hakedis;

/// <summary>A unit-price list on yfk.csb.gov.tr: the yearly book (Month 1) or a monthly update. Url is a PDF or a page of PDFs.</summary>
public sealed record CsbRelease(int Year, int Month, string Url)
{
    static readonly string[] Months = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
    public string Label => $"{Year} {Months[Month - 1]}";
    public string Source => $"ÇŞİDB {Label}";
    public static int MonthOf(string name) => Array.FindIndex(Months, m => string.Equals(m, name.Trim(), StringComparison.OrdinalIgnoreCase)) + 1;
}

/// <summary>
/// Downloads the ministry's (ÇŞİDB Yüksek Fen Kurulu) unit-price lists and reads them with <see cref="CsbPriceList"/>.
/// The site shows an access statement (Erişim Beyanı) before these pages; it is accepted for the session with the
/// statement's own form. Use of the lists in this program rests on the municipality's permission from the ministry.
/// </summary>
public static class CsbSite
{
    const string Base = "https://yfk.csb.gov.tr";
    const string MonthlyPage = Base + "/aylik-guncel-rayic-ve-birim-fiyat-listeleri-i-113351";
    const string BooksPage = Base + "/birim-fiyatlar-i-100468";
    // The new poz numbering (15.xxx, 25.xxx, 35.xxx) the parser knows started in 2019.
    const int FirstYear = 2019;

    static readonly Regex LinkRx = new(@"<a\s[^>]*?href=""(?<href>[^""]+)""[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex YearHeadingRx = new(@"\b(?<year>20\d\d)\s+(?:YILI|EYL)", RegexOptions.Compiled);   // "2026 YILINA AİT …", "2025 EYLÜL AYINDAN …"
    static readonly Regex BookRowRx = new(@"<td>\s*(?<year>20\d\d)\s*</td>\s*<td>\s*<a\s+href=""(?<href>[^""]+\.pdf)""", RegexOptions.Compiled);

    public static HttpClient CreateClient()
    {
        var http = new HttpClient(new HttpClientHandler { CookieContainer = new CookieContainer(), AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) MuhendisKit");
        return http;
    }

    /// <summary>The lists on the site, newest first: monthly updates and the yearly books since 2019.</summary>
    public static async Task<List<CsbRelease>> ListAsync(HttpClient http, CancellationToken ct = default)
    {
        var found = new Dictionary<(int, int), CsbRelease>();
        // Monthly page: "2026 YILINA AİT GÜNCEL AYLIK …" then one link per month (January = the yearly book's PDF).
        string monthly = Content(await GetPageAsync(http, MonthlyPage, ct));
        int year = 0, at = 0;
        foreach (Match link in LinkRx.Matches(monthly))
        {
            if (YearHeadingRx.Matches(monthly[at..link.Index]).LastOrDefault() is { } heading) year = int.Parse(heading.Groups["year"].Value);
            at = link.Index + link.Length;
            int month = CsbRelease.MonthOf(Text(link.Groups["text"].Value));
            if (year >= FirstYear && month > 0) found.TryAdd((year, month), new CsbRelease(year, month, Absolute(link.Groups["href"].Value)));
        }
        // Yearly books table: | YILI | TÜRKÇE | … |
        foreach (Match row in BookRowRx.Matches(Content(await GetPageAsync(http, BooksPage, ct))))
            if (int.Parse(row.Groups["year"].Value) is var y && y >= FirstYear)
                found.TryAdd((y, 1), new CsbRelease(y, 1, Absolute(row.Groups["href"].Value)));
        if (found.Count == 0)
            throw new InvalidDataException("Bakanlık sayfasında birim fiyat listesi bulunamadı; sayfa yapısı değişmiş olabilir.");
        return found.Values.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToList();
    }

    /// <summary>
    /// The PDFs of a release. A monthly page has one per discipline: inşaat / mekanik / elektrik birim fiyatları and
    /// their rayiç lists (file names with "ray"), which are only fetched when asked for.
    /// </summary>
    public static async Task<List<string>> PdfUrlsAsync(HttpClient http, CsbRelease release, bool includeRayic, CancellationToken ct = default)
    {
        if (release.Url.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return [release.Url];
        var all = LinkRx.Matches(Content(await GetPageAsync(http, release.Url, ct)))
            .Select(m => Absolute(m.Groups["href"].Value))
            .Where(u => u.StartsWith("https://webdosya.csb.gov.tr/", StringComparison.OrdinalIgnoreCase) && u.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                && !u.Contains("/menu/", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(u).StartsWith("4735-", StringComparison.Ordinal))
            .Distinct().ToList();
        if (all.Count == 0) throw new InvalidDataException($"{release.Label} sayfasında PDF bulunamadı.");
        var prices = all.Where(u => !Path.GetFileName(u).Contains("ray", StringComparison.OrdinalIgnoreCase)).ToList();
        return includeRayic || prices.Count == 0 ? all : prices;
    }

    /// <summary>Downloads into <paramref name="cacheDir"/> once; the ministry gives every upload a new file name.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, string url, string cacheDir, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        string path = Path.Combine(cacheDir, Path.GetFileName(new Uri(url).LocalPath));
        if (File.Exists(path) && new FileInfo(path).Length > 0) return path;
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        string tmp = path + ".part";
        await using (var file = File.Create(tmp))
            await res.Content.CopyToAsync(file, ct);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    /// <summary>
    /// Reads every PDF of the release into catalogue items under <see cref="CsbRelease.Source"/>. Monthly lists only
    /// print each item's own line ("800 mm. genişliğinde"), so the description and specification come from the
    /// same year's book where its text contains the monthly one; units and prices always come from the release.
    /// </summary>
    public static async Task<List<CatalogPoz>> ReadReleaseAsync(HttpClient http, CsbRelease release, CsbRelease? book, bool includeRayic,
        string cacheDir, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var items = new List<CatalogPoz>();
        foreach (string url in await PdfUrlsAsync(http, release, includeRayic, ct))
        {
            progress?.Report($"İndiriliyor: {Path.GetFileName(url)}");
            string pdf = await DownloadAsync(http, url, cacheDir, ct);
            progress?.Report($"Okunuyor: {Path.GetFileName(url)}");
            items.AddRange(await Task.Run(() => CsbPriceList.Read(pdf, release.Source), ct));
        }
        items = items.DistinctBy(i => i.PozNo).ToList();
        if (book is not null && book != release)
        {
            var bookItems = new Dictionary<string, CatalogPoz>();
            foreach (string url in await PdfUrlsAsync(http, book, true, ct))
            {
                progress?.Report($"Tanımlar için yıllık kitap: {Path.GetFileName(url)}");
                string pdf = await DownloadAsync(http, url, cacheDir, ct);
                foreach (var b in await Task.Run(() => CsbPriceList.Read(pdf, book.Source), ct)) bookItems.TryAdd(b.PozNo, b);
            }
            items = items.Select(i => Enrich(i, bookItems.GetValueOrDefault(i.PozNo))).ToList();
        }
        return items;
    }

    public static CatalogPoz Enrich(CatalogPoz item, CatalogPoz? fromBook)
    {
        if (fromBook is null) return item;
        bool same = fromBook.Description.Contains(item.Description, StringComparison.CurrentCultureIgnoreCase);
        return item with
        {
            Description = same ? fromBook.Description : item.Description,
            Unit = item.Unit.Length > 0 ? item.Unit : fromBook.Unit,
            Spec = item.Spec ?? (same ? fromBook.Spec : null),
        };
    }

    /// <summary>
    /// GET a page; when the access statement comes back instead, accept it with its form. The acceptance redirects to
    /// the page once (the next request shows the statement again), so the redirected response is the page.
    /// </summary>
    static async Task<string> GetPageAsync(HttpClient http, string url, CancellationToken ct)
    {
        string html = await http.GetStringAsync(url, ct);
        var form = Regex.Match(html, @"<form[^>]+action=""(?<action>/erisim-beyani/kabul)""[^>]*>(?<body>.*?)</form>", RegexOptions.Singleline);
        if (!form.Success) return html;
        var fields = Regex.Matches(form.Groups["body"].Value, @"<input[^>]+name=""(?<n>[^""]+)""[^>]+value=""(?<v>[^""]*)""")
            .ToDictionary(m => m.Groups["n"].Value, m => WebUtility.HtmlDecode(m.Groups["v"].Value));
        using var res = await http.PostAsync(Base + form.Groups["action"].Value, new FormUrlEncodedContent(fields), ct);
        res.EnsureSuccessStatusCode();
        html = await res.Content.ReadAsStringAsync(ct);
        if (html.Contains("/erisim-beyani/kabul", StringComparison.Ordinal))
            throw new HttpRequestException("Bakanlık sitesinin erişim beyanı geçilemedi.");
        return html;
    }

    /// <summary>The page's own content block (menus and footers link to other lists and to the law's PDF).</summary>
    static string Content(string html)
    {
        int i = html.IndexOf("class=\"page_content\"", StringComparison.Ordinal);
        if (i < 0) i = html.IndexOf("custom-content", StringComparison.Ordinal);   // monthly pages
        if (i < 0) return html;
        int end = html.IndexOf("<footer", i, StringComparison.OrdinalIgnoreCase);
        return end > i ? html[i..end] : html[i..];
    }

    static string Text(string html) => WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")).Trim();

    static string Absolute(string href) =>
        href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href.Replace("http://", "https://") : Base + "/" + href.TrimStart('/');
}
