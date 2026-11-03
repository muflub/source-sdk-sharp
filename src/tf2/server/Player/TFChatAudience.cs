//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Rules;
using TF2.Shared.Rules;

namespace TF2.Server.Player;

/// <summary>
/// TF2's answer to the say relay's per-listener stage
/// (<see cref="IChatAudience"/>): <c>CTFPlayer::CanHearAndReadChatFrom</c>
/// (tf_player.cpp:19775-19817). A dead talker reaches a living listener only
/// when the round is over (<c>GR_STATE_TEAM_WIN</c> / <c>GR_STATE_GAME_OVER</c>)
/// or <c>tf_gravetalk</c> (tf_gamerules.cpp:1112) says so; every other pair
/// hears, as the base player's plain true does. The same rule is stock's
/// <c>CTFGameRules::TFVoiceManager</c> (tf_gamerules.cpp:7743-7756) for the
/// voice relay, which this port has not built — <c>ClientVoice</c> is a
/// no-op — so the text half is the one that runs.
///
/// <para>
/// Not ported of the same override: the <c>m_iIgnoreGlobalChat</c> arms
/// (no player can ignore chat in this build) and the MvM always-hear arm, it
/// being a mode this port has not. Stock asks the convar only after the
/// round's end check, so a round-end say is heard with the gate off too.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(IChatAudience)])]
public sealed class TFChatAudience(TFGravetalkConVar gravetalk, ITeamRoundState round, IEnginePlayers players)
    : IChatAudience
{
    public bool CanHear(EntityHandle listener, EntityHandle speaker)
    {
        // "if ( pPlayer->m_lifeState != LIFE_ALIVE && m_lifeState == LIFE_ALIVE )"
        if (players.IsAlive(speaker) || !players.IsAlive(listener))
            return true;

        // "Everyone can chat like normal when the round/game ends".
        if (round.State is RoundState.TeamWin or RoundState.GameOver)
            return true;

        return gravetalk.Value;
    }
}
