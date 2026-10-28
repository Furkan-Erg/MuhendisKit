using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MuhendisKit;

sealed partial class MainForm : Form
{
    const string AppHost = "app.example";
    const string AppOrigin = $"https://{AppHost}/";
    public const string CadHost = "cad.example";
    public const string FontHost = "fonts.example";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(246, 247, 249) };
    readonly string? startProject;
    readonly CdnCache cdnCache = new();

    public MainForm(string? startProject)
    {
        this.startProject = startProject;
        Text = "MühendisKit";
        Icon = LoadIcon();
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1400, 880);
        Controls.Add(web);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await InitWebViewAsync();
    }

    async Task InitWebViewAsync()
    {
        var env = await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataDir);
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2!;
#if DEBUG
        const bool dev = true;
#else
        const bool dev = false;
#endif
        core.Settings.AreDevToolsEnabled = dev;
        core.Settings.AreDefaultContextMenusEnabled = dev;
        core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.SetVirtualHostNameToFolderMapping(AppHost, AppPaths.WebRoot, CoreWebView2HostResourceAccessKind.DenyCors);
        // Converted drawings and the system fonts the viewer renders text with (read-only, never redistributed).
        core.SetVirtualHostNameToFolderMapping(CadHost, AppPaths.CadCacheDir, CoreWebView2HostResourceAccessKind.Allow);
        core.SetVirtualHostNameToFolderMapping(FontHost, AppPaths.FontsDir, CoreWebView2HostResourceAccessKind.Allow);
        // The UI is local files; a stale HTTP cache would show the previous version's CSS/JS after an update.
        await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        cdnCache.Attach(core);
        core.WebMessageReceived += OnWebMessage;
        UpdateTitleBar();
        web.Source = new Uri(AppOrigin + "index.html");
        StartAutomation();
    }

    async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(AppOrigin, StringComparison.Ordinal))
            return;

        JsonNode? msg = JsonNode.Parse(e.WebMessageAsJson);
        if (TryHandleAutomationMessage(msg))
            return;
        int id = msg?["id"]?.GetValue<int>() ?? 0;
        string cmd = msg?["cmd"]?.GetValue<string>() ?? "";
        JsonNode? args = msg?["args"];
        try
        {
            object? result = await HandleAsync(cmd, args);
            Post(new { id, ok = true, result });
        }
        catch (Exception ex)
        {
            Post(new { id, ok = false, error = ex is TargetInvocationException { InnerException: { } inner } ? inner.Message : ex.Message });
        }
    }

    void Post(object payload) =>
        web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload, Json));

    static T Arg<T>(JsonNode? args, string name)
    {
        var node = args?[name] ?? throw new ArgumentException($"Eksik parametre: {name}");
        return node.Deserialize<T>(Json) ?? throw new ArgumentException($"Eksik parametre: {name}");
    }

    /// <summary>Optional argument; default(T) (null for reference types) when absent.</summary>
    static T Opt<T>(JsonNode? args, string name) => args?[name] is { } n ? n.Deserialize<T>(Json)! : default!;

    static void OpenExternal(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    static void OpenPath(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    // Follow the Windows app theme for the native title bar.
    void UpdateTitleBar()
    {
        int dark = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0 ? 1 : 0;
        DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));   // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static Icon LoadIcon()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")!;
        return new Icon(s);
    }
}
