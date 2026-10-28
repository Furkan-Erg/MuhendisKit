using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using MuhendisKit.Core.Cad;
using MuhendisKit.Core.Calc;
using MuhendisKit.Core.Hakedis;
using MuhendisKit.Core.Insulation;

namespace MuhendisKit;

sealed partial class MainForm
{
    const string ProjectFilter = "MühendisKit projesi (*.mkproj)|*.mkproj";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    PozCatalog? catalog;
    PozCatalog Catalog => catalog ??= PozCatalog.Load(AppPaths.CatalogFile);

    async Task<object?> HandleAsync(string cmd, JsonNode? args)
    {
        switch (cmd)
        {
            // ---- app ----
            case "app.info":
                return new
                {
                    version = Application.ProductVersion.Split('+')[0],
                    projectsDir = AppPaths.ProjectsDir,
                    dataDir = AppPaths.RoamingDir,
                    startProject,
                };
            case "settings.load":
                return File.Exists(AppPaths.SettingsFile) ? JsonNode.Parse(File.ReadAllText(AppPaths.SettingsFile)) : new JsonObject();
            case "settings.save":
                File.WriteAllText(AppPaths.SettingsFile, args!["data"]!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                return null;
            case "shell.open":
                OpenPath(Arg<string>(args, "path"));
                return null;
            case "shell.reveal":
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{Arg<string>(args, "path")}\"");
                return null;
            case "url.open":
                OpenExternal(Arg<string>(args, "url"));
                return null;

            // ---- projects ----
            case "projects.list":
                return Projects.List(Opt<string[]?>(args, "extra") ?? []);
            case "project.load":
                return Projects.Load(Arg<string>(args, "path"));
            case "project.save":
                return Projects.Save(Opt<string?>(args, "path"), args!["data"]!.AsObject());
            case "project.openDialog":
            {
                using var dlg = new OpenFileDialog { Filter = ProjectFilter, InitialDirectory = AppPaths.ProjectsDir };
                return dlg.ShowDialog(this) == DialogResult.OK ? Projects.Load(dlg.FileName) : null;
            }
            case "project.saveAs":
            {
                var data = args!["data"]!.AsObject();
                using var dlg = new SaveFileDialog
                {
                    Filter = ProjectFilter, InitialDirectory = AppPaths.ProjectsDir,
                    FileName = Projects.SafeName(data["name"]?.GetValue<string>() ?? "Proje") + ".mkproj",
                };
                return dlg.ShowDialog(this) == DialogResult.OK ? Projects.Save(dlg.FileName, data) : null;
            }
            case "project.delete":
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(Arg<string>(args, "path"),
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return null;

            // ---- calculators ----
            case "calc.pipe":
                return PipeSizing.Size(Arg<PipeSizingInput>(args, "input"));
            case "calc.pipeFlow":
                return PipeSizing.FlowFromHeat(Arg<double>(args, "kw"), Arg<double>(args, "dt"), Arg<double>(args, "temp"));
            case "calc.pipeCatalog":
                return PipeCatalog.All;
            case "calc.duct":
                return Duct.Size(Arg<DuctInput>(args, "input"));
            case "calc.psychro":
            {
                double t = Arg<double>(args, "t"), v = Arg<double>(args, "value");
                double p = Psychro.PressureAtAltitude(Opt<double>(args, "altitude"));
                return Arg<string>(args, "mode") switch
                {
                    "wetbulb" => Psychro.FromWetBulb(t, v, p),
                    "dewpoint" => Psychro.FromDewPoint(t, v, p),
                    _ => Psychro.FromRh(t, v / 100, p),
                };
            }
            case "calc.tank":
                return ExpansionTank.Size(Arg<ExpansionTankInput>(args, "input"));
            case "calc.gas":
                return GasPipe.Size(Arg<GasPipeInput>(args, "input"));
            case "calc.heatloss":
                return Arg<HeatLossRoom[]>(args, "rooms").Select(HeatLoss.Room).ToArray();
            case "calc.voltageDrop":
                return VoltageDrop.Size(Arg<VoltageDropInput>(args, "input"));
            case "calc.power":
                return Power.Compute(Arg<PowerInput>(args, "input"));
            case "calc.compensation":
                return Compensation.Size(Arg<CompensationInput>(args, "input"));
            case "calc.fireWater":
                return FireWater.Size(Arg<FireWaterInput>(args, "input"));
            case "calc.rainTables":
                return new { areaTypes = RainRegulation.AreaTypes, materials = RainRegulation.Materials };
            case "calc.rain":
                return RainDrainage.Size(Arg<RainDrainageInput>(args, "input"));
            case "calc.radiator":
                return Radiator.Size(Arg<RadiatorInput>(args, "input"));
            case "calc.booster":
                return Booster.Size(Arg<BoosterInput>(args, "input"));
            case "calc.cities":
                return new { source = Cities.Source, cities = Cities.All };
            case "calc.gravityPipes":
                return GravityPipes.Size(Arg<bool>(args, "rain"), Arg<int>(args, "slopeA"), Arg<double[]>(args, "values"));
            case "calc.chimneyFuels":
                return PracticalChimney.Coefficients;
            case "calc.chimney":
                return PracticalChimney.Size(Arg<ChimneyInput>(args, "input"));
            case "calc.fixtures":
                return WasteFixtureUnits.Fixtures;
            case "calc.fixtureUnits":
                return WasteFixtureUnits.Size(Arg<bool>(args, "horizontal"), Opt<double>(args, "roughness"), Opt<int>(args, "slopeA"), Arg<double[]>(args, "values"));

            // ---- insulation ----
            case "ins.evaluate":
            {
                var e = Arg<Element>(args, "element");
                var c = Opt<Climate?>(args, "climate");
                GlaserResult? glaser = null;
                string? glaserError = null;
                if (c is not null && e.FixedU is null && e.Layers.Length > 0)
                {
                    try { glaser = Envelope.Glaser(e, c); }
                    catch (ArgumentException ex) { glaserError = ex.Message; }
                }
                double? targetU = Opt<double?>(args, "targetU");
                int? insIndex = Opt<int?>(args, "insulationLayer");
                double? required = targetU is > 0 && insIndex is { } i && i >= 0 && i < e.Layers.Length && e.FixedU is null
                    ? Envelope.RequiredThickness(e, i, targetU.Value) : null;
                return new { u = e.U, rTotal = e.FixedU is null ? e.RTotal : (double?)null, glaser, glaserError, required };
            }

            // ---- hakediş ----
            case "hak.period":
                return Calculator.Period(Arg<Job>(args, "job"), Arg<int>(args, "no"));
            case "hak.all":
                return Calculator.All(Arg<Job>(args, "job"));
            case "hak.export":
            {
                var job = Arg<Job>(args, "job");
                int no = Arg<int>(args, "no");
                string name = Opt<string?>(args, "name") ?? "Hakediş";
                string? path = Opt<string?>(args, "path");
                bool open = path is null;
                if (path is null)
                {
                    using var dlg = new SaveFileDialog
                    {
                        Filter = "Excel çalışma kitabı (*.xlsx)|*.xlsx",
                        FileName = Projects.SafeName($"{name} - {no} Nolu Hakediş") + ".xlsx",
                        InitialDirectory = AppPaths.ProjectsDir,
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
                await Task.Run(() => ExcelExport.Write(job, name, no, path));
                if (open) OpenPath(path);
                return path;
            }
            case "catalog.info":
                return new
                {
                    count = Catalog.Items.Count,
                    sources = Catalog.Items.GroupBy(i => i.Source).Select(g => new { source = g.Key, count = g.Count() }).ToArray(),
                };
            case "catalog.import":
            {
                using var dlg = new OpenFileDialog { Filter = "Birim fiyat listesi (*.xlsx;*.xlsm;*.csv)|*.xlsx;*.xlsm;*.csv" };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                string source = Path.GetFileNameWithoutExtension(dlg.FileName);
                int n = await Task.Run(() => Catalog.Import(dlg.FileName, source));
                Catalog.Save(AppPaths.CatalogFile);
                return new { source, count = n };
            }
            case "catalog.remove":
                Catalog.Items.RemoveAll(i => i.Source == Arg<string>(args, "source"));
                Catalog.Save(AppPaths.CatalogFile);
                return null;
            case "catalog.search":
                return Catalog.Search(Opt<string?>(args, "q") ?? "", Opt<int?>(args, "max") ?? 60, Opt<string?>(args, "source")).ToArray();
            case "catalog.csbList":
            {
                using var http = CsbSite.CreateClient();
                return (await CsbSite.ListAsync(http)).Select(r => new { r.Year, r.Month, r.Label, r.Source }).ToArray();
            }
            case "catalog.csbImport":
            {
                // Ministry list (yearly book or monthly update); latest when no year/month is given.
                int year = Opt<int?>(args, "year") ?? 0, month = Opt<int?>(args, "month") ?? 1;
                using var http = CsbSite.CreateClient();
                var list = await CsbSite.ListAsync(http);
                var release = year == 0 ? list[0] : list.FirstOrDefault(r => r.Year == year && r.Month == month)
                    ?? throw new ArgumentException($"Bakanlık sitesinde {year}/{month:00} listesi yok. Mevcut son liste: {list[0].Label}.");
                var book = list.FirstOrDefault(r => r.Year == release.Year && r.Month == 1);
                var progress = new Progress<string>(m => Post(new { catalogProgress = m }));
                var items = await CsbSite.ReadReleaseAsync(http, release, book, Opt<bool>(args, "includeRayic"), AppPaths.CsbCacheDir, progress);
                Catalog.Replace(release.Source, items);
                Catalog.Save(AppPaths.CatalogFile);
                return new { source = release.Source, count = items.Count };
            }
            case "evds.fetch":
                return await Evds.FetchMonthlyAsync(Http, Arg<string>(args, "key"),
                    Opt<string?>(args, "series") ?? Evds.DefaultSeries, Arg<int>(args, "from"), Arg<int>(args, "to"));

            // ---- drawings (the editor runs in the cad2d iframe; files go through here) ----
            case "cad.pick":
            {
                string? path = Opt<string?>(args, "path");
                if (path is null)
                {
                    using var dlg = new OpenFileDialog { Filter = "Çizim dosyaları (*.dwg;*.dxf)|*.dwg;*.dxf" };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
                return StageDrawing(path);
            }
            case "cad.save":
            {
                string dxf = Arg<string>(args, "dxf");
                string? path = Opt<string?>(args, "path");
                if (path is null)
                {
                    using var dlg = new SaveFileDialog
                    {
                        Filter = "AutoCAD çizimi (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf",
                        FileName = Projects.SafeName(Opt<string?>(args, "name") ?? "Çizim"),
                        InitialDirectory = AppPaths.ProjectsDir,
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
                await Task.Run(() => SaveDrawing(dxf, path));
                return path;
            }
            case "cad.analyze":
            {
                string dxf = Arg<string>(args, "dxf");
                return await Task.Run(() =>
                {
                    using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(dxf));
                    var info = Drawing.Analyze(ACadSharp.IO.DxfReader.Read(ms));
                    return info with { SnapPoints = [] };
                });
            }

            // ---- generic files (3B model import/export) ----
            case "file.pick":
            {
                using var dlg = new OpenFileDialog { Filter = Opt<string?>(args, "filter") ?? "Tüm dosyalar (*.*)|*.*" };
                return dlg.ShowDialog(this) == DialogResult.OK ? StageDrawing(dlg.FileName) : null;
            }
            case "file.saveBase64":
            {
                byte[] bytes = Convert.FromBase64String(Arg<string>(args, "base64"));
                string? path = Opt<string?>(args, "path");
                if (path is null)
                {
                    using var dlg = new SaveFileDialog
                    {
                        Filter = Opt<string?>(args, "filter") ?? "Tüm dosyalar (*.*)|*.*",
                        FileName = Opt<string?>(args, "name") ?? "Model",
                        InitialDirectory = AppPaths.ProjectsDir,
                    };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                    path = dlg.FileName;
                }
                await File.WriteAllBytesAsync(path, bytes);
                return path;
            }

            // ---- Claude (MCP) ----
            case "mcp.info":
                return new
                {
                    serverPath = ClaudeSetup.ServerPath,
                    serverExists = File.Exists(ClaudeSetup.ServerPath),
                    claudeCodeCommand = $"claude mcp add --scope user muhendiskit -- \"{ClaudeSetup.ServerPath}\"",
                    desktopConfigPaths = ClaudeSetup.DesktopConfigs(),
                    desktopConfigured = ClaudeSetup.DesktopConfigured(),
                    codeConfigured = ClaudeSetup.CodeConfigured(),
                };
            case "mcp.configureDesktop":
                return ClaudeSetup.ConfigureDesktop();
            case "mcp.configureCode":
                return await ClaudeSetup.ConfigureCodeAsync();

            // ---- output ----
            case "pdf.save":
            {
                using var dlg = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = Projects.SafeName(Opt<string?>(args, "name") ?? "Rapor") + ".pdf",
                    InitialDirectory = AppPaths.ProjectsDir,
                };
                if (dlg.ShowDialog(this) != DialogResult.OK) return null;
                var settings = web.CoreWebView2.Environment.CreatePrintSettings();
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;
                settings.Orientation = Opt<bool>(args, "landscape") ? CoreWebView2PrintOrientation.Landscape : CoreWebView2PrintOrientation.Portrait;
                settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0.4;
                if (!await web.CoreWebView2.PrintToPdfAsync(dlg.FileName, settings))
                    throw new IOException("PDF oluşturulamadı.");
                OpenPath(dlg.FileName);
                return dlg.FileName;
            }

            default:
                throw new InvalidOperationException($"Bilinmeyen komut: {cmd}");
        }
    }

    /// <summary>Copies a drawing next to the editor (virtual host cad.example) so the iframe can fetch it.</summary>
    static object StageDrawing(string path)
    {
        foreach (var old in new DirectoryInfo(AppPaths.CadCacheDir).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Skip(6))
            try { old.Delete(); } catch (IOException) { }
        string file = Guid.NewGuid().ToString("n") + Path.GetExtension(path).ToLowerInvariant();
        File.Copy(path, Path.Combine(AppPaths.CadCacheDir, file));
        return new { url = $"https://{CadHost}/{file}", name = Path.GetFileName(path), path };
    }

    /// <summary>Writes the editor's DXF as-is, or converts it to DWG with ACadSharp.</summary>
    static void SaveDrawing(string dxf, string path)
    {
        string tmp = path + ".tmp";
        try
        {
            if (Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                Drawing.DxfToDwg(dxf, tmp);
            else
                File.WriteAllText(tmp, dxf);
            File.Move(tmp, path, overwrite: true);
        }
        finally { File.Delete(tmp); }
    }
}
