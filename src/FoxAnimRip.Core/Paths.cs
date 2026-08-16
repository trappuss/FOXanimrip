// SPDX-License-Identifier: MIT
namespace FoxAnimRip;

/// <summary>
/// Where the tool is allowed to write.
///
/// foxanimrip is portable by default: the unpacked assemblies, the name
/// dictionaries, the archive index and the saved settings all go into a
/// <c>data</c> folder beside the executable, so the whole thing can live on a
/// USB stick or a synced folder and leave nothing behind on the machine.
///
/// If that folder is not writable -- someone dropped it in Program Files, or it
/// is on read-only media -- everything falls back to the usual per-user
/// locations instead of failing. <c>--portable</c> and <c>--no-portable</c>
/// force the decision either way.
/// </summary>
public static class Paths
{
    private static string _base;
    private static bool? _forcePortable;

    /// <summary>Marker file: create it beside the exe to insist on portable mode.</summary>
    public const string MarkerFile = "foxanimrip.portable";

    public static void Force(bool portable)
    {
        _forcePortable = portable;
        _base = null;
    }

    public static bool IsPortable { get; private set; }

    /// <summary>The folder everything writable lives under.</summary>
    public static string Data
    {
        get
        {
            if (_base is not null) return _base;

            var beside = Path.Combine(AppContext.BaseDirectory, "data");
            var wantPortable = _forcePortable ?? !LooksInstalled(AppContext.BaseDirectory);

            if (wantPortable && TryPrepare(beside))
            {
                IsPortable = true;
                return _base = beside;
            }

            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData)) appData = Path.GetTempPath();
            var perUser = Path.Combine(appData, "foxanimrip");
            TryPrepare(perUser);
            IsPortable = false;
            return _base = perUser;
        }
    }

    public static string Assemblies => Sub("assemblies");
    public static string CatalogCache => Sub("catalog");
    public static string Settings => Path.Combine(Data, "settings.json");

    /// <summary>
    /// Where FoxBrowser's name dictionaries have to be staged.
    ///
    /// This one is not ours to choose: FoxBrowser's StrCodeNames reads
    /// <c>AppContext.BaseDirectory/dict</c>, i.e. the folder of whatever
    /// executable is running, so it must be exactly there.
    /// </summary>
    public static string DictStaging => Path.Combine(AppContext.BaseDirectory, "dict");

    private static string Sub(string name)
    {
        var path = Path.Combine(Data, name);
        try { Directory.CreateDirectory(path); } catch { }
        return path;
    }

    private static bool TryPrepare(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Program Files and friends: treat as an installed copy, not portable.</summary>
    private static bool LooksInstalled(string dir)
    {
        if (File.Exists(Path.Combine(dir, MarkerFile))) return false;   // explicit opt-in
        foreach (var folder in new[]
                 {
                     Environment.SpecialFolder.ProgramFiles,
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.Windows,
                 })
        {
            string root;
            try { root = Environment.GetFolderPath(folder); }
            catch { continue; }
            if (string.IsNullOrEmpty(root)) continue;
            if (dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>One line for the log, so it is obvious where things are going.</summary>
    public static string Describe()
    {
        var data = Data;          // resolves the mode as a side effect
        return IsPortable
            ? $"portable mode: settings and caches in {data}"
            : $"settings and caches in {data}";
    }
}
