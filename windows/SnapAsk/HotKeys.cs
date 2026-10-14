using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace SnapAsk;

public sealed record HotKeyChoice(string Label, uint Modifiers, uint Key);

/// System-wide shortcut via RegisterHotKey on a message-only window.
public static class HotKeys
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312, ID = 0x534E;

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

    private static HwndSource? source;
    private static Action? onPressed;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// Returns false if another app already owns the shortcut.
    public static bool Register(Action pressed)
    {
        onPressed = pressed;
        if (source == null)
        {
            source = new HwndSource(new HwndSourceParameters("SnapAskHotKey") { ParentWindow = new IntPtr(-3) }); // HWND_MESSAGE
            source.AddHook(Hook);
        }
        UnregisterHotKey(source.Handle, ID);
        var hk = Current;
        bool ok = RegisterHotKey(source.Handle, ID, hk.Modifiers | MOD_NOREPEAT, hk.Key);
        Log.Write($"RegisterHotKey {hk.Label} ok={ok} err={Marshal.GetLastWin32Error()}");
        return ok;
    }

    private static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == ID) { handled = true; onPressed?.Invoke(); }
        return IntPtr.Zero;
    }
}
