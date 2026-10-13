using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SnapAsk;

/// Which way Claude is reached: claude.ai (any plan, incl. Free) or the Claude Code CLI (Pro/Max, a bit faster).
public enum ClaudeRoute { Auto, Web, Cli }

public static class ClaudeCli
{
    public static bool SignedIn { get; private set; }
    public static string? Email { get; private set; }

    public static bool InUse => Settings.Current.ClaudeRoute switch
    {
        ClaudeRoute.Cli => true,
        ClaudeRoute.Web => false,
        _ => SignedIn,
    };

    /// Finds claude.exe / claude.cmd on PATH or in the usual install locations.
    public static string? Find()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { Path.Combine(home, ".local", "bin"), Path.Combine(appData, "npm") });
        foreach (var d in dirs)
            foreach (var name in new[] { "claude.exe", "claude.cmd" })
            {
                var p = Path.Combine(d.Trim('"'), name);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    private static ProcessStartInfo StartInfo(string exe, params string[] args)
    {
        ProcessStartInfo psi;
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
        {
            psi = new ProcessStartInfo("cmd.exe");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(exe);
        }
        else psi = new ProcessStartInfo(exe);
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = Encoding.UTF8;
        psi.WorkingDirectory = Path.GetTempPath();
        return psi;
    }

    /// Runs `claude auth status` to see whether the CLI is installed and signed in.
    public static async Task DetectAsync()
    {
        SignedIn = false; Email = null;
        var exe = Find();
        if (exe == null) { Log.Write("claude CLI not found"); return; }
        try
        {
            using var p = Process.Start(StartInfo(exe, "auth", "status"))!;
            string json = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            using var doc = JsonDocument.Parse(json);
            SignedIn = doc.RootElement.TryGetProperty("loggedIn", out var li) && li.GetBoolean();
            Email = doc.RootElement.TryGetProperty("email", out var em) ? em.GetString() : null;
        }
        catch (Exception ex) { Log.Write($"claude auth status failed: {ex.Message}"); }
        Log.Write($"claude CLI signed in={SignedIn}");
    }

    /// Opens a console running `claude auth login`.
    public static void OpenLogin()
    {
        var exe = Find();
        if (exe == null) { Process.Start(new ProcessStartInfo("https://claude.com/claude-code") { UseShellExecute = true }); return; }
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k \"\"{exe}\" auth login\"") { UseShellExecute = true });
    }

    /// One long-lived `claude -p` per popup, fed stream-json on stdin, so follow-ups keep context and tokens stream.
    public sealed class Session : IDisposable
    {
        private readonly Process proc;
        private Action<string>? onDelta;
        private TaskCompletionSource<string?>? turn;   // completes with null (ok) or an error message
        private bool sawDelta;
        private readonly StringBuilder stderr = new();

        public Session()
        {
            var exe = Find() ?? throw new InvalidOperationException("Claude Code CLI not found. Install it from claude.com/claude-code, or set “Claude via” to claude.ai.");
            var psi = StartInfo(exe, "-p", "--input-format", "stream-json", "--output-format", "stream-json",
                "--include-partial-messages", "--verbose", "--no-session-persistence",
                "--tools", "", "--setting-sources", "", "--strict-mcp-config",
                "--system-prompt", Sites.SystemPrompt);
            psi.RedirectStandardInput = true;
            psi.StandardInputEncoding = new UTF8Encoding(false);
            proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) Handle(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            proc.Exited += (_, _) => turn?.TrySetResult($"Claude CLI exited. {Tail()}");
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
        }

        public bool Alive => !proc.HasExited;

        private string Tail() { lock (stderr) { var s = stderr.ToString().Trim(); return s.Length > 400 ? s[^400..] : s; } }

        public Task<string?> AskAsync(string text, byte[]? png, Action<string> delta)
        {
            onDelta = delta;
            sawDelta = false;
            turn = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var content = new System.Collections.Generic.List<object>();
            if (png != null)
                content.Add(new { type = "image", source = new { type = "base64", media_type = "image/png", data = Convert.ToBase64String(png) } });
            content.Add(new { type = "text", text });
            var msg = new { type = "user", message = new { role = "user", content } };
            proc.StandardInput.WriteLine(JsonSerializer.Serialize(msg));
            proc.StandardInput.Flush();
            return turn.Task;
        }

        private void Handle(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                string? type = r.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == "stream_event" && r.TryGetProperty("event", out var ev)
                    && ev.TryGetProperty("type", out var et) && et.GetString() == "content_block_delta"
                    && ev.TryGetProperty("delta", out var d) && d.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta")
                {
                    sawDelta = true;
                    onDelta?.Invoke(d.GetProperty("text").GetString() ?? "");
                }
                else if (type == "result")
                {
                    bool isError = r.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                    string? result = r.TryGetProperty("result", out var rs) ? rs.GetString() : null;
                    if (!isError && !sawDelta && result != null) onDelta?.Invoke(result);
                    turn?.TrySetResult(isError ? (result ?? "Claude returned an error.") : null);
                }
            }
            catch { /* non-JSON noise */ }
        }

        public void Dispose()
        {
            try { if (!proc.HasExited) proc.Kill(true); } catch { }
            proc.Dispose();
        }
    }
}
