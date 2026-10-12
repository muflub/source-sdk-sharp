using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of the four <c>FCVAR_REPLICATED</c> ConVars that
/// <c>game/shared/multiplay_gamerules.cpp</c> used to declare on this side.
///
/// <para>
/// <b>Four, not fifteen.</b> That file declares fifteen and eleven of them are
/// inside <c>#ifdef GAME_DLL</c> — the HLTV pair, the restart pair, the
/// waiting-for-players trio, the clan-ready pair, <c>nextlevel</c> and
/// <c>mp_mapcycle_empty_timeout_seconds</c>. The client never had those and does
/// not gain them here. What it compiled was <c>mp_chattime</c>,
/// <c>mp_timelimit</c>, <c>mp_fraglimit</c> and <c>mp_show_voice_icons</c>, and
/// those are what is below.
/// </para>
///
/// <para>
/// <b>Why a client copy is required at all.</b> A replicated ConVar is a
/// contract between two DLLs: the server owns the value and the client's copy
/// tracks it, and that only works while both sides declare the name. The server
/// half moved to <c>SourceSharp.Server/MultiplayerConVars.cs</c> when the .cpp
/// left the server build; this is the other end, so the file can leave the
/// client build too.
/// </para>
///
/// <para>
/// <b>A stock oddity, recorded rather than copied.</b>
/// <c>mp_mapcycle_empty_timeout_seconds</c> is declared <c>FCVAR_REPLICATED</c>
/// and sits inside the <c>#ifdef GAME_DLL</c> block, so stock never gives it a
/// client half either. It is replicated to nobody. That is left exactly as it
/// is — inventing a client declaration would be this port changing behaviour
/// while claiming to preserve it.
/// </para>
///
/// <para>
/// Declared in the Client assembly rather than in Shared: a ConVar in Shared
/// registers from both DLLs out of one declaration, which is not the same thing
/// as two modules each declaring the name, and is what
/// <see cref="SourceSharp.Server.ConVars"/> warns about.
/// </para>
/// </summary>
internal static class MultiplayerConVarsDoc
{
}

/// <summary>Client half of <c>mp_chattime</c>. Bounds are stock's, 1..120.</summary>
[ConVar("mp_chattime", "10", Flags = ConVarFlags.Replicated,
        Help = "amount of time players can chat after the game is over",
        Min = 1f, Max = 120f)]
public sealed class ClMpChatTimeConVar : FloatConVar { }

/// <summary>Client half of <c>mp_timelimit</c>.</summary>
[ConVar("mp_timelimit", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "game time per map in minutes")]
public sealed class ClMpTimeLimitConVar : FloatConVar { }

/// <summary>Client half of <c>mp_fraglimit</c>.</summary>
[ConVar("mp_fraglimit", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "The number of kills at which the map ends")]
public sealed class ClMpFragLimitConVar : IntConVar { }

/// <summary>
/// Client half of <c>mp_show_voice_icons</c> — and the one of the four the
/// client actually acts on, since drawing the overhead icon is its job.
/// </summary>
[ConVar("mp_show_voice_icons", "1", Flags = ConVarFlags.Replicated,
        Help = "Show overhead player voice icons when players are speaking.\n")]
public sealed class ClMpShowVoiceIconsConVar : BoolConVar { }
