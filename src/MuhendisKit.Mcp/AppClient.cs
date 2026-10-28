using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace MuhendisKit.Mcp;

/// <summary>
/// Talks to the running MühendisKit window over its per-user named pipe, starting the app when it isn't open.
/// One request at a time: the window serialises UI work anyway.
/// </summary>
public sealed class AppClient : IAsyncDisposable
{
    static string PipeName => "MuhendisKit.Automation." + WindowsIdentity.GetCurrent().User!.Value;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly SemaphoreSlim gate = new(1, 1);
    NamedPipeClientStream? pipe;
    StreamReader? reader;
    StreamWriter? writer;
    int seq;

    public async Task<JsonNode?> CallAsync(string cmd, object? args = null, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                await EnsureConnectedAsync(ct);
                int id = ++seq;
                try
                {
                    await writer!.WriteLineAsync(JsonSerializer.Serialize(new { id, cmd, args }, Json).AsMemory(), ct);
                    string? line = await reader!.ReadLineAsync(ct) ?? throw new IOException("Bağlantı kapandı.");
                    var msg = JsonNode.Parse(line)!;
                    if (msg["ok"]?.GetValue<bool>() == true) return msg["result"];
                    throw new McpAppException(msg["error"]?.GetValue<string>() ?? "Bilinmeyen hata");
                }
                catch (IOException) when (attempt == 0)
                {
                    Disconnect();   // the window was closed or restarted: reconnect (and relaunch) once
                }
            }
        }
        finally { gate.Release(); }
    }

    async Task EnsureConnectedAsync(CancellationToken ct)
    {
        if (pipe is { IsConnected: true }) return;
        Disconnect();
        if (!await TryConnectAsync(500, ct))
        {
            Launch();
            var until = DateTime.UtcNow.AddSeconds(45);
            while (!await TryConnectAsync(1000, ct))
                if (DateTime.UtcNow > until)
                    throw new McpAppException("MühendisKit başlatılamadı ya da yanıt vermiyor.");
        }
    }

    async Task<bool> TryConnectAsync(int timeoutMs, CancellationToken ct)
    {
        var p = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await p.ConnectAsync(timeoutMs, ct);
        }
        catch (TimeoutException)
        {
            await p.DisposeAsync();
            return false;
        }
        pipe = p;
        reader = new StreamReader(p, new UTF8Encoding(false));
        writer = new StreamWriter(p, new UTF8Encoding(false)) { AutoFlush = true };
        return true;
    }

    static void Launch()
    {
        string exe = Environment.GetEnvironmentVariable("MUHENDISKIT_EXE") is { Length: > 0 } env
            ? env
            : Path.Combine(AppContext.BaseDirectory, "MuhendisKit.exe");
        if (!File.Exists(exe))
            throw new McpAppException($"MühendisKit bulunamadı: {exe}. MUHENDISKIT_EXE ortam değişkeniyle yolunu belirtin.");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    void Disconnect()
    {
        reader?.Dispose();
        writer?.Dispose();
        pipe?.Dispose();
        pipe = null; reader = null; writer = null;
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        return ValueTask.CompletedTask;
    }
}

// McpException messages reach the model as the tool error text (other exceptions are masked).
public sealed class McpAppException(string message) : ModelContextProtocol.McpException(message);
