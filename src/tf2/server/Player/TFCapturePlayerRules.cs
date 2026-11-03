//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;

namespace TF2.Server.Player;

/// <summary>
/// TF's answers to what a player is to a capture, from the player's
/// <c>tf_player_data</c> carrier: <c>CTFGameRules::PlayerMayCapturePoint</c>,
/// <c>PlayerMayBlockPoint</c> and <c>GetCaptureValueForPlayer</c>
/// (src/game/shared/tf/tf_gamerules.cpp:16835, :16953, :18121).
/// <list type="bullet">
/// <item>May cap: not stealthed; not invulnerable or megahealed (outside MvM,
/// which this build never runs); not phased; not control-stunned; not
/// disguised as the other team. A player with no carrier is not a TF player
/// and may not.</item>
/// <item>May block (while not capping): invulnerable.</item>
/// <item>Counts as: a scout 2 under <c>mp_capstyle</c> 1, else 10; everyone
/// else the base class's 1, plus the player's <c>add_player_capturevalue</c>
/// attribute.</item>
/// </list>
/// Not ported (deferred.tsv): the training and creep arms.
/// </summary>
[SideService(Side.Server, As = [typeof(ICapturePlayerRules)])]
public sealed class TFCapturePlayerRules(TFPlayerCarriers carriers, ITeams teams, AttributeHooks hooks,
                                         ITFWeaponOwners owners, MpCapStyleConVar? capStyle = null)
    : ICapturePlayerRules
{
    /// <summary><c>add_player_capturevalue</c>, the capture value's hook (tf_gamerules.cpp:18156).</summary>
    public const string CaptureValueAttrib = "add_player_capturevalue";
    public bool PlayerMayCapturePoint(EntityHandle player, int pointIndex)
    {
        if (carriers.Get(player.EntryIndex) is not { } state)
            return false;

        // Disguised and invisible spies cannot capture points.
        if (state.IsStealthed())
            return false;

        if (state.IsInvulnerable() || state.InCond(TFCond.Megaheal))
            return false;

        if (state.InCond(TFCond.Phase))
            return false;

        // Control-stunned players cannot capture points (IsControlStunned,
        // tf_player_shared.cpp:9952): the active stun entry, the stunned
        // condition and the controls bit — a movement-only stun leaves the
        // cap hand working. The building pickup reads the same words
        // (TFBuildingPickup).
        if (state.StunIndex >= 0
            && state.InCond(TFCond.Stunned)
            && ((TFStunFlags)state.StunFlags & TFStunFlags.Controls) != 0)
            return false;

        // Spies disguised as the enemy team cannot capture points.
        if (state.InCond(TFCond.Disguised) && state.DisguiseTeam != teams.Team(player))
            return false;

        return true;
    }

    public bool PlayerMayBlockPoint(EntityHandle player, int pointIndex)
        => carriers.Get(player.EntryIndex) is { } state && state.IsInvulnerable();

    public int CaptureValueForPlayer(EntityHandle player)
    {
        int value = 1;

        if (carriers.Get(player.EntryIndex)?.Class() == TFClass.Scout)
        {
            // Scouts count for 2 people in timebased capping, else they cap all points on their own.
            value = (capStyle?.Value ?? 1) == 1 ? 2 : 10;
        }

        // CALL_ATTRIB_HOOK_INT_ON_OTHER on the player (tf_gamerules.cpp:18156),
        // after the scout arm as stock's order runs.
        return hooks.Int(value, CaptureValueAttrib, owners.ManagerOf(player.EntryIndex));
    }
}
