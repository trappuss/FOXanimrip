// SPDX-License-Identifier: MIT
// Console-only entry point: the same CLI as foxanimrip.exe, without the window.
// Useful on Linux and macOS, and for automated testing.
namespace FoxAnimRip;

internal static class HeadlessProgram
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine(Cli.Usage);
            return 0;
        }
        return Cli.Main(args);
    }
}
