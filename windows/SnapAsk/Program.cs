using System;
using System.Drawing;
using Point = System.Drawing.Point;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace SnapAsk;

/// SnapAsk for Windows: press a shortcut, drag over any part of the screen, and ask Claude, ChatGPT or Gemini
/// about it in a floating popup. Each AI runs through its own website, signed in with your account.
public static class Program
{
    private static WinForms.NotifyIcon? tray;
    private static bool capturing;

    [STAThread]
    public static void Main()
    {
        using var single = new Mutex(true, "SnapAsk.SingleInstance", out bool first);
        if (!first) { MessageBox.Show("SnapAsk is already running — look for its icon in the system tray.", "SnapAsk"); return; }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            Log.Write("launched");
            SetupTray();
            if (!HotKeys.Register(Snap))
                tray!.ShowBalloonTip(5000, "SnapAsk", $"{HotKeys.Current.Label} is used by another app. Pick another shortcut from the tray menu.", WinForms.ToolTipIcon.Warning);
            else
                tray!.ShowBalloonTip(4000, "SnapAsk is running", $"Press {HotKeys.Current.Label} to snapshot and ask.", WinForms.ToolTipIcon.Info);
            await ClaudeCli.DetectAsync();
            WebBackend.PrewarmAll();
            if (!Settings.Current.Welcomed) Accounts.ShowWindow();
        };
        app.Exit += (_, _) => { if (tray != null) tray.Visible = false; };
        app.Run();
    }

    public static void Snap()
    {
        if (capturing) return;
        capturing = true;
        try
        {
            var picked = RegionSelector.Pick();
            if (picked is { } p) new Popup(p.image, p.end).Show();
        }
        catch (Exception ex) { Log.Write($"capture failed: {ex}"); }
        finally { capturing = false; }
    }

    // ---- Tray icon & menu ----

    private static void SetupTray()
    {
        tray = new WinForms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "SnapAsk",
            Visible = true,
        };
        tray.DoubleClick += (_, _) => Snap();
        RebuildMenu();
    }

    private static void RebuildMenu()
    {
        var m = new WinForms.ContextMenuStrip();
        m.Items.Add($"Snap && Ask    {HotKeys.Current.Label}", null, (_, _) => Snap());
        m.Items.Add("Setup && accounts…", null, (_, _) => Accounts.ShowWindow());
        m.Items.Add(new WinForms.ToolStripSeparator());

        var model = new WinForms.ToolStripMenuItem("Default model");
        foreach (Provider p in Enum.GetValues(typeof(Provider)))
        {
            var it = new WinForms.ToolStripMenuItem(p.ToString()) { Checked = Settings.Current.Provider == p };
            it.Click += (_, _) => { Settings.Current.Provider = p; Settings.Current.Save(); RebuildMenu(); };
            model.DropDownItems.Add(it);
        }
        m.Items.Add(model);

        var keys = new WinForms.ToolStripMenuItem("Shortcut");
        foreach (var hk in HotKeys.All)
        {
            var it = new WinForms.ToolStripMenuItem(hk.Label) { Checked = HotKeys.Current == hk };
            it.Click += (_, _) =>
            {
                Settings.Current.HotKey = hk.Label; Settings.Current.Save();
                if (!HotKeys.Register(Snap)) MessageBox.Show($"{hk.Label} is already used by another app. Pick a different one.", "SnapAsk");
                RebuildMenu();
            };
            keys.DropDownItems.Add(it);
        }
        m.Items.Add(keys);

        var signIn = new WinForms.ToolStripMenuItem("Sign in / open full chat");
        foreach (Provider p in Enum.GetValues(typeof(Provider)))
            signIn.DropDownItems.Add(p.ToString(), null, (_, _) => Accounts.SignIn(p));
        m.Items.Add(signIn);

        var startup = new WinForms.ToolStripMenuItem("Start with Windows") { Checked = StartsWithWindows };
        startup.Click += (_, _) => { StartsWithWindows = !StartsWithWindows; RebuildMenu(); };
        m.Items.Add(startup);

        m.Items.Add(new WinForms.ToolStripSeparator());
        m.Items.Add("Quit SnapAsk", null, (_, _) => Application.Current.Shutdown());

        var old = tray!.ContextMenuStrip;
        tray.ContextMenuStrip = m;
        old?.Dispose();
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static bool StartsWithWindows
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("SnapAsk") != null; }
        set
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (k == null) return;
            if (value) k.SetValue("SnapAsk", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("SnapAsk", false);
        }
    }

    /// Simple viewfinder icon drawn at runtime (no binary assets needed).
    private static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.White, 3);
            g.DrawLines(pen, new[] { new Point(3, 11), new Point(3, 3), new Point(11, 3) });
            g.DrawLines(pen, new[] { new Point(21, 3), new Point(29, 3), new Point(29, 11) });
            g.DrawLines(pen, new[] { new Point(29, 21), new Point(29, 29), new Point(21, 29) });
            g.DrawLines(pen, new[] { new Point(11, 29), new Point(3, 29), new Point(3, 21) });
            using var dot = new SolidBrush(Color.FromArgb(59, 130, 246));
            g.FillEllipse(dot, 11, 11, 10, 10);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
