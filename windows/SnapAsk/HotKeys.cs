using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SnapAsk;

public sealed record HotKeyChoice(string Label, uint Modifiers, uint Key);

/// System-wide shortcut via RegisterHotKey on a message-only window.
public static class HotKeys
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312, ID = 0x534E, RECALL_ID = 0x534F;

    public static readonly HotKeyChoice[] All =
    {
        new("Ctrl+Alt+S", MOD_CONTROL | MOD_ALT, 0x53),
        new("Ctrl+Shift+Space", MOD_CONTROL | MOD_SHIFT, 0x20),
        new("Alt+Shift+A", MOD_ALT | MOD_SHIFT, 0x41),
        new("Ctrl+Shift+2", MOD_CONTROL | MOD_SHIFT, 0x32),
        new("Ctrl+Alt+Shift+S", MOD_CONTROL | MOD_ALT | MOD_SHIFT, 0x53),
    };

    public static HotKeyChoice Current =>
        Array.Find(All, h => h.Label == Settings.Current.HotKey) ?? All[0];

    /// Brings the last popup back in front of your work (press again to send it back). Key 0 = off.
    public static readonly HotKeyChoice[] RecallAll =
    {
        new("Ctrl+Alt+A", MOD_CONTROL | MOD_ALT, 0x41),
        new("Ctrl+Alt+R", MOD_CONTROL | MOD_ALT, 0x52),
        new("Ctrl+Shift+1", MOD_CONTROL | MOD_SHIFT, 0x31),
        new("Alt+Shift+S", MOD_ALT | MOD_SHIFT, 0x53),
        new("Off", 0, 0),
    };

    public static HotKeyChoice Recall =>
        Array.Find(RecallAll, h => h.Label == Settings.Current.RecallHotKey) ?? RecallAll[0];

    private static HwndSource? source;
    private static Action? onPressed, onRecall;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// Returns false if another app already owns the shortcut.
    public static bool Register(Action pressed)
    {
        onPressed = pressed;
        Source();
        UnregisterHotKey(source!.Handle, ID);
        var hk = Current;
        bool ok = RegisterHotKey(source.Handle, ID, hk.Modifiers | MOD_NOREPEAT, hk.Key);
        Log.Write($"RegisterHotKey {hk.Label} ok={ok} err={Marshal.GetLastWin32Error()}");
        return ok;
    }

    /// Returns false if another app already owns the shortcut.
    public static bool RegisterRecall(Action pressed)
    {
        onRecall = pressed;
        Source();
        UnregisterHotKey(source!.Handle, RECALL_ID);
        var hk = Recall;
        if (hk.Key == 0) return true;
        bool ok = RegisterHotKey(source.Handle, RECALL_ID, hk.Modifiers | MOD_NOREPEAT, hk.Key);
        Log.Write($"RegisterHotKey (recall) {hk.Label} ok={ok} err={Marshal.GetLastWin32Error()}");
        return ok;
    }

    private static void Source()
    {
        if (source != null) return;
        source = new HwndSource(new HwndSourceParameters("SnapAskHotKey") { ParentWindow = new IntPtr(-3) }); // HWND_MESSAGE
        source.AddHook(Hook);
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == ID) { handled = true; onPressed?.Invoke(); }
        if (msg == WM_HOTKEY && wParam.ToInt32() == RECALL_ID) { handled = true; onRecall?.Invoke(); }
        return IntPtr.Zero;
    }
}
