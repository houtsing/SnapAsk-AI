using System;
using System.IO;

namespace SnapAsk;

/// %LOCALAPPDATA%\SnapAsk\SnapAsk.log
public static class Log
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapAsk");

    public static void Write(string msg)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Path.Combine(Dir, "SnapAsk.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }
}
