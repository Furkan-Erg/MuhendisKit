using Microsoft.Web.WebView2.Core;

namespace MuhendisKit;

/// <summary>
/// Serves the CAD editor's fonts and templates (mlightcad/cad-data on jsDelivr) from a local copy,
/// downloading each file once. Drawings then open offline and don't wait on the CDN.
/// </summary>
sealed class CdnCache
{
    public const string Prefix = "https://cdn.jsdelivr.net/gh/mlightcad/cad-data@main/";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly string root = Path.Combine(AppPaths.LocalDir, "cad-data");

    public void Attach(CoreWebView2 core)
    {
        core.AddWebResourceRequestedFilter(Prefix + "*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        core.WebResourceRequested += async (_, e) =>
        {
            if (!e.Request.Uri.StartsWith(Prefix, StringComparison.Ordinal) || e.Request.Method != "GET") return;
            using var deferral = e.GetDeferral();
            try
            {
                string? file = await GetAsync(e.Request.Uri);
                e.Response = file is null
                    ? core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Access-Control-Allow-Origin: *")
                    : core.Environment.CreateWebResourceResponse(Open(file), 200, "OK",
                        $"Content-Type: {ContentType(file)}\nAccess-Control-Allow-Origin: *");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                e.Response = core.Environment.CreateWebResourceResponse(null, 502, "Bad Gateway", "Access-Control-Allow-Origin: *");
            }
        };
    }

    async Task<string?> GetAsync(string uri)
    {
        string rel = Uri.UnescapeDataString(new Uri(uri).AbsolutePath["/gh/mlightcad/cad-data@main/".Length..]);
        if (rel.Contains("..")) return null;
        string path = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        if (File.Exists(path)) return path;

        using var res = await Http.GetAsync(uri);
        if (!res.IsSuccessStatusCode) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".part";
        await using (var fs = File.Create(tmp)) await res.Content.CopyToAsync(fs);
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    // The upstream new-drawing template names its paper-space layouts in Chinese; new drawings get Turkish tab names.
    static Stream Open(string file)
    {
        if (!file.EndsWith("acadiso.dxf", StringComparison.OrdinalIgnoreCase)) return File.OpenRead(file);
        string dxf = File.ReadAllText(file).Replace("布局1", "Düzen1").Replace("布局2", "Düzen2");
        return new MemoryStream(new System.Text.UTF8Encoding(false).GetBytes(dxf));
    }

    static string ContentType(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".json" => "application/json",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".dxf" or ".shx" => "application/octet-stream",
        _ => "application/octet-stream",
    };
}
