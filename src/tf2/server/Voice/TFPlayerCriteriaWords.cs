//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Player;

namespace TF2.Server.Voice;

/// <summary>
/// The player words every concept is asked with:
/// <c>CTFPlayer::ModifyOrAppendCriteria</c> (server/tf/tf_player.cpp:19212-19274)
/// as far as this build's carrier reads them — the class, the disguise's, the
/// <c>disguiseclass</c> modifier winning the lookup (:19219-19231) — and
/// <c>OnRedTeam</c> decided by the disguise too (:19233-19238) — then the
/// condition words. The stats, weapon-role and context criteria are
/// divergences (docs/divergences.md, the voice-menu row).
/// </summary>
internal static class TFPlayerCriteriaWords
{
    /// <summary>Appends the words; <paramref name="team"/> is the player's real team.</summary>
    public static void Append(TFPlayerNetState? state, int team, ITeamRoundState? round, ResponseCriteriaSet criteria)
    {
        if (state is null)
            return;

        // The class, the disguise's where there is one (stock's else arm
        // names it from g_aPlayerClassNames_NonLocalized).
        TFClass shown = state.InCond(TFCond.Disguised) && state.DisguiseAs() != TFClass.Undefined
            ? state.DisguiseAs()
            : state.Class();
        if (shown != TFClass.Undefined)
            criteria.Append("playerclass", TFClassNames.NonLocalized(shown));

        // OnRedTeam, the disguise's team where there is one (:19233-19238).
        team = state.InCond(TFCond.Disguised) ? state.DisguiseTeam : team;
        criteria.Append("OnRedTeam", (TFTeam)team == TFTeam.Red ? "1" : "0");

        bool disguised = state.InCond(TFCond.Disguised);
        criteria.Append("disguised", disguised ? "1" : "0");
        criteria.Append("cloaked", state.IsStealthed() || state.InCond(TFCond.StealthedBlink) ? "1" : "0");
        criteria.Append("invulnerable", state.IsInvulnerable() ? "1" : "0");
        criteria.Append("beinghealed", state.InCond(TFCond.HealthBuff) ? "1" : "0");
        criteria.Append("waitingforplayers", round is { } r && r.InWaitingForPlayers ? "1" : "0");

        // IsControlStunned / IsSnared over the carried stun entry, the same
        // reading TFMovementRules makes (TFMovementRules.cs, the stock gates
        // at tf_player_shared.cpp:9952-9986).
        bool stunned = state.StunIndex >= 0 && state.InCond(TFCond.Stunned);
        var stunFlags = stunned ? (TFStunFlags)state.StunFlags : TFStunFlags.None;
        bool controlStunned = stunned && (stunFlags & TFStunFlags.Controls) != 0;
        criteria.Append("stunned", controlStunned ? "1" : "0");
        criteria.Append("snared", stunned && !controlStunned ? "1" : "0");
        criteria.Append("dodging", state.InCond(TFCond.Phase) || state.InCond(TFCond.PasstimeInterception) ? "1" : "0");
        criteria.Append("doublejumping", state.AirDash > 0 ? "1" : "0");
    }
}
