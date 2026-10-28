using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MuhendisKit;

/// <summary>Registers MuhendisKit.Mcp.exe with Claude Desktop and Claude Code on this PC.</summary>
static class ClaudeSetup
{
    const string Name = "muhendiskit";
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ServerPath => Path.Combine(AppPaths.AppDir, "MuhendisKit.Mcp.exe");

    /// <summary>
    /// Claude Desktop config files Claude actually reads. The Store/MSIX build sees %APPDATA% through its package's
    /// virtualised folder (Packages\Claude_*\LocalCache\Roaming), so a file in the real %APPDATA%\Claude is ignored there.
    /// </summary>
    public static List<string> DesktopConfigs()
    {
        var found = new List<string>();
        string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packages))
            foreach (var pkg in Directory.EnumerateDirectories(packages, "Claude_*"))
            {
                string dir = Path.Combine(pkg, "LocalCache", "Roaming", "Claude");
                if (Directory.Exists(dir)) found.Add(Path.Combine(dir, "claude_desktop_config.json"));
            }
        // Classic installer (Squirrel) build.
        string classic = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AnthropicClaude");
        if (found.Count == 0 || Directory.Exists(classic))
            found.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json"));
        return found;
    }

    /// <summary>Claude Desktop is installed (Store/MSIX package or the classic installer).</summary>
    public static bool DesktopInstalled()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string packages = Path.Combine(local, "Packages");
        return Directory.Exists(Path.Combine(local, "AnthropicClaude"))
            || (Directory.Exists(packages) && Directory.EnumerateDirectories(packages, "Claude_*").Any());
    }

    /// <summary>
    /// Setup's "Claude'a bağla" task (MuhendisKit.exe --register-claude): registers with whichever Claude apps
    /// this PC has, silently. Never creates Claude config for an app that isn't installed.
    /// </summary>
    public static async Task RegisterInstalledAsync()
    {
        if (DesktopInstalled()) ConfigureDesktop();
        try { await ConfigureCodeAsync(); }
        catch (InvalidOperationException) { }   // Claude Code not installed
    }

    static string? RegisteredIn(string configFile)
    {
        if (!File.Exists(configFile)) return null;
        try { return JsonNode.Parse(File.ReadAllText(configFile))?["mcpServers"]?[Name]?["command"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }

    public static bool DesktopConfigured() =>
        DesktopConfigs().Any(f => string.Equals(RegisteredIn(f), ServerPath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds or updates only our entry in each config; every other setting is kept. Returns the files written.</summary>
    public static List<string> ConfigureDesktop()
    {
        var written = new List<string>();
        foreach (var file in DesktopConfigs())
        {
            JsonObject config = File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file))?.AsObject() ?? [] : [];
            if (File.Exists(file)) File.Copy(file, file + ".bak", overwrite: true);
            var servers = config["mcpServers"] as JsonObject ?? [];
            servers[Name] = new JsonObject { ["command"] = ServerPath, ["args"] = new JsonArray() };
            config["mcpServers"] = servers;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, config.ToJsonString(Indented));
            written.Add(file);
        }
        return written;
    }

    // ---- Claude Code: user-scope servers live in ~/.claude.json; changes go through the claude CLI ----

    static string CodeConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    public static bool CodeConfigured() =>
        string.Equals(RegisteredIn(CodeConfig), ServerPath, StringComparison.OrdinalIgnoreCase);

    public static async Task<string> ConfigureCodeAsync()
    {
        // Re-add so a stale path (app moved or reinstalled elsewhere) is replaced.
        await RunClaudeAsync($"mcp remove --scope user {Name}");
        var (code, output) = await RunClaudeAsync($"mcp add --scope user {Name} -- \"{ServerPath}\"");
        if (code != 0)
            throw new InvalidOperationException(code == 9009 || output.Contains("is not recognized")
                ? "Claude Code bu bilgisayarda bulunamadı (claude komutu yok)."
                : "Claude Code kaydı başarısız: " + output.Trim());
        return output.Trim();
    }

    // Through cmd.exe so both claude.exe (native installer) and claude.cmd (npm) are found on PATH.
    static async Task<(int, string)> RunClaudeAsync(string args)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/d /c claude {args}")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return (p.ExitCode, await stdout + await stderr);
    }
}
