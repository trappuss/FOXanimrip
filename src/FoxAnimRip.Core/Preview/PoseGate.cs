// SPDX-License-Identifier: MIT
namespace FoxAnimRip.Preview;

/// <summary>
/// The lock every caller of FoxBrowser's animation solve must hold.
///
/// <c>AnimSkinner.BuildPalette</c> hands its intermediate results back through
/// static fields -- <c>LastAnimWorld</c>, <c>LastBonePos</c>, <c>SubTension</c>,
/// <c>IkLog</c> -- and <c>ExportBake.FromGani</c> reads <c>LastAnimWorld</c>
/// straight after calling it. Two threads in there at once do not throw; they
/// quietly hand each other the wrong skeleton. Before the preview existed the
/// export loop was the only caller and ran on one thread, so the hazard was
/// theoretical. Playing a clip while an export runs makes it real.
///
/// The lock is coarse on purpose. Posing a skeleton is a fraction of a
/// millisecond, and correctness here is worth more than overlap.
/// </summary>
public static class PoseGate
{
    private static readonly object Gate = new();

    public static T Run<T>(Func<T> work)
    {
        lock (Gate) return work();
    }

    public static void Run(Action work)
    {
        lock (Gate) work();
    }
}
