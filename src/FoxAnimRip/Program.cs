// SPDX-License-Identifier: MIT
using System.Runtime.InteropServices;
using FoxAnimRip.Gui;

namespace FoxAnimRip;

internal static class WindowsProgram
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        // Double-clicked: show the window. Started with arguments from a
        // console: behave like the command-line tool and write back to it.
        if (args.Length > 0)
        {
            AttachConsole(AttachParentProcess);
            var code = Cli.Main(args);
            Console.Out.Flush();
            return code;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
