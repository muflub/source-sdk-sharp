using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>Client ConVars. See the server-side note about Shared.</summary>
[ConVar("cs_cl_log_input", "0", Help = "Log input commands C# sees (1/0)")]
public sealed class LogInputConVar : BoolConVar
{
    protected override void OnChanged(bool oldValue, bool newValue)
        => Log.Msg($"[SourceSharp/Client] input logging -> {newValue}\n");
}

/// <summary>
/// Whether the build stamp is drawn in the HUD's bottom right corner.
///
/// <para>
/// DEFAULTS ON. The failure it prevents is somebody reading a screen and not
/// thinking to ask which build produced it — which has already cost this project
/// a round trip and a rebuild — and a stamp they would have had to enable first
/// is a stamp that would not have been on the screen in question. It is two dim
/// lines in a corner nothing else uses.
/// </para>
///
/// <para>
/// Archived, because the one good reason to turn it off is a screenshot or a
/// recording, and having to turn it off again every session would be its own
/// small annoyance.
/// </para>
/// </summary>
[ConVar("cs_cl_build_stamp", "1", Flags = ConVarFlags.Archive,
        Help = "Show which build is running in the HUD corner (1/0)")]
public sealed class BuildStampConVar : BoolConVar
{
}

/// <summary>
/// Whether the local player's unacknowledged commands are replayed
/// (plan_entity.md step 6f).
///
/// <para>
/// <b>ON BY DEFAULT, and that is the step's gate rather than a preference.</b>
/// Prediction is what makes a networked game feel like a local one: with it off
/// every movement waits a round trip, and the player watches their own input
/// arrive late. It is a convar at all so that the meter has a control arm —
/// tools/movemath-gate runs the same scenario with it 0 and with it 1 and
/// requires the numbers to differ — and so that a session chasing a
/// misprediction can turn the prediction off and see what the server alone
/// says.
/// </para>
///
/// <para>
/// Archived, because somebody who turned it off to look at something wants it
/// off until they say otherwise, and because the value people actually run with
/// is the default.
/// </para>
/// </summary>
[ConVar("cs_cl_player_predict", "1", Flags = ConVarFlags.Archive,
        Help = "Replay the local player's unacknowledged commands (1/0)")]
public sealed class PlayerPredictConVar : BoolConVar
{
}

//=============================================================================
// THE SHIM'S POLICY KNOBS, MOVED OUT OF C++ — the client's half.
//
// See the same banner in SourceSharp.Server/ConVars.cs for why these moved and
// what did not move with them. In short: a `ConVar` line in C++ IS the tuning
// value, and §6's rule is that no ConVar's default or semantics is declared
// natively. The native readers that remain resolve the same convar out of the
// engine's registry by name through CManagedConVarRef, so no read crosses the
// managed boundary.
//
// default_fov is NOT here: it failed the link when moved, because a stock TU
// this build compiles holds an `extern ConVar` for it. The server file's
// ConVars.cs carries the object-by-object evidence.
//
// hud_fastswitch WAS in that sentence and IS here now, by lane 4c-render. The
// TU that held its `extern ConVar` was game/shared/weapon_parse.cpp, and that
// file is no longer compiled at all:
//
//     grep -c weapon_parse src/_vpc_/ninja/sdk_csharp_debug.ninja   -> 0
//
// with the symbol reading one definer (shell_hud.o) and ZERO importers on both
// link lines afterwards. So the link failure that kept it native no longer has
// a cause. Re-measure before moving default_fov the same way: the same thing
// may have happened to its holder.
//
// THE FIRST FIVE ARE DECLARED ON BOTH SIDES ON PURPOSE. They came out of files
// in game/shared that compile into BOTH DLLs, so a single `static ConVar` line
// already produced two registrations, one per DLL. Declaring them here and in
// SourceSharp.Server reproduces that exactly. Declaring them once in
// SourceSharp.Shared would not: that assembly is loaded from both ALCs and the
// duplicate is the trap ConVarRegistry's summary warns about.
//=============================================================================

//-----------------------------------------------------------------------------
// THREE THAT CAME OUT OF THE CLIENT SHIM IN PHASE 1c.
//
// Each was a `ConVar` line in C++ whose DEFAULT, FLAGS and HELP TEXT were a
// tuning value native code owned. §6's ConVar row says none of those is
// declared natively unless the name is engine protocol, and none of these
// three is: nothing reads them back off the wire by name, which is what makes
// cl_team, cl_class, cl_interpolate and localplayer_visionflags stay native.
//
// WHAT THE NATIVE SIDE KEEPS IS THE READ, through CManagedConVarRef, which
// resolves out of the engine's own registry by name -- so no read crosses the
// managed boundary and no per-frame cost is added.
//-----------------------------------------------------------------------------

/// <summary>
/// From <c>shell_clientdll.cpp</c>, where it was a member of CShellClientDLL.
///
/// <para>
/// NOTHING READS IT, on either side, and that is stock's arrangement rather
/// than a gap this port introduced: the particle counter that consumed it lives
/// in a translation unit this build does not compile. The declaration exists so
/// the console has the name, exactly as it did natively, and it is here rather
/// than in C++ because a default and a help string are not engine protocol.
/// </para>
/// </summary>
[ConVar("showparticlecounts", "0", Help = "Display number of particles drawn per frame")]
public sealed class ShowParticleCountsConVar : BoolConVar { }

/// <summary>
/// Whether a weapon switch waits for a selection UI. It never does here.
///
/// <para>
/// FROM <c>shell_hud.cpp</c>, WHERE IT WAS HELD BACK BY A LINK ERROR RATHER
/// THAN BY A RULE. <c>game/shared/weapon_parse.cpp</c> holds
/// <c>extern ConVar hud_fastswitch;</c> and reads it to pick the console
/// bucket layout at <c>hud_fastswitch.GetInt() == 2</c>, so moving the
/// declaration used to fail with
/// <c>weapon_parse.o: undefined reference to hud_fastswitch</c>. That
/// translation unit is no longer compiled into either DLL, and the symbol had
/// one definer and zero importers on both link lines, so the reason expired.
/// </para>
///
/// <para>
/// DEFAULT 1, WHICH IS NOT STOCK'S 0, and that is deliberate and predates this
/// move: switching is always immediate in this mod because there is no
/// selection UI to drive — SourceSharp draws its own weapon list, and the
/// commands the stock binds land on (invnext, invprev, lastinv, slot1..slot6)
/// forward straight to the managed Arsenal on the server. The registration is
/// kept so a config that sets the name still loads.
/// </para>
/// </summary>
[ConVar("hud_fastswitch", "1", Flags = ConVarFlags.Archive,
        Help = "Weapon switching is always immediate in this mod; kept for config compatibility.")]
public sealed class HudFastSwitchConVar : BoolConVar { }

/// <summary>
/// The once-per-install marker behind the bicubic lightmap default.
///
/// <para>
/// ARCHIVED AND HIDDEN, both of which matter. CShellClientDLL::PostInit picks a
/// default for <c>r_lightmap_bicubic</c> from the adapter's maximum DX support
/// level and then sets this, so the pick happens on a fresh install and never
/// overwrites a value the player chose afterwards. Archived is what carries
/// that across sessions; hidden keeps a marker out of the player's way.
/// </para>
///
/// <para>
/// PostInit reads and writes it through CManagedConVarRef. That is safe in
/// ordering terms because CShellClientDLL::Init brings the managed runtime up
/// before PostInit runs, and the native side asks <c>IsValid()</c> rather than
/// treating an unresolved ref's zero as "not yet done".
/// </para>
/// </summary>
[ConVar("r_lightmap_bicubic_set", "0", Flags = ConVarFlags.Archive | ConVarFlags.Hidden,
        Help = "Hack to get this convar to be re-set on first launch.")]
public sealed class LightmapBicubicSetConVar : BoolConVar { }

/// <summary>
/// How far in the past NPC positions are interpolated from.
///
/// <para>
/// FCVAR_USERINFO, AND IT MOVED ANYWAY -- which is the distinction worth
/// writing down. USERINFO makes the VALUE transmissible; it does not make
/// anything read it. <c>cl_interpolate</c> stays native because the SERVER
/// reads it back off the wire by name (player.cpp:3509) and changes how it
/// lag-compensates on the answer. Nothing anywhere reads this one: its single
/// reader was c_baseentity.cpp, which is not on the client link line. The
/// transmitted name is unchanged by declaring it here.
/// </para>
/// </summary>
[ConVar("cl_interp_npcs", "0.0", Flags = ConVarFlags.UserInfo,
        Help = "Interpolate NPC positions starting this many seconds in past (or cl_interp, if greater)")]
public sealed class InterpNpcsConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// The violence family is the ENGINE's and is not declared here; see the
// server's ConVars.cs (convar_ownership.h).
//-----------------------------------------------------------------------------

/// <summary>
/// Draw physics collision models in wireframe.
///
/// <para>
/// <b>THE ONE ConVar PHASE 1c COULD NOT MOVE</b>, and the reason is worth
/// keeping: this toggle draws nothing by itself. Its whole effect is its CHANGE
/// CALLBACK, which forces every renderable to re-evaluate whether the client
/// leaf system should be showing it, and a managed <c>OnChanged</c> had no way
/// to say that — there was no ABI slot for "re-evaluate every client shell's
/// visibility". Lane 1e-abi appended one, so §6's rule finally reaches this
/// row: no ConVar's default or semantics is declared natively.
/// </para>
///
/// <para>
/// <b><see cref="Sweep"/> IS A SEAM AND NOT A STATIC BACK-REFERENCE.</b> A
/// <c>[ConVar]</c> class is constructed with no arguments — the registry
/// registers the convars BEFORE the services, so there is no container to ask —
/// so the module hands it the sweep after registration, the same way it hands
/// <see cref="BuildStampConVar"/>'s answer to the HUD's corner stamp. Unwired,
/// a toggle does nothing on change, which is what it did before this existed;
/// it is not a silent wrong answer, it is the old behaviour.
/// </para>
///
/// <para>
/// The three native readers left — <c>viewrender.cpp</c> twice and
/// <c>c_baseanimating.cpp</c> once — resolve this by NAME out of the engine's
/// registry through <c>CManagedConVarRef</c>. They read a live
/// <c>ConVar</c> pointer exactly as they did; what moved is the declaration.
/// </para>
/// </summary>
[ConVar("vcollide_wireframe", "0", Flags = ConVarFlags.Cheat,
        Help = "Render physics collision models in wireframe")]
public sealed class VCollideWireframeConVar : BoolConVar
{
    /// <summary>
    /// What re-evaluates every client shell's visibility. Installed by
    /// <c>ClientModule</c> after the engine areas are in the container; null
    /// until then, and a change before then simply does not sweep.
    /// </summary>
    public Func<int>? Sweep { get; set; }

    /// <summary>
    /// How many shells the last change swept. A READOUT, for the reason the ABI
    /// slot answers a count at all: the sweep's effect is on the next frame, so
    /// there is nothing observable about the call itself and "it swept nothing"
    /// and "it did not run" would otherwise look the same from every side.
    /// </summary>
    public int LastSweep { get; private set; }

    protected override void OnChanged(bool oldValue, bool newValue)
        => LastSweep = Sweep?.Invoke() ?? 0;
}

// sv_alternateticks is the engine's (convar_ownership.h); read by name natively.

/// <summary>
/// How long a soundscape takes to crossfade into the next one. From
/// game/client/shell_soundscape_client.cpp; read by ClientSoundscapes (C#) while
/// mixing.
/// </summary>
[ConVar("soundscape_fadetime", "3.0", Flags = ConVarFlags.Cheat,
        Help = "Time to crossfade sound effects between soundscapes")]
public sealed class SoundscapeFadetimeConVar : FloatConVar { }

/// <summary>
/// How long the pickup history stays on screen. From
/// game/client/shell_hud.cpp. Its stock reader, history_resource.cpp, is not
/// compiled into this build, so the declaration is the whole of it.
/// </summary>
[ConVar("hud_drawhistory_time", "5",
        Help = "Seconds the pickup history stays up. Unused here \u2014 the history HUD is hidden.")]
public sealed class HudDrawHistoryTimeConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// THE THIRTY-TWO PRECIPITATION AND WIND KNOBS, from
// game/client/shell_effects_sim.cpp.
//
// The rain, snow, ember and wind SIMULATION did not move and is not meant to:
// it is §6's "leaf renderers: ... particle | mesh building and material calls",
// it is already driven from managed code through
// SourceSharp_CreatePrecipitation / UpdateEmbers / WindUpdate, and it reads
// several of these per particle per frame. What moved is the thirty-two
// declarations -- the defaults, the flags and the help text -- which were
// tuning values native code owned and managed code could not see.
//
// Every default below was GENERATED from the C++ declarations rather than
// retyped, so the two cannot have drifted at the moment of the move; the gate
// then checked all thirty-two against a live game's own answer.
//
// THREE DEFAULTS CARRY AN 'f' INSIDE THE STRING -- "384.0f", "512.0f",
// "8192.0f". That is stock's, not a slip of this port's, and it is preserved
// rather than tidied: the engine parses a convar's default with atof, which
// stops at the 'f' and yields the same number, but "tidying" it would change a
// default the console prints and any test that compares against stock's text.
//
// r_SnowZoomRadius is declared and read by NOTHING, here or in either DLL, so
// the declaration is the whole of it and shell_effects_sim.cpp keeps no ref.
//-----------------------------------------------------------------------------

// props_shared.cpp's piece limits (it left both DLLs): the clientside props'
// breaks read them (ClientPhysicsProps, PropBreakLimits). The per-frame one is
// replicated, so the server declares its half too (GameVars.cs).
[ConVar("props_break_max_pieces", "-1", Help = "Maximum prop breakable piece count (-1 = model default)")]
public sealed class PropsBreakMaxPiecesConVar : IntConVar { }

[ConVar("props_break_max_pieces_perframe", "-1", Flags = ConVarFlags.Replicated,
        Help = "Maximum prop breakable piece count per frame (-1 = model default)")]
public sealed class PropsBreakMaxPiecesPerFrameConVar : IntConVar { }

[ConVar("cl_winddir", "0",
        Flags = ConVarFlags.Cheat,
        Help = "Weather effects wind direction angle")]
public sealed class ClWinddirConVar : FloatConVar { }

[ConVar("cl_windspeed", "0",
        Flags = ConVarFlags.Cheat,
        Help = "Weather effects wind speed scalar")]
public sealed class ClWindspeedConVar : FloatConVar { }

[ConVar("r_RainSplashPercentage", "20", Flags = ConVarFlags.Cheat)]
public sealed class RRainSplashPercentageConVar : IntConVar { }

[ConVar("r_RainHack", "0", Flags = ConVarFlags.Cheat)]
public sealed class RRainHackConVar : IntConVar { }

[ConVar("r_RainRadius", "1500", Flags = ConVarFlags.Cheat)]
public sealed class RRainRadiusConVar : FloatConVar { }

[ConVar("r_RainSideVel", "130",
        Flags = ConVarFlags.Cheat,
        Help = "How much sideways velocity rain gets.")]
public sealed class RRainSideVelConVar : IntConVar { }

[ConVar("r_RainSimulate", "1",
        Flags = ConVarFlags.Cheat,
        Help = "Enable/disable rain simulation.")]
public sealed class RRainSimulateConVar : IntConVar { }

[ConVar("r_DrawRain", "1", Flags = ConVarFlags.Cheat, Help = "Enable/disable rain rendering.")]
public sealed class RDrawRainConVar : IntConVar { }

[ConVar("r_RainProfile", "0",
        Flags = ConVarFlags.Cheat,
        Help = "Enable/disable rain profiling.")]
public sealed class RRainProfileConVar : IntConVar { }

[ConVar("r_SnowEnable", "1", Flags = ConVarFlags.Cheat, Help = "Snow Enable")]
public sealed class RSnowEnableConVar : BoolConVar { }

[ConVar("r_SnowParticles", "500", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowParticlesConVar : IntConVar { }

[ConVar("r_SnowInsideRadius", "256", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowInsideRadiusConVar : FloatConVar { }

[ConVar("r_SnowOutsideRadius", "1024", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowOutsideRadiusConVar : FloatConVar { }

[ConVar("r_SnowSpeedScale", "1", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowSpeedScaleConVar : FloatConVar { }

[ConVar("r_SnowPosScale", "1", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowPosScaleConVar : FloatConVar { }

[ConVar("r_SnowFallSpeed", "1.5", Flags = ConVarFlags.Cheat, Help = "Snow fall speed scale.")]
public sealed class RSnowFallSpeedConVar : FloatConVar { }

[ConVar("r_SnowWindScale", "0.0035", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowWindScaleConVar : FloatConVar { }

[ConVar("r_SnowDebugBox", "0", Flags = ConVarFlags.Cheat, Help = "Snow Debug Boxes.")]
public sealed class RSnowDebugBoxConVar : FloatConVar { }

[ConVar("r_SnowZoomOffset", "384.0f", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowZoomOffsetConVar : FloatConVar { }

[ConVar("r_SnowZoomRadius", "512.0f", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowZoomRadiusConVar : FloatConVar { }

[ConVar("r_SnowStartAlpha", "25", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowStartAlphaConVar : IntConVar { }

[ConVar("r_SnowEndAlpha", "255", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowEndAlphaConVar : IntConVar { }

[ConVar("r_SnowColorRed", "150", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowColorRedConVar : IntConVar { }

[ConVar("r_SnowColorGreen", "175", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowColorGreenConVar : IntConVar { }

[ConVar("r_SnowColorBlue", "200", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowColorBlueConVar : IntConVar { }

[ConVar("r_SnowStartSize", "1", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowStartSizeConVar : IntConVar { }

[ConVar("r_SnowEndSize", "0", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowEndSizeConVar : IntConVar { }

[ConVar("r_SnowRayLength", "8192.0f", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowRayLengthConVar : FloatConVar { }

[ConVar("r_SnowRayRadius", "256", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowRayRadiusConVar : FloatConVar { }

[ConVar("r_SnowRayEnable", "1", Flags = ConVarFlags.Cheat, Help = "Snow.")]
public sealed class RSnowRayEnableConVar : BoolConVar { }

[ConVar("r_rainalpha", "0.4", Flags = ConVarFlags.Cheat)]
public sealed class RRainalphaConVar : FloatConVar { }

[ConVar("r_rainalphapow", "0.8", Flags = ConVarFlags.Cheat)]
public sealed class RRainalphapowConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// THE THIRTEEN THE EFFECTS SHIM STILL DECLARED IN C++ (plan_shim.md §6).
//
// Every one is a tuning value: nothing here is transmitted, replicated, or
// resolved by the engine as part of a protocol. plan_entity.md §6's rule is
// that a ConVar's default and semantics are policy and may not be declared
// natively, and these thirteen were the effects group's remainder.
//
// THE FOUR r_rain* WERE HIDING BEHIND A CLASS. They are static MEMBERS of
// CClient_Precipitation, defined as `ConVar CClient_Precipitation::s_rainwidth(
// "r_rainwidth", ... )`, which is why the generated pass that moved the other
// thirty-two precipitation knobs walked past them -- and why the header at the
// top of shell_effects_sim.cpp claimed all thirty-two had moved while four of
// them were still C++ a hundred lines below it.
//
// r_raindensity HAS NO READER, in this file or in either DLL: the member was
// defined and never read. The declaration is the whole of it, exactly as with
// r_SnowZoomRadius, and shell_effects_sim.cpp keeps no ref for it.
//
// mat_reduceparticles WAS REGISTERED TWICE IN ONE DLL -- once in
// shell_smokestack_sim.cpp and once in shell_smokevolume_sim.cpp, both
// `static ConVar mat_reduceparticles( "mat_reduceparticles", "0" )`. Two
// registrations of one name is neither a compile error nor a link error; the
// engine keeps the first and drops the second with one console line, and the
// loser's `.GetBool()` answered its compiled-in default forever. There is one
// declaration now and both files ref it by name.
//
// AND IT IS NOT DECLARED HERE AT ALL: the name carries neither DLL flag in the
// live table, so the engine side (the material system reads it through
// ConVarRefs in spritecard.cpp and vertexlitgeneric_dx9_helper.cpp) defines it
// itself, and a managed declaration only aliased that one (convar_ownership.h).
// Both native readers resolve the engine's by name.
//
// FLAGS AND DEFAULTS ARE THE C++ TO THE CHARACTER, the two 'f'-inside-the-string
// defaults included -- "100.0f" and "15.0f" on the worldtext pair, and "0.1f"
// and "600.0f" on two of the rain knobs. The engine parses a default with atof,
// which stops at the 'f' and yields the same number; tidying it would change a
// default the console prints.
//-----------------------------------------------------------------------------

[ConVar("r_raindensity", "0.001", Flags = ConVarFlags.Cheat)]
public sealed class RRaindensityConVar : FloatConVar { }

[ConVar("r_rainwidth", "0.5", Flags = ConVarFlags.Cheat)]
public sealed class RRainwidthConVar : FloatConVar { }

[ConVar("r_rainlength", "0.1f", Flags = ConVarFlags.Cheat)]
public sealed class RRainlengthConVar : FloatConVar { }

[ConVar("r_rainspeed", "600.0f", Flags = ConVarFlags.Cheat)]
public sealed class RRainspeedConVar : FloatConVar { }

// The client-side tracers' three (stock's fx_discreetline.cpp and fx_tracer.cpp
// declared them natively; plan_client_novalve C2, lane c2-fx). Defaults, flags
// and help as stock's: client-convars-contract.txt holds them.
[ConVar("tracer_extra", "1", Flags = ConVarFlags.ClientDll)]
public sealed class TracerExtraConVar : BoolConVar { }

[ConVar("r_drawtracers", "1", Flags = ConVarFlags.Cheat | ConVarFlags.ClientDll)]
public sealed class DrawTracersConVar : BoolConVar { }

// fx_sparks.cpp's one ConVar, read by the metal sparks (lane c5-particles:
// fx_sparks.cpp left the build). Default, flags and help as stock's.
[ConVar("fx_drawmetalspark", "1", Flags = ConVarFlags.DevelopmentOnly | ConVarFlags.ClientDll, Help = "Draw metal spark effects.")]
public sealed class DrawMetalSparkConVar : BoolConVar, Effects.IDrawMetalSpark
{
    public bool On => Value;
}

// fx.cpp's one ConVar, read by the temp entities' muzzle flashes (lane
// c5-particles: fx.cpp left the build). Default and flags as stock's.
[ConVar("muzzleflash_light", "1", Flags = ConVarFlags.Archive | ConVarFlags.ClientDll)]
public sealed class MuzzleFlashLightConVar : BoolConVar, TempEnts.IMuzzleFlashLight
{
    public bool On => Value;
}

// The legacy tempents' three (stock's c_te_legacytempents.cpp declared them
// natively; plan_client_novalve C2, lane c2-fx). Defaults and flags as stock's,
// no help (stock gave none): client-convars-contract.txt holds them. Read by
// name where they are read (ClientTempEntDecisions, TempEntSpawns, the frame).
[ConVar("cl_ejectbrass", "1", Flags = ConVarFlags.ClientDll)]
public sealed class EjectBrassConVar : BoolConVar { }

[ConVar("func_break_max_pieces", "15", Flags = ConVarFlags.Archive | ConVarFlags.Replicated | ConVarFlags.ClientDll)]
public sealed class FuncBreakMaxPiecesConVar : IntConVar { }

[ConVar("cl_fasttempentcollision", "5", Flags = ConVarFlags.ClientDll)]
public sealed class FastTempEntCollisionConVar : IntConVar { }

[ConVar("r_drawtracers_firstperson", "1", Flags = ConVarFlags.Archive | ConVarFlags.ClientDll,
        Help = "Toggle visibility of first person weapon tracers")]
public sealed class DrawTracersFirstPersonConVar : BoolConVar { }

[ConVar("cl_spraydisable", "0",
        Flags = ConVarFlags.ClientDll | ConVarFlags.Archive,
        Help = "Disable player sprays.")]
public sealed class ClSpraydisableConVar : BoolConVar { }

[ConVar("cl_sporeclipdistance", "512", Flags = ConVarFlags.Cheat | ConVarFlags.ClientDll)]
public sealed class ClSporeclipdistanceConVar : FloatConVar { }

[ConVar("pointworldtext_rainbowspeed_time", "100.0f")]
public sealed class PointworldtextRainbowspeedTimeConVar : FloatConVar { }

[ConVar("pointworldtext_rainbowspeed_char", "15.0f")]
public sealed class PointworldtextRainbowspeedCharConVar : FloatConVar { }


[ConVar("fx_drawimpactdebris", "1",
        Flags = ConVarFlags.DevelopmentOnly,
        Help = "Draw impact debris effects.")]
public sealed class FxDrawimpactdebrisConVar : BoolConVar { }

[ConVar("fx_drawimpactdust", "1",
        Flags = ConVarFlags.DevelopmentOnly,
        Help = "Draw impact dust effects.")]
public sealed class FxDrawimpactdustConVar : BoolConVar { }

[ConVar("r_drawflecks", "1", Flags = ConVarFlags.AllowedInCompetitive)]
public sealed class RDrawflecksConVar : BoolConVar { }

[ConVar("cl_new_impact_effects", "0")]
public sealed class ClNewImpactEffectsConVar : IntConVar { }

/// <summary>
/// Which HL2 this is: the client's half of a replicated pair whose server half
/// is SourceSharp.Server.HL2EpisodicConVar (plan_server_novalve.md P2.3b). No
/// reader on either side; kept so a config naming it resolves.
/// </summary>
[ConVar("hl2_episodic", "0", Flags = ConVarFlags.Replicated)]
public sealed class HL2EpisodicConVar : BoolConVar { }
