// SPDX-License-Identifier: MIT
namespace FoxAnimRip;

/// <summary>
/// The name fragments Fox Engine uses for moving about.
///
/// Clip names are terse and abbreviated -- <c>enemasr_s_wal_atk_dam_nea_f</c>,
/// <c>enetasr_s_dsh_ed_s_rdy_l_l</c> -- with the action in the middle and
/// direction and variant codes trailing. There is no manifest anywhere that says
/// which clips are locomotion, so the practical definition is a list of the
/// tokens that appear in the ones that are: walking, running, dashing, turning,
/// crouching, standing, stopping, idling.
///
/// This is a starting point rather than a truth. It is deliberately a plain list
/// so it can be pasted into <c>--filter-any</c> and edited: run
/// <c>--list-clips</c> on a set, look at what comes back, and add what is
/// missing. Being slightly too broad is the right failure -- an extra clip in
/// the export costs a file, a missing one costs a re-run.
/// </summary>
public static class Locomotion
{
    public static readonly string[] Tokens =
    {
        // standing and waiting
        "idl", "idle", "wait", "rlx", "stnd", "stand",
        // walking and running
        "wal", "wlk", "walk", "_wk", "run", "jog", "dsh", "dash", "spr", "sprint",
        // crouching, crawling, prone
        "crc", "crch", "crouch", "cwl", "sqt", "squat", "crw", "crawl", "prn", "prone",
        // starting, stopping, turning, stepping
        "stp", "step", "trn", "turn", "brk", "start", "_ed", "_st",
        // in-place movement and strafing
        "mv", "move", "str", "strf", "side", "back",
        // climbing, vaulting, ladders, stairs
        "clm", "climb", "vlt", "vault", "ldr", "ladder", "stair", "str_",
    };

    public static string Joined => string.Join(",", Tokens);
}
