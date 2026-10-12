using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The fifteen console variables that used to be declared by
/// <c>game/shared/multiplay_gamerules.cpp</c>.
///
/// <para>
/// <b>Why they are here and not simply gone.</b> That file left the server
/// build, and a file full of <c>ConVar</c> objects is the trap section 8 of
/// <c>plan_server_all.md</c> calls number 3: a ConVar registers itself from
/// <c>.init_array</c>, so nothing NAMES it and every link-time probe reports the
/// file free — while the file is the only reason the setting exists. It is the
/// same thing that happened to <see cref="ForceCameraConVar"/>'s five, at three
/// times the size.
/// </para>
///
/// <para>
/// <b>Four of them are a contract, not a convenience.</b> <c>mp_timelimit</c>,
/// <c>mp_fraglimit</c>, <c>mp_show_voice_icons</c> and
/// <c>mp_mapcycle_empty_timeout_seconds</c> are <c>FCVAR_REPLICATED</c>, and the
/// CLIENT still compiles <c>multiplay_gamerules.cpp</c> and still declares all
/// four. A replicated ConVar that only one side declares stops tracking the
/// server. The other eleven are server-only and are kept so that a server.cfg
/// naming one does not start answering "Unknown command".
/// </para>
///
/// <para>
/// <b>What none of them do is take effect.</b> Every reader of all fifteen was
/// inside <c>multiplay_gamerules.cpp</c> itself — measured across the 142
/// translation units the server compiles, each appears in exactly one file and
/// that file is its own. So this class does not make them work; it stops them
/// vanishing, and it puts them somewhere a managed rule can now read them.
/// <see cref="GameRules"/> already has its own <c>cs_sv_fraglimit</c>, which is
/// the one this mod's rules consult — the same split as
/// <see cref="MpFriendlyFireConVar"/> against <c>cs_sv_friendly_fire</c>.
/// </para>
///
/// <para>
/// Names, defaults, flags and help text are stock's exactly, and
/// <c>MultiplayerConVarTests</c> parses that .cpp rather than trusting this
/// file.
/// </para>
/// </summary>
internal static class MultiplayerConVarsDoc
{
}

/// <summary>
/// How long players may chat after the game is over. Bounded 1..120 in stock,
/// and the bounds are kept: they are the one thing here the engine enforces.
/// </summary>
[ConVar("mp_chattime", "10", Flags = ConVarFlags.Replicated,
        Help = "amount of time players can chat after the game is over",
        Min = 1f, Max = 120f)]
public sealed class MpChatTimeConVar : FloatConVar { }

/// <summary>
/// Game time per map, in minutes. Replicated, so the client's copy tracks it.
///
/// <para>
/// Stock attaches <c>MPTimeLimitCallback</c> under <c>#ifdef GAME_DLL</c>, which
/// nudged the map-cycle logic when the value changed. That logic lived in the
/// same file and left with it; no callback is invented here to replace a
/// consumer that no longer exists.
/// </para>
/// </summary>
[ConVar("mp_timelimit", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "game time per map in minutes")]
public sealed class MpTimeLimitConVar : FloatConVar { }

/// <summary>
/// Kills at which the map ends. Note the C++ variable was named
/// <c>fraglimit</c> while the console name is <c>mp_fraglimit</c> — the console
/// name is what matters and is what is kept.
///
/// <para>
/// <b>This is not the fraglimit this mod's rules read.</b> That is
/// <c>cs_sv_fraglimit</c> on <see cref="GameRules"/>. Setting this one changes
/// nothing, exactly as it changed nothing before the port.
/// </para>
/// </summary>
[ConVar("mp_fraglimit", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "The number of kills at which the map ends")]
public sealed class MpFragLimitConVar : IntConVar { }

/// <summary>Overhead voice icons. Replicated; the client draws them.</summary>
[ConVar("mp_show_voice_icons", "1", Flags = ConVarFlags.Replicated,
        Help = "Show overhead player voice icons when players are speaking.\n")]
public sealed class MpShowVoiceIconsConVar : BoolConVar { }

/// <summary>Cycle to the next map after the server has been empty this long.</summary>
[ConVar("mp_mapcycle_empty_timeout_seconds", "0", Flags = ConVarFlags.Replicated,
        Help = "If nonzero, server will cycle to the next map if it has been empty on the current map for N seconds")]
public sealed class MpMapcycleEmptyTimeoutConVar : FloatConVar { }

// ---------------------------------------------------------------------------
// The eleven server-only ones. FCVAR_NONE and FCVAR_GAMEDLL, no client half,
// and no reader left in this build -- kept so a server.cfg that names one does
// not start failing, and so the settings stay discoverable in `cvarlist`.
// ---------------------------------------------------------------------------

/// <summary>
/// Delay a map change until the HLTV broadcast has finished.
/// <para>
/// THIS AND THE PAIR BELOW IT ARE SETTINGS FOR A FEATURE THIS MOD DROPPED —
/// plan_shim.md step 5c — and they stay REGISTERED anyway, for the reason the
/// block comment above gives: a <c>server.cfg</c> that names one must not start
/// failing. Registering a setting is not the same as honouring it, and nothing
/// in this build reads either: every launch line carries <c>-nohltv</c> and
/// <c>tv_enable 0</c>, so there is no broadcast for a map change to wait on.
/// </para>
/// </summary>
[ConVar("tv_delaymapchange", "0",
        Help = "Delays map change until broadcast is complete")]
public sealed class TvDelayMapChangeConVar : BoolConVar { }

[ConVar("tv_delaymapchange_protect", "1",
        Help = "Protect against doing a manual map change if HLTV is broadcasting and has not caught up with a major game event such as round_end")]
public sealed class TvDelayMapChangeProtectConVar : BoolConVar { }

[ConVar("mp_restartgame", "0",
        Help = "If non-zero, game will restart in the specified number of seconds")]
public sealed class MpRestartGameConVar : IntConVar { }

[ConVar("mp_restartgame_immediate", "0",
        Help = "If non-zero, game will restart immediately")]
public sealed class MpRestartGameImmediateConVar : BoolConVar { }

[ConVar("mp_waitingforplayers_time", "0",
        Help = "WaitingForPlayers time length in seconds")]
public sealed class MpWaitingForPlayersTimeConVar : FloatConVar { }

[ConVar("mp_waitingforplayers_restart", "0",
        Help = "Set to 1 to start or restart the WaitingForPlayers period.")]
public sealed class MpWaitingForPlayersRestartConVar : BoolConVar { }

[ConVar("mp_waitingforplayers_cancel", "0",
        Help = "Set to 1 to end the WaitingForPlayers period.")]
public sealed class MpWaitingForPlayersCancelConVar : BoolConVar { }

[ConVar("mp_clan_readyrestart", "0",
        Help = "If non-zero, game will restart once someone from each team gives the ready signal")]
public sealed class MpClanReadyRestartConVar : BoolConVar { }

[ConVar("mp_clan_ready_signal", "ready",
        Help = "Text that team leader from each team must speak for the match to begin")]
public sealed class MpClanReadySignalConVar : StringConVar { }

/// <summary>
/// The map to change to. Stock's help text is chosen by
/// <c>#if defined( CSTRIKE_DLL ) || defined( TF_DLL )</c>; this is neither, so
/// the <c>#else</c> arm is the one that applies and the one kept.
/// </summary>
[ConVar("nextlevel", "", Flags = ConVarFlags.Notify,
        Help = "If set to a valid map name, will change to this map during the next changelevel")]
public sealed class NextLevelConVar : StringConVar { }
