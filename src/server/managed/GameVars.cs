//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The five replicated ConVars that used to be
/// <c>game/shared/gamevars_shared.cpp</c>.
///
/// <para>
/// That file defined nothing but these five and one callback, and
/// <c>nm</c> found ZERO consumers for it — which is exactly the shape section 8
/// of <c>plan_server_all.md</c> calls trap 3, "static constructors are
/// invisible". A ConVar is registered from <c>.init_array</c>, so a file that
/// holds only ConVars looks free to a link-time probe and is not: evicting it
/// without replacing them deletes five settings with no error anywhere.
/// </para>
///
/// <para>
/// <b>Why they had to be re-declared rather than dropped.</b> Every SERVER-side
/// reader of all five — <c>player.cpp</c>, <c>teamplay_gamerules.cpp</c>,
/// <c>hl2mp_player.cpp</c>, the <c>tf/</c> pair — has already left this build,
/// so nothing here reads them. But <c>FCVAR_REPLICATED</c> is a contract
/// between two DLLs: the client still compiles <c>gamevars_shared.cpp</c> and
/// declares all five, and a replicated ConVar that only one side declares stops
/// tracking the server. <c>mp_forcecamera</c> in particular is still read live
/// by the client's <c>MapOverview.cpp</c>. Dropping them would mean a server
/// admin setting <c>mp_friendlyfire 1</c> no longer reaching anybody, with
/// nothing said about it.
/// </para>
///
/// <para>
/// So the names, defaults, flags and help strings below are stock's exactly,
/// and <c>GameVarsTests</c> holds them against the .cpp the client still
/// compiles rather than against this file's own opinion.
/// </para>
///
/// <para>
/// They are declared in the Server assembly, not Shared: a ConVar in Shared
/// would register from both DLLs and Source refuses the same name twice. See
/// <see cref="ConVars"/>.
/// </para>
/// </summary>
internal static class GameVarsDoc
{
}

/// <summary>
/// Which spectator modes a dead player may use. Stock's default outside
/// CSTRIKE is 1 — <c>OBS_ALLOW_TEAM</c>.
///
/// <para>
/// The clamp is stock's <c>MPForceCameraCallback</c>, and it was
/// <c>#ifdef GAME_DLL</c> — the SERVER owns the correction, the client only
/// reads what it is sent. Out-of-range becomes <c>OBS_ALLOW_TEAM</c> rather
/// than the nearest bound, which is stock's choice and is not obviously the
/// same thing: <c>-1</c> and <c>99</c> both land on 1.
/// </para>
/// </summary>
[ConVar("mp_forcecamera", "1", Help = "Restricts spectator modes for dead players",
        Flags = ConVarFlags.Replicated)]
public sealed class ForceCameraConVar : IntConVar
{
    /// <summary>allow all modes, all targets.</summary>
    public const int AllowAll = 0;
    /// <summary>allow only own team and first person, no PIP.</summary>
    public const int AllowTeam = 1;
    /// <summary>no spectating after death — fixed, and faded to black.</summary>
    public const int AllowNone = 2;
    /// <summary>one past the last mode. Matches OBS_ALLOW_NUM_MODES.</summary>
    public const int NumModes = 3;

    protected override void OnChanged(int oldValue, int newValue)
    {
        // Stock re-reads the convar rather than trusting the value it was
        // handed, and writes through SetValue so every other reader sees the
        // corrected one. Same here: this re-enters OnChanged once with an
        // in-range value and then stops.
        if (newValue < AllowAll || newValue >= NumModes)
            SetValue(AllowTeam);
    }
}

/// <summary>
/// Whether the server allows spectator mode at all.
///
/// <para>
/// Declared "1.0" rather than "1", which looks like a typo and is stock's
/// literal default string. It is left alone because a ConVar's default is
/// visible: <c>cvarlist</c>, <c>help mp_allowspectators</c> and a reset all
/// show it, and "fixing" it is a difference between this build and every other
/// Source game for no gain.
/// </para>
/// </summary>
[ConVar("mp_allowspectators", "1.0",
        Help = "toggles whether the server allows spectator mode or not",
        Flags = ConVarFlags.Replicated)]
public sealed class AllowSpectatorsConVar : BoolConVar
{
}

/// <summary>
/// Stock's replicated friendly-fire flag.
///
/// <para>
/// <b>This is NOT the setting this mod's rules read.</b> That is
/// <see cref="FriendlyFireConVar"/> — <c>cs_sv_friendly_fire</c> — which
/// <see cref="GameRules"/> holds alongside the "are they teammates" rule
/// because the two are halves of one decision. The two names are one letter
/// apart in meaning and worlds apart in effect, so it is worth being blunt:
/// setting <c>mp_friendlyfire</c> on this server changes nothing, and it
/// changed nothing before this class existed either.
/// </para>
///
/// <para>
/// It is declared anyway because <c>FCVAR_REPLICATED</c> is a contract between
/// two DLLs and the client still declares it from
/// <c>gamevars_shared.cpp</c>. Its stock help text is kept WORD FOR WORD rather
/// than annotated, because <c>GameVarsTests</c> holds every string here against
/// that .cpp — a help string improved on this side would be a difference
/// between the two declarations of one replicated ConVar, which is the thing
/// this class exists to prevent.
/// </para>
/// </summary>
[ConVar("mp_friendlyfire", "0",
        Help = "Allows team members to injure other members of their team",
        Flags = ConVarFlags.Replicated | ConVarFlags.Notify)]
public sealed class MpFriendlyFireConVar : BoolConVar
{
}

/// <summary>Fade a player's screen to black when they die.</summary>
[ConVar("mp_fadetoblack", "0",
        Help = "fade a player's screen to black when he dies",
        Flags = ConVarFlags.Replicated | ConVarFlags.Notify)]
public sealed class FadeToBlackConVar : BoolConVar
{
}

/// <summary>
/// Whether a hud hint plays a sound. Stock declares it with no help text at
/// all, and that is kept: an invented help string would read as documentation
/// of a decision nobody made.
/// </summary>
[ConVar("sv_hudhint_sound", "1", Flags = ConVarFlags.Replicated)]
public sealed class HudHintSoundConVar : BoolConVar
{
}

/// <summary>
/// The server's half of props_shared.cpp's replicated per-frame piece limit
/// (the client's breaks read it: SourceSharp.Client's
/// PropsBreakMaxPiecesPerFrameConVar). The server's own breaks do not read it
/// yet.
/// </summary>
[ConVar("props_break_max_pieces_perframe", "-1", Flags = ConVarFlags.Replicated,
        Help = "Maximum prop breakable piece count per frame (-1 = model default)")]
public sealed class PropsBreakMaxPiecesPerFrameConVar : IntConVar
{
}
