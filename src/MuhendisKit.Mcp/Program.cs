using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MuhendisKit.Mcp;

// stdio MCP server for Claude Code / Claude Desktop. stdout carries the protocol, so logs go to stderr.
// Empty builder: no EventLog/config providers (the exe also runs from the app folder with the app's dependency set).
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton<AppClient>();
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = "muhendiskit", Version = typeof(Tools).Assembly.GetName().Version?.ToString(3) ?? "0.1.0" };
        o.ServerInstructions = """
            MühendisKit is a Windows app for a Turkish mechanical / site-control engineer: mechanical calculators,
            thermal insulation (TS 825, Glaser), hakediş (progress payments, price difference), a 2D DWG/DXF editor and a
            3D solid modeller (OpenCascade). These tools drive the engineer's open MühendisKit window; they see every change.
            - Talk to the engineer in Turkish unless they write otherwise.
            - 2D: drawing_api once, then drawing_run_script for all drawing/queries; verify with cad.measure or drawing_preview.
            - 3D: model3d_guide once, then model3d_call(run_program …) and model3d_call(capture_screenshot) to check.
            - Never invent standard or contract values (TS 825 U limits, TS 2164 design temperatures, unit prices, Yİ-ÜFE
              indices, B coefficient). Ask, or leave them empty; say which values you assumed.
            - Saving files: give full Windows paths; projects live in Documents\MühendisKit\Projeler.
            """;
    })
    .WithStdioServerTransport()
    .WithTools<Tools>();

await builder.Build().RunAsync();
