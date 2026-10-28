using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MuhendisKit.Mcp;

/// <summary>
/// MühendisKit tools. Everything runs in the user's open MühendisKit window, so the engineer sees each change.
/// Drawing and 3D work is script-first: a few lifecycle tools plus one tool that runs code against the editor.
/// </summary>
[McpServerToolType]
public sealed class Tools(AppClient app)
{
    // Relaxed escaping keeps Turkish text (ç, ş, ğ …) readable instead of "ç"-style escapes.
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------- app & project ----------

    [McpServerTool(Name = "app_status", ReadOnly = true), Description("Starts MühendisKit if needed and reports the version, the visible page and the open project.")]
    public async Task<string> AppStatus(CancellationToken ct)
    {
        var status = await app.CallAsync("app.status", null, ct);
        var ui = await app.CallAsync("ui.state", null, ct);
        return Text(new JsonObject { ["app"] = status?.DeepClone(), ["ui"] = ui?.DeepClone() });
    }

    [McpServerTool(Name = "app_screenshot", ReadOnly = true), Description("PNG screenshot of the whole MühendisKit window (sidebar, page, editors). Use it to check what the engineer sees.")]
    public async Task<CallToolResult> AppScreenshot(CancellationToken ct)
    {
        string b64 = (await app.CallAsync("app.screenshot", null, ct))!.GetValue<string>();
        return new CallToolResult { Content = [ImageContentBlock.FromBytes(Convert.FromBase64String(b64), "image/png")] };
    }

    [McpServerTool(Name = "app_navigate"), Description("Shows a page: home, calc (mechanical calculators), ins (thermal insulation TS 825), hak (hakediş / progress payments), cad (2D drawing), model3d (3D solid modelling), settings.")]
    public async Task<string> AppNavigate([Description("Page id")] string page, CancellationToken ct) =>
        Text(await app.CallAsync("ui.go", new { page }, ct));

    [McpServerTool(Name = "project_list", ReadOnly = true), Description("Lists project files (.mkproj) in Documents\\MühendisKit\\Projeler with name, path and last change.")]
    public async Task<string> ProjectList(CancellationToken ct) =>
        Text(await app.CallAsync("host.projects.list", new { extra = Array.Empty<string>() }, ct));

    [McpServerTool(Name = "project_open"), Description("Opens a project file (.mkproj) in the window; later project/insulation/hakediş tools work on it.")]
    public async Task<string> ProjectOpen([Description("Full path of the .mkproj file")] string path, CancellationToken ct) =>
        Text(await app.CallAsync("project.open", new { path }, ct));

    [McpServerTool(Name = "project_create"), Description("Creates and opens a new project in Documents\\MühendisKit\\Projeler.")]
    public async Task<string> ProjectCreate(string name, [Description("Site / location")] string? location = null,
        [Description("Owner institution (idare)")] string? owner = null, CancellationToken ct = default) =>
        Text(await app.CallAsync("project.create", new { name, location, owner }, ct));

    [McpServerTool(Name = "project_get", ReadOnly = true), Description("""
        Reads the open project as JSON, or one section of it. Sections:
        - info: { location, owner, notes }
        - calcs: saved calculator results [{ type, title, input, summary, date }]
        - insulation: { zone: "1".."6" | null, climate: { insideTemp °C, insideRh %, outsideTemp °C, outsideRh % },
            elements: [{ id, name, kind: duvar|cati|doseme_hava|doseme_toprak|duvar_toprak|pencere, rsi, rse,
                         layers: [{ id, name, thickness (cm), lambda (W/mK), mu, fixedR (m²K/W) | null, insulation: bool }], fixedU (windows) }] }
        - heatloss: { insideTemp, outsideTemp, rooms: [{ id, name, volume m³, n 1/h, increment %, insideTemp | null,
                      surfaces: [{ id, name, area m², u W/m²K, dt K | null (null = Ti − Td) }] }] }
        - job (hakediş): { owner, contractor, contractNo, tenderMonth "yyyy-MM", contractAmount, b, pnDigits, kdvRate %,
            deductions: [{ name, ratePercent, base: tutar|kdv|toplam }],
            items: [{ id, pozNo, description, unit, unitPrice }],
            lines: [{ id, itemId, period, description, count, width, length, height, fixed, deduct, source }]  (quantity = fixed ?? count×width×length×height, empty dims = 1),
            periods: [{ no, date "yyyy-MM-dd", month "yyyy-MM", allowanceMonth | null, note }],
            indices: { "yyyy-MM": index value } }
        """)]
    public async Task<string> ProjectGet([Description("Optional: info, calcs, insulation, heatloss, job")] string? section = null, CancellationToken ct = default) =>
        Text(await app.CallAsync("project.get", new { section }, ct));

    [McpServerTool(Name = "project_update"), Description("""
        Replaces one section of the open project (see project_get for the shapes) and saves it; the window refreshes.
        Read the section first, change what you need, write the whole section back. New ids: any unique string.
        Never invent standard values (TS 825 limits, TS 2164 design temperatures, unit prices, indices): ask the engineer or leave them empty.
        """)]
    public async Task<string> ProjectUpdate([Description("name, info, calcs, insulation, heatloss or job")] string section,
        [Description("The complete new value of the section")] JsonElement value, CancellationToken ct) =>
        Text(await app.CallAsync("project.update", new { section, value = Json(value) }, ct));

    // ---------- calculators ----------

    [McpServerTool(Name = "calculate", ReadOnly = true), Description("""
        Runs a mechanical calculation in MühendisKit's engine (SI units) and returns the result JSON.
        kind and input:
        - pipe: { material: celik|bakir|ppr, flowM3h, tempC, maxVelocity m/s, maxPressureDrop Pa/m } → rows per size (velocity, Re, f, R) + suggested
        - pipe_flow: { kw, dt, temp } → water flow m³/h for a heat load
        - duct: { flowM3h, targetPressureDrop Pa/m, roughnessMm=0.09, aspectRatio=2 } → round + equivalent rectangle (Huebscher)
        - psychro: { t °C, mode: rh|wetbulb|dewpoint, value (% for rh, °C otherwise), altitude m } → moist air state (ASHRAE)
        - tank: { systemVolumeL, maxTempC, staticHeightM, safetyValveBar, fillTempC=10 } → expansion vessel (TS EN 12828)
        - gas: { material: celik|bakir, flowM3h, lengthM, maxDropMbar, relativeDensity=0.6 } → Renouard low pressure sizing
        - heatloss: { rooms: [{ name, surfaces: [{ name, area, u, tempDiff }], volumeM3, airChangesPerHour, insideTempC, outsideTempC, incrementPercent }] }
        Design limits (max velocity, pressure drop, allowed gas drop, outside design temperature) come from the project specification: ask, don't assume.
        """)]
    public async Task<string> Calculate([Description("pipe, pipe_flow, duct, psychro, tank, gas or heatloss")] string kind,
        [Description("Input object for that kind")] JsonElement input, CancellationToken ct)
    {
        input = Json(input);
        var (cmd, args) = kind switch
        {
            "pipe" => ("host.calc.pipe", (object)new { input }),
            "pipe_flow" => ("host.calc.pipeFlow", input),
            "duct" => ("host.calc.duct", new { input }),
            "psychro" => ("host.calc.psychro", input),
            "tank" => ("host.calc.tank", new { input }),
            "gas" => ("host.calc.gas", new { input }),
            "heatloss" => ("host.calc.heatloss", input),
            _ => throw new McpAppException("kind: pipe, pipe_flow, duct, psychro, tank, gas veya heatloss olmalı."),
        };
        return Text(await app.CallAsync(cmd, args, ct));
    }

    [McpServerTool(Name = "insulation_evaluate", ReadOnly = true), Description("""
        U value, total resistance and Glaser condensation analysis (TS EN ISO 13788) of one building element.
        element: { name, kind, rsi, rse, layers: [{ name, thicknessM (metres!), lambda, mu, fixedR | null }], fixedU | null }
        climate (optional): { insideTemp, insideRh (0–1), outsideTemp, outsideRh (0–1) }
        targetU + insulationLayer (index) → required thickness of that layer in metres.
        """)]
    public async Task<string> InsulationEvaluate(JsonElement element, JsonElement? climate = null, double? targetU = null,
        int? insulationLayer = null, CancellationToken ct = default) =>
        Text(await app.CallAsync("host.ins.evaluate", new { element = Json(element), climate = climate is { } c ? Json(c) : (JsonElement?)null, targetU, insulationLayer }, ct));

    [McpServerTool(Name = "hakedis_compute", ReadOnly = true), Description("Computes progress payment no. N of the open project: item quantities and amounts, cumulative/previous/this period, price difference F = An × B × (Pn − 1), VAT, deductions, payable, warnings.")]
    public async Task<string> HakedisCompute([Description("Payment (period) number")] int period, CancellationToken ct) =>
        Text(await app.CallAsync("hak.period", new { no = period }, ct));

    [McpServerTool(Name = "hakedis_export_excel"), Description("Writes payment no. N of the open project to an Excel workbook (İcmal, Hakediş Raporu, Yeşil Defter, Fiyat Farkı sheets).")]
    public async Task<string> HakedisExportExcel(int period, [Description("Full .xlsx path")] string path, CancellationToken ct) =>
        Text(await app.CallAsync("hak.export", new { no = period, path }, ct));

    // ---------- unit-price catalogue (poz) ----------

    [McpServerTool(Name = "poz_search", ReadOnly = true), Description("""
        Searches the unit-price catalogue (poz kataloğu): every word must occur in the poz number or description,
        e.g. "25.130", "radyatör panel", "pprc boru 20". Returns [{ pozNo, description, unit, unitPrice (TL),
        mountingPrice (montaj bedeli, mechanical/electrical), spec (tarif), source (e.g. "ÇŞİDB 2026 Ekim") }].
        Use these instead of asking the engineer for poz numbers or prices; say which source (list/month) the price is from.
        If the catalogue is empty or lacks the needed month, call poz_catalog_update.
        """)]
    public async Task<string> PozSearch([Description("Words or poz number prefix")] string query,
        [Description("Only this source, e.g. \"ÇŞİDB 2026 Ekim\" (see poz_catalog_info)")] string? source = null,
        [Description("Max results (default 20)")] int max = 20, CancellationToken ct = default) =>
        Text(await app.CallAsync("host.catalog.search", new { q = query, source, max }, ct));

    [McpServerTool(Name = "poz_catalog_info", ReadOnly = true), Description("Sources in the unit-price catalogue with their poz counts, and the ministry (ÇŞİDB) lists available for download: yearly books (month 1) and monthly updates.")]
    public async Task<string> PozCatalogInfo(CancellationToken ct)
    {
        var info = await app.CallAsync("host.catalog.info", null, ct);
        JsonNode? available;
        try { available = await app.CallAsync("host.catalog.csbList", null, ct); }
        catch (McpAppException e) { available = $"Bakanlık listesi alınamadı: {e.Message}"; }
        return Text(new JsonObject { ["catalog"] = info?.DeepClone(), ["ministryLists"] = available?.DeepClone() });
    }

    [McpServerTool(Name = "poz_catalog_update"), Description("""
        Downloads a ministry (ÇŞİDB Yüksek Fen Kurulu) unit-price list from yfk.csb.gov.tr and adds it to the catalogue
        as source "ÇŞİDB <year> <month>" (replacing an earlier import of the same list). Without year/month: the latest list.
        Month 1 = the yearly book; later months = monthly updated prices (descriptions completed from the yearly book).
        The first download takes a few minutes; PDFs are cached.
        """)]
    public async Task<string> PozCatalogUpdate([Description("Year, e.g. 2026")] int? year = null, [Description("Month 1–12")] int? month = null,
        [Description("Also the rayiç (material/labour/machine) lists")] bool includeRayic = false, CancellationToken ct = default) =>
        Text(await app.CallAsync("host.catalog.csbImport", new { year, month, includeRayic }, ct));

    // ---------- 2D drawing (MLightCAD editor) ----------

    [McpServerTool(Name = "drawing_api", ReadOnly = true), Description("Reference of the cad.* script API used by drawing_run_script. Read it before the first script.")]
    public async Task<string> DrawingApi(CancellationToken ct) =>
        (await app.CallAsync("cad2d.api", null, ct))?.GetValue<string>() ?? "";

    [McpServerTool(Name = "drawing_run_script"), Description("""
        Runs a JavaScript function body against the drawing open in the 2D editor (argument: cad). Use it for ALL drawing,
        queries, statistics and edits: cad.edit.transaction(label, tx => { tx.drawLine(...) … }), cad.entities.where(…),
        cad.measure.*, cad.view.zoomExtents(). Return JSON from the body. Units are drawing units (usually mm).
        cad.view.capturePreview() attaches a PNG to the result. Call drawing_api first for the full API.
        """)]
    public async Task<CallToolResult> DrawingRunScript([Description("Function body; may `return` a JSON value")] string code,
        [Description("Short label for the undo history")] string? intent = null, CancellationToken ct = default)
    {
        var r = await app.CallAsync("cad2d.script", new { code, intent }, ct);
        var images = r?["images"]?.AsArray().Select(i => i?["dataUrl"]?.GetValue<string>()).OfType<string>().ToList() ?? [];
        if (r is JsonObject o) o.Remove("images");
        var result = new CallToolResult { Content = [new TextContentBlock { Text = r?.ToJsonString(Indented) ?? "null" }] };
        foreach (var url in images) result.Content.Add(DataUrlImage(url));
        result.IsError = r?["success"]?.GetValue<bool>() == false;
        return result;
    }

    [McpServerTool(Name = "drawing_open"), Description("Opens a DWG or DXF file in the 2D editor.")]
    public async Task<string> DrawingOpen([Description("Full path of the .dwg/.dxf file")] string path, CancellationToken ct) =>
        Text(await app.CallAsync("cad2d.open", new { path }, ct));

    [McpServerTool(Name = "drawing_new"), Description("Starts a new empty drawing (ISO template, mm).")]
    public async Task<string> DrawingNew(CancellationToken ct) => Text(await app.CallAsync("cad2d.new", null, ct));

    [McpServerTool(Name = "drawing_save"), Description("Saves the current drawing. The extension picks the format: .dwg (converted with ACadSharp) or .dxf.")]
    public async Task<string> DrawingSave([Description("Full .dwg or .dxf path")] string path, CancellationToken ct) =>
        Text(await app.CallAsync("cad2d.save", new { path }, ct));

    [McpServerTool(Name = "drawing_quantities", ReadOnly = true), Description("Quantity take-off of the current drawing: total length and closed area per layer, block (INSERT) counts per block and layer, drawing units. Lengths include polyline arcs.")]
    public async Task<string> DrawingQuantities(CancellationToken ct) => Text(await app.CallAsync("cad2d.quantities", null, ct));

    [McpServerTool(Name = "drawing_fit"), Description("Zooms the 2D editor to show the whole drawing.")]
    public async Task<string> DrawingFit(CancellationToken ct) => Text(await app.CallAsync("cad2d.fit", null, ct));

    [McpServerTool(Name = "drawing_preview", ReadOnly = true), Description("PNG of the whole drawing (model space extents), independent of the current zoom.")]
    public async Task<CallToolResult> DrawingPreview(CancellationToken ct)
    {
        string url = (await app.CallAsync("cad2d.preview", new { longSide = 1400 }, ct))!.GetValue<string>();
        return new CallToolResult { Content = [DataUrlImage(url)] };
    }

    // ---------- 3D solid modelling (Chili3D / OpenCascade) ----------

    [McpServerTool(Name = "model3d_guide", ReadOnly = true), Description("Guide for the 3D modeller: how its tools work, modelling rules and the current document state. Read it before modelling.")]
    public async Task<string> Model3dGuide(CancellationToken ct) =>
        (await app.CallAsync("cad3d.guide", null, ct))?.GetValue<string>() ?? "";

    [McpServerTool(Name = "model3d_tools", ReadOnly = true), Description("Lists the 3D modeller's tools with their JSON parameter schemas (run_program, load_skill, capture_screenshot, transform_node, set_material, …). Call them with model3d_call.")]
    public async Task<string> Model3dTools(CancellationToken ct) => Text(await app.CallAsync("cad3d.tools", null, ct));

    [McpServerTool(Name = "model3d_call"), Description("""
        Calls one 3D modeller tool by name. Typical flow: load_skill {name:"modeling-api"} once, then run_program { ops: [...] }
        to build solids (box, cylinder, prism, revolve, sweep, loft, booleanCut/Fuse, fillet, chamfer …, units mm, degrees),
        then capture_screenshot to look at the result. Images in the result are returned as images.
        """)]
    public async Task<CallToolResult> Model3dCall([Description("Tool name from model3d_tools")] string tool,
        [Description("Tool arguments object")] JsonElement? args = null, CancellationToken ct = default)
    {
        var r = await app.CallAsync("cad3d.call", new { name = tool, args = args is { } a ? Json(a) : JsonDocument.Parse("{}").RootElement }, ct);
        var result = new CallToolResult { Content = [new TextContentBlock { Text = r?["content"]?.GetValue<string>() ?? "" }] };
        foreach (var img in r?["images"]?.AsArray() ?? [])
            result.Content.Add(ImageContentBlock.FromBytes(Convert.FromBase64String(img!["data"]!.GetValue<string>()), img["mediaType"]?.GetValue<string>() ?? "image/png"));
        return result;
    }

    [McpServerTool(Name = "model3d_new"), Description("Starts a new empty 3D document.")]
    public async Task<string> Model3dNew(string? name = null, CancellationToken ct = default) =>
        Text(await app.CallAsync("cad3d.newDocument", new { name = name ?? "Model" }, ct));

    [McpServerTool(Name = "model3d_import"), Description("Imports a STEP, IGES, BREP or STL file into the current 3D document.")]
    public async Task<string> Model3dImport([Description("Full file path")] string path, CancellationToken ct) =>
        Text(await app.CallAsync("cad3d.import", new { path }, ct));

    [McpServerTool(Name = "model3d_export"), Description("Exports the 3D model (all top-level parts, or the given node ids) to a file. format: .step, .iges, .brep, .stl binary, .stl, .obj, .ply")]
    public async Task<string> Model3dExport([Description("Full output path")] string path, [Description("Export format, e.g. .step")] string format,
        [Description("Optional node ids")] string[]? ids = null, CancellationToken ct = default) =>
        Text(await app.CallAsync("cad3d.export", new { path, format, ids }, ct));

    // ---------- helpers ----------

    static string Text(JsonNode? node) => node?.ToJsonString(Indented) ?? "null";

    /// <summary>
    /// Object/array arguments have an untyped schema, so some clients send them as a JSON string
    /// ("{\"material\":…}") instead of an object. Accept both.
    /// </summary>
    static JsonElement Json(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.String) return e;
        string s = e.GetString()!.Trim();
        if (s is not ['{', ..] and not ['[', ..]) return e;
        try { return JsonDocument.Parse(s).RootElement; }
        catch (JsonException ex) { throw new McpAppException($"Girdi geçerli JSON değil: {ex.Message}"); }
    }

    static ImageContentBlock DataUrlImage(string dataUrl)
    {
        int comma = dataUrl.IndexOf(',');
        string mime = dataUrl[5..dataUrl.IndexOf(';')];
        return ImageContentBlock.FromBytes(Convert.FromBase64String(dataUrl[(comma + 1)..]), mime);
    }
}
