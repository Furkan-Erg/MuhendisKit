using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;

namespace MuhendisKit;

// Commands from the MCP server. "host.*" runs a host command directly; "app.*" is window-level;
// everything else goes to the page (App.automation in core.js), which owns the project and the editors.
sealed partial class MainForm
{
    AutomationServer? automation;
    readonly TaskCompletionSource uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> pageCalls = new();
    int pageCallSeq;

    void StartAutomation()
    {
        automation = new AutomationServer(OnAutomationAsync);
        automation.Start();
        FormClosed += (_, _) => automation.Stop();
    }

    // Runs on a pipe thread; hops to the UI thread for everything that touches the window.
    Task<object?> OnAutomationAsync(string cmd, JsonNode? args)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        BeginInvoke(async () =>
        {
            try { tcs.SetResult(await AutomationAsync(cmd, args)); }
            catch (Exception e) { tcs.SetException(e); }
        });
        return tcs.Task;
    }

    async Task<object?> AutomationAsync(string cmd, JsonNode? args)
    {
        await uiReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
        if (cmd.StartsWith("host.", StringComparison.Ordinal))
            return await HandleAsync(cmd[5..], args);
        switch (cmd)
        {
            case "app.status":
                return new { version = Application.ProductVersion.Split('+')[0], visible = Visible, pid = Environment.ProcessId };
            case "app.show":
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                return null;
            case "app.screenshot":
            {
                using var ms = new MemoryStream();
                await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                return Convert.ToBase64String(ms.ToArray());
            }
            default:
                return await CallPageAsync(cmd, args);
        }
    }

    async Task<JsonNode?> CallPageAsync(string cmd, JsonNode? args)
    {
        int id = Interlocked.Increment(ref pageCallSeq);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pageCalls[id] = tcs;
        Post(new { automation = new { id, cmd, args } });
        try { return await tcs.Task.WaitAsync(TimeSpan.FromMinutes(5)); }
        finally { pageCalls.TryRemove(id, out _); }
    }

    // {automationReply: id, ok, result, error} from the page, or {automationReady: true} once the UI has started.
    bool TryHandleAutomationMessage(JsonNode? msg)
    {
        if (msg?["automationReady"] is not null)
        {
            uiReady.TrySetResult();
            return true;
        }
        if (msg?["automationReply"] is not { } idNode) return false;
        if (pageCalls.TryGetValue(idNode.GetValue<int>(), out var tcs))
        {
            if (msg["ok"]?.GetValue<bool>() == true) tcs.TrySetResult(msg["result"]?.DeepClone());
            else tcs.TrySetException(new InvalidOperationException(msg["error"]?.GetValue<string>() ?? "Hata"));
        }
        return true;
    }
}
