namespace MuhendisKit;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Setup task: register the MCP server with the Claude apps on this PC, no window.
        if (args.Contains("--register-claude"))
        {
            ClaudeSetup.RegisterInstalledAsync().GetAwaiter().GetResult();
            return;
        }

        // A project file passed on the command line (double-click on .mkproj) opens directly.
        string? open = args.FirstOrDefault(a => a.EndsWith(".mkproj", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

        // One window per user: a second start hands its project to the running window over the automation pipe.
        using var mutex = new Mutex(true, "MuhendisKit.SingleInstance." + Environment.UserName, out bool first);
        if (!first)
        {
            AllowSetForegroundWindow(-1);   // ASFW_ANY: let the running window come to the front
            AutomationServer.SendToRunning(open is null ? [("app.show", null)] : [("app.show", null), ("project.open", new { path = open })]);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportError((Exception)e.ExceptionObject, fatal: true);
        Application.Run(new MainForm(open));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    public static string ErrorLog => Path.Combine(AppPaths.LocalDir, "error.log");

    static void ReportError(Exception e, bool fatal)
    {
        try { File.AppendAllText(ErrorLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(fatal ? "FATAL" : "ERROR")} {e}\n\n"); }
        catch (IOException) { }
        if (!fatal)
            MessageBox.Show($"Beklenmeyen bir hata oldu:\n\n{e.Message}\n\nAyrıntılar: {ErrorLog}", "MühendisKit",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
