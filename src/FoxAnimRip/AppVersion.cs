// SPDX-License-Identifier: MIT
using System.Reflection;

namespace FoxAnimRip;

/// <summary>
/// What build this is.
///
/// Shown in the window title, the browser title and the first line of the log,
/// because "is the fix in the copy I am running?" is otherwise unanswerable
/// without diffing binaries -- and gets asked every single time something is
/// still not working after an update.
/// </summary>
public static class AppVersion
{
    private static string _cached;

    public static string Current => _cached ??= Read();

    private static string Read()
    {
        try
        {
            var informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Split('+')[0];      // drop the git hash

            var version = typeof(AppVersion).Assembly.GetName().Version;
            if (version is not null) return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch { }
        return "dev";
    }
}
