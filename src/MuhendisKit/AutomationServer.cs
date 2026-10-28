using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MuhendisKit;

/// <summary>
/// Local automation endpoint for the MCP server (MuhendisKit.Mcp.exe): newline-delimited JSON over a named pipe
/// that only the current Windows user can open. Request {id, cmd, args} → response {id, ok, result | error}.
/// </summary>
sealed class AutomationServer(Func<string, JsonNode?, Task<object?>> handle)
{
    /// <summary>Per-user pipe name (the user's SID), shared with the MCP server.</summary>
    public static string PipeName => "MuhendisKit.Automation." + WindowsIdentity.GetCurrent().User!.Value;

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly CancellationTokenSource cts = new();

    public void Start() => _ = Task.Run(AcceptLoop);

    /// <summary>Client side, for a second app start: runs commands in the window that is already open.</summary>
    public static void SendToRunning(IEnumerable<(string Cmd, object? Args)> commands)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect(5000);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            int id = 0;
            foreach (var (cmd, args) in commands)
            {
                writer.WriteLine(JsonSerializer.Serialize(new { id = ++id, cmd, args }, Json));
                reader.ReadLine();
            }
        }
        catch (Exception e) when (e is IOException or TimeoutException) { }
    }

    public void Stop() => cts.Cancel();

    async Task AcceptLoop()
    {
        while (!cts.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.WaitForConnectionAsync(cts.Token); }
            catch (OperationCanceledException) { await pipe.DisposeAsync(); return; }
            catch (IOException) { await pipe.DisposeAsync(); continue; }
            _ = Task.Run(() => Serve(pipe));
        }
    }

    async Task Serve(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false));
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            while (!cts.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(cts.Token); }
                catch (Exception e) when (e is IOException or OperationCanceledException) { return; }
                if (line is null) return;

                JsonNode? msg;
                try { msg = JsonNode.Parse(line); }
                catch (JsonException) { continue; }
                var id = msg?["id"]?.DeepClone();
                string cmd = msg?["cmd"]?.GetValue<string>() ?? "";
                string reply;
                try
                {
                    object? result = await handle(cmd, msg?["args"]);
                    reply = JsonSerializer.Serialize(new { id, ok = true, result }, Json);
                }
                catch (Exception e)
                {
                    reply = JsonSerializer.Serialize(new { id, ok = false, error = (e.InnerException ?? e).Message }, Json);
                }
                try { await writer.WriteLineAsync(reply); }
                catch (IOException) { return; }
            }
        }
    }
}
