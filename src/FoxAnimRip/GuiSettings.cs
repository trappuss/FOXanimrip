// SPDX-License-Identifier: MIT
using System.Text.Json;

namespace FoxAnimRip.Gui;

/// <summary>Remembers the three things nobody wants to re-enter every run.</summary>
public sealed class GuiSettings
{
    public string FoxBrowserExe { get; set; }
    public string GameRoot { get; set; }
    public string OutDir { get; set; }

    /// <summary>Theme choice: "dark", "light" or "system".</summary>
    public string Theme { get; set; }
    public List<string> Characters { get; set; }

    private static string Path0 => Paths.Settings;

    public static GuiSettings Load()
    {
        try
        {
            if (File.Exists(Path0))
                return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(Path0))
                       ?? new GuiSettings();
        }
        catch { }
        return new GuiSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path0)!);
            File.WriteAllText(Path0, JsonSerializer.Serialize(this));
        }
        catch { }
    }

    public static void Update(Action<GuiSettings> change)
    {
        var settings = Load();
        change(settings);
        settings.Save();
    }
}
