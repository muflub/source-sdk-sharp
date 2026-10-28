using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// The client's half of the thirteen <c>FCVAR_REPLICATED</c> ConVars
/// <see cref="SourceSharp.Server"/>'s <c>MoveVars.cs</c> declares, plus one
/// <c>FCVAR_USERINFO</c> ConVar that only ever existed on this side.
///
/// <para>
/// <b>Why a client copy is required at all.</b> A replicated ConVar is a
/// contract between two DLLs: the server owns the value and the client's copy
/// tracks it, and that only works while both sides declare the name.
/// <c>game/shared/movevars_shared.cpp</c> and
/// <c>game/shared/takedamageinfo.cpp</c> were compiled into BOTH DLLs, so each
/// of these names was declared twice — once per side — and this file is the
/// half that used to come from the client's compilation of those files.
/// </para>
///
/// <para>
/// <b>A client half that is missing is silent.</b> The client's copy simply
/// sits at whatever default it was given and never tracks the server, which is
/// a prediction bug in one process that no compile step, no link step and no
/// single-player test can see. That is why the two files are written as mirrors
/// and why <c>MoveVarsMirrorTests</c> compares them name by name, default by
/// default and flag by flag rather than trusting that two lists written on the
/// same afternoon agree.
/// </para>
///
/// <para>
/// <b>Declared in the Client assembly rather than in Shared</b>, for the reason
/// <c>SourceSharp.Abi/ConVar.cs</c> gives: a class in Shared is discovered from
/// both ALCs and registered twice out of one declaration, which is not two
/// modules each declaring a name. <c>SourceSharp.Client/MultiplayerConVars.cs</c>
/// is the same pattern for the same reason.
/// </para>
/// </summary>
internal static class ClientMoveVarsDoc
{
}

/// <summary>Client half of <c>sv_backspeed</c>.</summary>
[ConVar("sv_backspeed", "0.6", Flags = ConVarFlags.Archive | ConVarFlags.Replicated,
        Help = "How much to slow down backwards motion")]
public sealed class SvBackspeedConVar : FloatConVar { }

/// <summary>Client half of <c>sv_footsteps</c>.</summary>
[ConVar("sv_footsteps", "1", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Play footstep sound for players")]
public sealed class SvFootstepsConVar : BoolConVar { }

/// <summary>Client half of <c>sv_wateraccelerate</c>.</summary>
[ConVar("sv_wateraccelerate", "10", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvWaterAccelerateConVar : FloatConVar { }

/// <summary>Client half of <c>sv_waterdist</c>.</summary>
[ConVar("sv_waterdist", "12", Flags = ConVarFlags.Replicated,
        Help = "Vertical view fixup when eyes are near water plane.")]
public sealed class SvWaterDistConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// hl_gamemovement.cpp's three ladder knobs, read by the useable-ladder
// movement (GameMovement.Ladders) through this side's ladder world.
//-----------------------------------------------------------------------------

/// <summary>Client half of <c>sv_autoladderdismount</c>: get off at a ladder's end without +USE.</summary>
[ConVar("sv_autoladderdismount", "1", Flags = ConVarFlags.Replicated,
        Help = "Automatically dismount from ladders when you reach the end (don't have to +USE).")]
public sealed class SvAutoLadderDismountConVar : BoolConVar { }

/// <summary>Client half of <c>sv_ladderautomountdot</c>: how nearly along a ladder's axis a look auto-mounts it.</summary>
[ConVar("sv_ladderautomountdot", "0.4", Flags = ConVarFlags.Replicated,
        Help = "When auto-mounting a ladder by looking up its axis, this is the tolerance for looking now directly along the ladder axis.")]
public sealed class SvLadderAutoMountDotConVar : FloatConVar { }

/// <summary>Client half of <c>sv_ladder_useonly</c>: ladders mount only by +USE.</summary>
[ConVar("sv_ladder_useonly", "0", Flags = ConVarFlags.Replicated,
        Help = "If set, ladders can only be mounted by pressing +USE")]
public sealed class SvLadderUseOnlyConVar : BoolConVar { }

/// <summary>Client half of <c>r_VehicleViewDampen</c>.</summary>
[ConVar("r_VehicleViewDampen", "1",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RVehicleViewDampenConVar : BoolConVar { }

/// <summary>Client half of <c>r_JeepViewDampenFreq</c>.</summary>
[ConVar("r_JeepViewDampenFreq", "7.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewDampenFreqConVar : FloatConVar { }

/// <summary>Client half of <c>r_JeepViewDampenDamp</c>.</summary>
[ConVar("r_JeepViewDampenDamp", "1.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewDampenDampConVar : FloatConVar { }

/// <summary>Client half of <c>r_JeepViewZHeight</c>.</summary>
[ConVar("r_JeepViewZHeight", "10.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewZHeightConVar : FloatConVar { }

/// <summary>Client half of <c>r_AirboatViewDampenFreq</c>.</summary>
[ConVar("r_AirboatViewDampenFreq", "7.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewDampenFreqConVar : FloatConVar { }

/// <summary>Client half of <c>r_AirboatViewDampenDamp</c>.</summary>
[ConVar("r_AirboatViewDampenDamp", "1.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewDampenDampConVar : FloatConVar { }

/// <summary>Client half of <c>r_AirboatViewZHeight</c>.</summary>
[ConVar("r_AirboatViewZHeight", "0.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewZHeightConVar : FloatConVar { }

/// <summary>Client half of <c>phys_pushscale</c>.</summary>
[ConVar("phys_pushscale", "1", Flags = ConVarFlags.Replicated)]
public sealed class PhysPushScaleConVar : FloatConVar { }

/// <summary>Client half of <c>sv_soundemitter_trace</c>.</summary>
[ConVar("sv_soundemitter_trace", "0", Flags = ConVarFlags.Replicated,
        Help = "Show all EmitSound calls including their symbolic name and the actual wave file they resolved to\n")]
public sealed class SvSoundEmitterTraceConVar : BoolConVar { }

//-----------------------------------------------------------------------------
// Client-only, and not part of the mirror above.
//-----------------------------------------------------------------------------

/// <summary>
/// The player's language, as the engine carries it in userinfo.
///
/// <para>
/// Out of <c>shell_hud_framework.cpp</c>, where it sat beside
/// <c>closecaption</c>. <b><c>closecaption</c> stayed and this did not</b>, and
/// the difference is measurable rather than a matter of taste: the shipped
/// <c>engine.so</c> resolves <c>closecaption</c> by name — it is in the engine's
/// own client-convar query block, and <c>shell_recipientfilter.cpp:631</c> reads
/// it off a connected client through <c>GetClientConVarValue</c> — while
/// <c>english</c> appears in <c>engine.so</c> nowhere and is read by no
/// translation unit on either link line. Its two apparent hits are a comment in
/// <c>teammenu.cpp:203</c> and an unrelated caption-token table in
/// <c>public/sentence.cpp:359</c>.
/// </para>
///
/// <para>
/// <c>FCVAR_USERINFO</c> is kept, and it is what makes the move safe:
/// userinfo is transmitted to the server BY FLAG, not by name, so a managed
/// declaration carrying the flag is sent exactly as the native one was. This
/// overturns <c>plan_shim.md</c> §7, which lists <c>english</c> among the
/// protocol keeps.
/// </para>
/// </summary>
[ConVar("english", "1", Flags = ConVarFlags.UserInfo,
        Help = "If set to 1, running the english language set of assets.")]
public sealed class EnglishConVar : BoolConVar { }

//-----------------------------------------------------------------------------
// HL2MP's player speeds and suit (c_hl2mp_player.cpp), read by the replay's
// HL2 player (GameMovement.Hl2Player) through ClientHl2PlayerMove.
//-----------------------------------------------------------------------------

/// <summary>Client half of <c>hl2_walkspeed</c>.</summary>
[ConVar("hl2_walkspeed", "150", Flags = ConVarFlags.Replicated)]
public sealed class Hl2WalkSpeedConVar : FloatConVar { }

/// <summary>Client half of <c>hl2_normspeed</c>.</summary>
[ConVar("hl2_normspeed", "190", Flags = ConVarFlags.Replicated)]
public sealed class Hl2NormSpeedConVar : FloatConVar { }

/// <summary>Client half of <c>hl2_sprintspeed</c>.</summary>
[ConVar("hl2_sprintspeed", "320", Flags = ConVarFlags.Replicated)]
public sealed class Hl2SprintSpeedConVar : FloatConVar { }

/// <summary>Client half of <c>sv_infinite_aux_power</c>.</summary>
[ConVar("sv_infinite_aux_power", "0", Flags = ConVarFlags.Cheat | ConVarFlags.Replicated)]
public sealed class SvInfiniteAuxPowerConVar : BoolConVar { }
