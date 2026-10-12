//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The damage-rule cheats — stock's <c>god</c> and <c>buddha</c> console
/// commands, which left the standalone player dead: client.cpp's registered
/// handlers resolve their subject with <c>ToBasePlayer(UTIL_GetCommandClient())</c>,
/// get NULL for a standalone player, and no-op without a word. The stock
/// registrations are now guarded out under SOURCESHARP and the spellings route
/// here through the managed client-command crossing.
///
/// <para>
/// Both are damage RULES, so the state lives on <see cref="SsPlayer"/> and the
/// verdict is <see cref="SsPlayer.DamageTaken"/>, applied in the one place all
/// damage already goes through — <see cref="SsPlayer.OnDamaged"/>. This class
/// is only the toggle: the gate, the flip, and the report. A toggle MUST
/// report its new state — a silent toggle is exactly how the dead commands
/// hid in the first place.
/// </para>
///
/// <para>
/// The sv_cheats gate is on the TOGGLE, as stock's FCVAR_CHEAT was, and it is
/// inside THIS class per the <see cref="ImpulseCheats"/> precedent — never on
/// the command crossing, which once swallowed ordinary commands like lastinv
/// whenever cheats were off. A flag already on stays on if sv_cheats drops to
/// 0 afterwards, exactly as stock's FL_GODMODE flag did.
/// </para>
/// </summary>
public sealed class DamageCheats
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IEntityDirectory _entities;

    public DamageCheats(ILog log, IEntityDirectory entities, IEngineConsole console)
    {

        _log = log;
        _entities = entities;
        CheatsEnabled = () => ImpulseCheats.CheatsOn(NativeConsole.ValueOf(console, "sv_cheats"));
    }

    /// <summary>
    /// The sv_cheats gate. A seam so a test can hold it open or shut; in the
    /// game it reads the engine's convar live per toggle, through the same
    /// fail-closed reading <see cref="ImpulseCheats.CheatsOn"/> gives the
    /// impulses: missing or garbage means SHUT.
    /// </summary>
    public Func<bool> CheatsEnabled { get; set; }

    /// <summary>The god command: toggle invulnerability, out loud.</summary>
    public void ToggleGod(EntityHandle player)
    {
        var subject = Subject(player, "god");

        if (subject == null)
            return;

        subject.GodMode = !subject.GodMode;
        _log.Msg(subject.GodMode ? "godmode ON\n" : "godmode OFF\n");
    }

    /// <summary>The buddha command: toggle can't-die, out loud.</summary>
    public void ToggleBuddha(EntityHandle player)
    {
        var subject = Subject(player, "buddha");

        if (subject == null)
            return;

        subject.BuddhaMode = !subject.BuddhaMode;
        _log.Msg(subject.BuddhaMode ? "Buddha Mode on...\n" : "Buddha Mode off...\n");
    }

    /// <summary>
    /// The gate and the resolve, refused OUT LOUD both ways: the whole point
    /// of this port is that the console never again accepts one of these and
    /// does nothing.
    /// </summary>
    private SsPlayer? Subject(EntityHandle player, string command)
    {
        if (!CheatsEnabled())
        {
            _log.Msg($"Can't use cheat command {command}, unless the server has sv_cheats set to 1.\n");
            return null;
        }

        if (_entities.Get(player) is not SsPlayer subject)
        {
            _log.Msg($"[SourceSharp/Server] {command}: the issuer is not a player\n");
            return null;
        }

        return subject;
    }
}
