//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Rules;

/// <summary>
/// Stock's live team-switch kill, <c>CTFPlayer::ChangeTeam</c>'s active-player
/// arm (tf_player.cpp:6687-6696), as an <see cref="ITeamChangeWatcher"/>:
///
/// <code>
/// else // active player
/// {
///     bool bKill = true;
///     if ( bKill &amp;&amp; !IsDead() &amp;&amp; (iOldTeam == TF_TEAM_RED || iOldTeam == TF_TEAM_BLUE) )
///     {
///         // Kill player if switching teams while alive
///         CommitSuicide( false, true );
///     }
/// </code>
///
/// <para>
/// THE GUARDS, read at the moment stock read them — after the team is stored
/// (<c>BaseClass::ChangeTeam</c> at :6657 runs before the arm at :6687, so the
/// death belongs to the new team, and our watcher fires at that same
/// after-the-store moment). The arm itself is taken only when the NEW team is
/// neither UNASSIGNED (:6671) nor SPECTATOR (:6675), and the kill further
/// requires the OLD team to be RED or BLUE (:6692): a dead player, a joiner
/// from spectator, and a departure to spectator all walk past it.
/// </para>
///
/// <para>
/// WHY THE CORE CANNOT DECIDE THIS: its game is HL2MP, which kills nobody on
/// a team change (the absence is <c>docs/divergences.md</c>'s row); the kill
/// is TF2's own, and it hangs on the core's watcher chain. The death is the
/// viewmodel's cure too: the dead player re-joins a class through the class
/// menu, and the redeploy re-asks the skin chain
/// (<see cref="IViewmodelSkinRule"/>, asked per deploy at the arsenal) — so
/// the switch that used to keep wearing the old team's sleeves now dies out of
/// them. The suicide is the house one (<see cref="PlayerSelfCommand"/>,
/// PlayerCheatCommands.cs:95-113): lethal damage, nobody as the attacker,
/// generic type — what stock's <c>CommitSuicide( false, true )</c> asks the
/// damage path for.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class TFChangeTeamKillRule(IEngineDamage damage, IEnginePlayers players)
    : ITeamChangeWatcher, IOrderedHook
{
    /// <summary>The rule's order in the rules' band.</summary>
    public const int Order = TeamRoundOrders.ChangeTeamKill;

    /// <inheritdoc />
    public int HookOrder => Order;

    /// <inheritdoc />
    public void TeamChanged(EntityHandle player, int oldTeam, int newTeam)
    {
        // The arm's two guards, stock's spelling: the new team is a playing
        // one (the active-player else at :6687, past the UNASSIGNED and
        // SPECTATOR arms), the old one was RED or BLUE (:6692), and the
        // player is standing (:6692's !IsDead, read now — as stock read it
        // after the store).
        if (!IsPlayingTeam(oldTeam) || !IsPlayingTeam(newTeam))
            return;

        if (!players.IsAlive(player))
            return;

        // Kill player if switching teams while alive (:6694-6695). The
        // attacker is nobody, which is what makes it read as a suicide.
        damage.Apply(player, EntityHandle.None, PlayerSelfCommand.SuicideDamage, DamageType.Generic);
    }

    private static bool IsPlayingTeam(int team)
        => team is (int)TFTeam.Red or (int)TFTeam.Blue;
}
