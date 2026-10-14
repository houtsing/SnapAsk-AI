using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace SnapAsk;

/// One hidden, pre-loaded chat page (claude.ai / chatgpt.com / gemini.google.com) per provider,
/// signed in with the user's own account and driven behind the scenes by the popup.
public sealed class WebBackend
{
    private static readonly Dictionary<Provider, WebBackend> all = new();
    private static Task<CoreWebView2Environment>? env;

    public static WebBackend Get(Provider p)
    {
        if (!all.TryGetValue(p, out var b)) { b = new WebBackend(p); all[p] = b; }
        return b;
    }

    /// Start every site loading at launch, so the first question doesn't wait for a page load.
    public static void PrewarmAll()
    {
        foreach (Provider p in Enum.GetValues(typeof(Provider)))
            if (p != Provider.Claude || !ClaudeCli.InUse) Get(p);
    }

    private static Task<CoreWebView2Environment> Env() => env ??= CoreWebView2Environment.CreateAsync(
        null, Path.Combine(Log.Dir, "WebView2"),
        new CoreWebView2EnvironmentOptions(
            // Keep hidden pages rendering at full speed (they'd otherwise be throttled as "occluded").
            "--disable-features=CalculateNativeWinOcclusion --disable-background-timer-throttling " +
            "--disable-renderer-backgrounding --disable-backgrounding-occluded-windows"));

    public Provider Provider { get; }
    private readonly Site site;
    private readonly Window host;
    private readonly WebView2 web;
    private readonly Task ready;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
    private TaskCompletionSource<bool>? navigation;
    private bool showingFull;

    public Guid? Owner { get; private set; }   // the popup whose conversation is on the page
    private bool fresh = true;                 // page is an untouched new chat
    private int generation;                    // bumps on reset; stale polls stop

    private WebBackend(Provider p)
    {
        Provider = p;
        site = Sites.For(p);
        web = new WebView2();
        host = new Window
        {
            Title = p.ToString(), Width = 1000, Height = 860, Left = -32000, Top = -32000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, Content = web,
        };
        host.SourceInitialized += (_, _) => SetToolWindow(host, true);
        host.Closing += (s, e) => { e.Cancel = true; HideFull(); };   // "closing" the full chat just hides it again
        host.Show();
        ready = InitAsync();
    }

    private async Task InitAsync()
    {
        await web.EnsureCoreWebView2Async(await Env());
        var core = web.CoreWebView2;
        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
                var id = doc.RootElement.GetProperty("id").GetString()!;
                if (pending.TryRemove(id, out var tcs)) tcs.TrySetResult(doc.RootElement.Clone());
            }
            catch (Exception ex) { Log.Write($"web message parse failed: {ex.Message}"); }
        };
        core.NavigationStarting += (_, _) => navigation ??= new TaskCompletionSource<bool>();
        core.NavigationCompleted += (_, _) => { navigation?.TrySetResult(true); navigation = null; };
        await NavigateAsync(site.Url);
    }

    private Task NavigateAsync(string url)
    {
        navigation = new TaskCompletionSource<bool>();
        var done = navigation.Task;
        web.CoreWebView2.Navigate(url);
        return Task.WhenAny(done, Task.Delay(30000));
    }

    /// Back to a blank new chat, ready for the next snapshot.
    public async Task ResetAsync()
    {
        generation++;
        Owner = null;
        fresh = true;
        await ready;
        await NavigateAsync(site.Url);
    }

    public void Release(Guid id) { if (Owner == id) _ = ResetAsync(); }

    public static void ReleaseAll(Guid id) { foreach (var b in all.Values) b.Release(id); }

    /// true = signed in, false = not signed in, null = still loading / unknown.
    public async Task<bool?> CheckSignedInAsync()
    {
        if (!ready.IsCompleted) return null;
        try
        {
            var r = await RunAsync(Sites.SignedInJs, new Dictionary<string, object> { ["composer"] = site.Composer }, TimeSpan.FromSeconds(5));
            return r.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null };
        }
        catch { return null; }
    }

    public static void ReloadAll() { foreach (var b in all.Values) _ = b.ResetAsync(); }

    // ---- Showing the real page (sign-in, full chat) ----

    public void ShowFull()
    {
        showingFull = true;
        SetToolWindow(host, false);
        host.WindowStyle = WindowStyle.SingleBorderWindow;
        host.ResizeMode = ResizeMode.CanResize;
        host.ShowInTaskbar = true;
        host.Title = $"{Provider} — close this window when you're done";
        var wa = SystemParameters.WorkArea;
        host.Left = wa.Left + (wa.Width - host.Width) / 2;
        host.Top = wa.Top + Math.Max(0, (wa.Height - host.Height) / 2);
        host.Activate();
    }

    private void HideFull()
    {
        if (!showingFull) return;
        showingFull = false;
        host.WindowStyle = WindowStyle.None;
        host.ResizeMode = ResizeMode.NoResize;
        host.ShowInTaskbar = false;
        SetToolWindow(host, true);
        host.Left = host.Top = -32000;
        // A sign-in may have just happened; make sure the page reflects it.
        if (Owner == null) _ = ResetAsync();
    }

    // ---- Asking ----

    /// Sends one question (attaching `png` when given) and streams the reply text through `onUpdate`.
    /// Returns null on success, otherwise an error message.
    public async Task<string?> AskAsync(Guid id, string text, byte[]? png, Action<string> onUpdate)
    {
        await ready;
        if (Owner != id && !fresh) await ResetAsync();   // another popup's chat is on the page
        Owner = id;
        fresh = false;
        int gen = generation;

        JsonElement r;
        try
        {
            r = await RunAsync(Sites.AskJs, new Dictionary<string, object>
            {
                ["composer"] = site.Composer, ["send"] = site.Send, ["reply"] = site.Reply,
                ["b64"] = png == null ? "" : Convert.ToBase64String(png), ["text"] = text,
            }, TimeSpan.FromSeconds(120));
        }
        catch (Exception ex) { return $"Couldn't send to {Provider} ({ex.Message}). Try “Open full chat”."; }
        if (gen != generation) return "Cancelled.";

        if (!r.TryGetProperty("before", out var beforeEl))
        {
            string code = r.TryGetProperty("error", out var e) ? e.GetString() ?? "unknown" : "unknown";
            Log.Write($"{Provider} ask failed: {code}");
            return code == "not-signed-in"
                ? $"{Provider} isn't signed in yet. Sign in once, then ask again."
                : $"Couldn't send to {Provider} ({code}). Try “Open full chat”.";
        }

        int before = beforeEl.GetInt32();
        string last = "";
        int stable = 0;
        var started = DateTime.UtcNow;
        while (true)
        {
            await Task.Delay(250);
            if (gen != generation) return "Cancelled.";
            JsonElement d;
            try
            {
                d = await RunAsync(Sites.ReadJs, new Dictionary<string, object>
                    { ["reply"] = site.Reply, ["stop"] = site.Stop, ["before"] = before }, TimeSpan.FromSeconds(10));
            }
            catch { continue; }
            string t = d.TryGetProperty("text", out var te) ? te.GetString() ?? "" : "";
            bool streaming = !d.TryGetProperty("streaming", out var se) || se.GetBoolean();
            if (t != last) onUpdate(t);
            stable = (t == last && t.Length > 0) ? stable + 1 : 0;
            last = t;
            if (t.Length > 0 && !streaming && stable >= 6) return null;             // ~1.5 s with no change
            if ((DateTime.UtcNow - started).TotalSeconds > 300) return t.Length > 0 ? null : "Timed out waiting for a reply.";
        }
    }

    /// Runs an async JS function body with named arguments; the result comes back via postMessage.
    private async Task<JsonElement> RunAsync(string body, Dictionary<string, object> args, TimeSpan timeout)
    {
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = tcs;
        string names = string.Join(", ", args.Keys);
        string js =
            "(async () => {" +
            $" const {{ {names} }} = {JsonSerializer.Serialize(args)};" +
            " let result, ok = true;" +
            $" try {{ result = await (async () => {{\n{body}\n}})(); }} catch (e) {{ ok = false; result = {{error: String(e)}}; }}" +
            $" window.chrome.webview.postMessage(JSON.stringify({{ id: '{id}', ok, result }}));" +
            "})();";
        await web.CoreWebView2.ExecuteScriptAsync(js);
        var finished = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        if (finished != tcs.Task) { pending.TryRemove(id, out _); throw new TimeoutException("page didn't respond"); }
        var msg = await tcs.Task;
        return msg.TryGetProperty("result", out var res) ? res : default;
    }

    // ---- Win32: hide the hidden window from Alt+Tab ----

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);

    private static void SetToolWindow(Window w, bool on)
    {
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        ex = on ? (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW : (ex & ~WS_EX_TOOLWINDOW) | WS_EX_APPWINDOW;
        SetWindowLong(h, GWL_EXSTYLE, ex);
    }
}
