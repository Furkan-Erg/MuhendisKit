using System.Text.Json;
using System.Text.Json.Nodes;

namespace MuhendisKit;

/// <summary>Project files (.mkproj) are plain JSON owned by the UI; the host only reads names and dates.</summary>
static class Projects
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public sealed record Entry(string Path, string Name, DateTime Modified);

    public static Entry[] List(string[] extra)
    {
        var files = Directory.EnumerateFiles(AppPaths.ProjectsDir, "*.mkproj")
            .Concat(extra.Where(File.Exists))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return files.Select(f =>
        {
            string name = Path.GetFileNameWithoutExtension(f);
            try
            {
                using var s = File.OpenRead(f);
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } nm) name = nm;
            }
            catch (Exception e) when (e is IOException or JsonException) { }
            return new Entry(f, name, File.GetLastWriteTime(f));
        }).OrderByDescending(p => p.Modified).ToArray();
    }

    public static object Load(string path) =>
        new { path, data = JsonNode.Parse(File.ReadAllText(path)) };

    /// <summary>Saves through a temp file so a crash mid-write never leaves a half-written project.</summary>
    public static string Save(string? path, JsonObject data)
    {
        path ??= UniquePath(SafeName(data["name"]?.GetValue<string>() ?? "Proje"));
        data["modified"] = DateTime.Now.ToString("s");
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, data.ToJsonString(Indented));
        File.Move(tmp, path, overwrite: true);
        return path;
    }

    public static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        string s = new string(name.Select(c => bad.Contains(c) ? '-' : c).ToArray()).Trim();
        return s.Length == 0 ? "Proje" : s;
    }

    static string UniquePath(string name)
    {
        string p = Path.Combine(AppPaths.ProjectsDir, name + ".mkproj");
        for (int i = 2; File.Exists(p); i++)
            p = Path.Combine(AppPaths.ProjectsDir, $"{name} ({i}).mkproj");
        return p;
    }
}
