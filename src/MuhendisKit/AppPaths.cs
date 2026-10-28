namespace MuhendisKit;

static class AppPaths
{
    public static string AppDir => AppContext.BaseDirectory;
    public static string WebRoot => Path.Combine(AppDir, "wwwroot");

    public static string RoamingDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuhendisKit"));

    public static string LocalDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MuhendisKit"));

    /// <summary>Projects live in the user's Documents so they're backed up and easy to send.</summary>
    public static string ProjectsDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MühendisKit", "Projeler"));

    public static string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public static string CatalogFile => Path.Combine(RoamingDir, "poz-katalogu.json");
    public static string WebViewDataDir => Path.Combine(LocalDir, "WebView2");
    /// <summary>DXF copies of opened drawings, served to the viewer through a virtual host.</summary>
    public static string CadCacheDir => Ensure(Path.Combine(LocalDir, "cad"));
    /// <summary>Unit-price PDFs downloaded from the ministry, kept so a re-import needs no download.</summary>
    public static string CsbCacheDir => Ensure(Path.Combine(LocalDir, "csb"));
    public static string FontsDir => Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
