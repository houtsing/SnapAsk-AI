using System;
using System.IO;
using System.Text.Json;

namespace SnapAsk;

public sealed class Settings
{
    public Provider Provider { get; set; } = Provider.Claude;
    public string HotKey { get; set; } = HotKeys.All[0].Label;

    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapAsk");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static Settings Current { get; private set; } = Load();

    private static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
        catch { /* settings are a convenience; ignore write failures */ }
    }
}
