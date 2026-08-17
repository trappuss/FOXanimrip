// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;

namespace FoxAnimRip;

/// <summary>
/// One Fox Engine title, and how to recognise an install of it.
///
/// Nothing here is baked into the export path: the tool works from whatever
/// archives FoxBrowser can open (<c>.dat</c>, <c>.g0s</c>, <c>.qar</c>, with
/// <c>.fpk</c> / <c>.fpkd</c> / <c>.pftxs</c> nested inside). Profiles exist so
/// the GUI can say "Ground Zeroes" instead of asking someone to find a folder
/// full of .g0s files.
/// </summary>
public sealed class GameProfile
{
    public string Id = "";
    public string DisplayName = "";
    /// <summary>Executable names that identify the install folder.</summary>
    public string[] Executables = Array.Empty<string>();
    /// <summary>Folder names Steam and the installers commonly use.</summary>
    public string[] FolderNames = Array.Empty<string>();
    /// <summary>Where the archives sit relative to the install root.</summary>
    public string[] ArchiveSubdirs = { "" };
    /// <summary>True when this profile has been exercised against a real install.</summary>
    public bool Verified;
    public string Notes = "";

    public static readonly GameProfile GroundZeroes = new()
    {
        Id = "gz",
        DisplayName = "MGSV: Ground Zeroes",
        Executables = new[] { "MgsGroundZeroes.exe" },
        FolderNames = new[]
        {
            "MGS_GROUND_ZEROES", "MGSV Ground Zeroes",
            "Metal Gear Solid V - Ground Zeroes", "METAL GEAR SOLID V GROUND ZEROES",
        },
        ArchiveSubdirs = new[] { "" },
        Verified = true,
    };

    public static readonly GameProfile PhantomPain = new()
    {
        Id = "tpp",
        DisplayName = "MGSV: The Phantom Pain",
        Executables = new[] { "mgsvtpp.exe", "MGSVTPP.exe" },
        FolderNames = new[]
        {
            "MGS_TPP", "MGSV The Phantom Pain",
            "METAL GEAR SOLID V THE PHANTOM PAIN",
        },
        // TPP keeps the shipped archives in master\, and the streamed chunks
        // beside them; master\0\ holds the patch layers. mgo\ is a separate
        // tree for Metal Gear Online -- a different game as far as the file
        // layout is concerned, and the only place the male and female avatar
        // models and their motions live. Leaving it out made those invisible.
        ArchiveSubdirs = new[] { "master", "mgo", "" },
        Verified = true,
    };

    public static readonly GameProfile Survive = new()
    {
        Id = "survive",
        DisplayName = "Metal Gear Survive",
        Executables = new[] { "MetalGearSurvive.exe", "mgsurvive.exe" },
        FolderNames = new[] { "Metal Gear Survive", "MGSurvive" },
        ArchiveSubdirs = new[] { "master", "" },
        Verified = false,
        Notes = "Fox Engine, same archive containers. Not tested against a real "
              + "install; if FoxBrowser can browse it, this can export from it.",
    };

    public static readonly GameProfile Custom = new()
    {
        Id = "custom",
        DisplayName = "Other / custom folder",
        ArchiveSubdirs = new[] { "", "master" },
        Notes = "Any folder holding Fox Engine .dat / .g0s / .qar archives.",
    };

    public static readonly GameProfile[] All =
        { GroundZeroes, PhantomPain, Survive, Custom };

    public static GameProfile ById(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Custom;
}

/// <summary>An install found on disk.</summary>
public sealed record GameInstall(GameProfile Profile, string Root, int ArchiveCount)
{
    public string Label => ArchiveCount > 0
        ? $"{Profile.DisplayName}  ({ArchiveCount} archive{(ArchiveCount == 1 ? "" : "s")})"
        : Profile.DisplayName;

    public override string ToString() => $"{Label} - {Root}";
}

public static class GameFinder
{
    /// <summary>Root archive containers FoxBrowser can open.</summary>
    public static readonly string[] ArchiveExtensions = { ".dat", ".g0s", ".qar" };

    /// <summary>Texture archives are huge and hold no models or animation.</summary>
    private static bool IsUsefulArchive(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("texture", StringComparison.OrdinalIgnoreCase)) return false;
        // Installer leftovers that happen to end in .dat
        if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) return false;
        return ArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    /// <summary>Every archive under <paramref name="root"/> a profile cares about.</summary>
    public static List<string> ArchivesIn(string root, GameProfile profile)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(root)) return found;

        if (File.Exists(root))
        {
            if (IsUsefulArchive(root)) found.Add(root);
            return found;
        }
        if (!Directory.Exists(root)) return found;

        var subdirs = profile?.ArchiveSubdirs ?? new[] { "" };
        foreach (var sub in subdirs)
        {
            var dir = sub.Length == 0
                ? root
                : Path.Combine(root, sub.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir)) continue;

            // A named data folder (TPP's master\) also holds patch layers a
            // level or two down -- master\0\, master\1\MGSVTUPDATEV0110\ --
            // so sweep those too. The game root itself is only read flat, to
            // avoid dragging in unrelated .dat files from the whole install.
            var options = sub.Length == 0
                ? new EnumerationOptions()
                : new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    MaxRecursionDepth = 2,
                    IgnoreInaccessible = true,
                };
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", options))
                    if (IsUsefulArchive(file)) found.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        if (found.Count == 0)
        {
            // Unknown layout: look a couple of levels down before giving up.
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*",
                             new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 }))
                    if (IsUsefulArchive(file)) found.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Which title is this folder, judged by the executable in it.</summary>
    public static GameProfile Identify(string root)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return GameProfile.Custom;
        foreach (var profile in GameProfile.All)
        {
            if (profile.Executables.Length == 0) continue;
            foreach (var exe in profile.Executables)
                if (File.Exists(Path.Combine(root, exe)))
                    return profile;
        }
        // No executable (a copied-out data folder): guess from the containers.
        try
        {
            if (Directory.EnumerateFiles(root, "*.g0s").Any())
                return GameProfile.GroundZeroes;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return GameProfile.Custom;
    }

    /// <summary>Everything we can find, best guesses first, de-duplicated.</summary>
    public static List<GameInstall> Detect(IEnumerable<string> extraRoots = null)
    {
        var candidates = new List<string>();

        foreach (var lib in SteamLibraries())
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            foreach (var profile in GameProfile.All)
                foreach (var folder in profile.FolderNames)
                {
                    var path = Path.Combine(common, folder);
                    if (Directory.Exists(path)) candidates.Add(path);
                }
            // Also accept a folder whose name we do not know but whose exe we do.
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(common))
                    if (Identify(dir) != GameProfile.Custom) candidates.Add(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var root in FoxBrowserRoots()) candidates.Add(root);
        if (extraRoots is not null) candidates.AddRange(extraRoots.Where(r => !string.IsNullOrEmpty(r)));

        var results = new List<GameInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in candidates)
        {
            var root = raw;
            if (File.Exists(root)) root = Path.GetDirectoryName(root) ?? root;
            if (!Directory.Exists(root)) continue;
            var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            if (!seen.Add(full)) continue;
            var profile = Identify(full);
            var count = ArchivesIn(full, profile).Count;
            if (count == 0) continue;
            results.Add(new GameInstall(profile, full, count));
        }

        return results
            .OrderByDescending(r => r.Profile.Verified)
            .ThenByDescending(r => r.ArchiveCount)
            .ToList();
    }

    /// <summary>Steam library roots, from the Steam config Steam itself keeps.</summary>
    public static List<string> SteamLibraries()
    {
        var libraries = new List<string>();
        var steamRoots = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            foreach (var guess in new[]
                     {
                         @"C:\Program Files (x86)\Steam",
                         @"C:\Program Files\Steam",
                     })
                if (Directory.Exists(guess)) steamRoots.Add(guess);

            try
            {
                var fromRegistry = ReadSteamPathFromRegistry();
                if (!string.IsNullOrEmpty(fromRegistry) && Directory.Exists(fromRegistry))
                    steamRoots.Insert(0, fromRegistry);
            }
            catch { }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var guess in new[]
                     {
                         Path.Combine(home, ".steam", "steam"),
                         Path.Combine(home, ".local", "share", "Steam"),
                     })
                if (Directory.Exists(guess)) steamRoots.Add(guess);
        }

        foreach (var steam in steamRoots)
        {
            libraries.Add(steam);
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                // "path"    "D:\\SteamLibrary"
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf),
                             "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase))
                {
                    var path = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(path)) libraries.Add(path);
                }
            }
            catch (IOException) { }
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ReadSteamPathFromRegistry()
    {
        if (!OperatingSystem.IsWindows()) return "";
#pragma warning disable CA1416
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string ?? "";
#pragma warning restore CA1416
    }

    /// <summary>Whatever FoxBrowser is already pointed at.</summary>
    public static List<string> FoxBrowserRoots()
    {
        var roots = new List<string>();
        try
        {
            FoxBrowser.AppSettings.Load();
            foreach (var value in new[]
                     {
                         FoxBrowser.AppSettings.Current.GameFolder,
                         FoxBrowser.AppSettings.Current.RootFolder,
                     })
            {
                if (string.IsNullOrEmpty(value)) continue;
                roots.Add(File.Exists(value) ? Path.GetDirectoryName(value)! : value);
            }
        }
        catch { /* FoxBrowser may never have been run */ }
        return roots;
    }

    /// <summary>Find FoxBrowser.exe without making the user hunt for it.</summary>
    public static string FindFoxBrowser(string hint = "")
    {
        var tried = new List<string>();
        if (!string.IsNullOrEmpty(hint)) tried.Add(hint);
        tried.Add(Path.Combine(AppContext.BaseDirectory, "FoxBrowser.exe"));

        var parent = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
            tried.Add(Path.Combine(parent, "FoxBrowser.exe"));

        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                 })
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
            try
            {
                foreach (var hit in Directory.EnumerateFiles(folder, "FoxBrowser.exe",
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 MaxRecursionDepth = 4,
                                 IgnoreInaccessible = true,
                             }))
                {
                    tried.Add(hit);
                    break;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return tried.FirstOrDefault(File.Exists) ?? "";
    }
}
