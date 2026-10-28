using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The server's half of thirteen <c>FCVAR_REPLICATED</c> ConVars that C++ used to
/// declare — eleven of them from <c>game/shared/movevars_shared.cpp</c>, two more
/// of the same shape from elsewhere in the shim — out of
/// C++ by <c>plan_entity.md</c> §6's rule: no ConVar's default or semantics is
/// declared natively unless the engine itself transmits or resolves the name.
///
/// <para>
/// <b>Eleven of movevars_shared.cpp's twenty-nine, and the other eighteen are
/// still in C++ on purpose.</b> What separates them is not the flag, the default or the
/// meaning: it is whether a translation unit ON A LINK LINE still reads the
/// declaration through <c>movevars_shared.h</c>'s extern. These eleven have
/// <b>zero</b> such readers — the header's extern is the only mention of each
/// anywhere the build compiles. Seventeen of the eighteen that stay are read by
/// <c>shell_movement.cpp</c>, <c>shell_movement_audit.cpp</c>,
/// <c>world_adapter.cpp</c>, <c>shell_gamerules.cpp</c> and
/// <c>shell_motioncontrollers.cpp</c>, and each of those reads becomes a
/// <c>CManagedConVarRef</c> before its declaration can follow these. The
/// eighteenth is <c>sv_skyname</c>, which stays for good: the engine resolves
/// that one by name.
/// </para>
///
/// <para>
/// <b>Why the engine still replicates them.</b> A replicated ConVar is not
/// resolved by the engine by name — none of these thirteen appears in the shipped
/// <c>engine.so</c>'s string table at all. It is replicated by its FLAG, and
/// <c>Engine.RegisterConVar</c> builds a real native <c>ConVar</c> in the
/// engine's own registry carrying that flag. What makes the flag work from
/// managed code is the DLL-identity bit the registration ORs in
/// (<c>sourcesharp_gamesystem.cpp:699,701</c>): without it
/// <c>AreConVarsLinkable</c> refuses the client/server link and prints "For
/// FCVAR_REPLICATED, ConVar must be defined in client and game .dlls", which is
/// the defect <c>docs/defect-convar-order.md</c> diagnosed and which is fixed.
/// </para>
///
/// <para>
/// <b>Declared per side, not in Shared.</b> A ConVar class in
/// <c>SourceSharp.Shared</c> is discovered from both ALCs and registered twice
/// out of ONE declaration, which is not the same thing as two modules each
/// declaring the name. <c>SourceSharp.Client/MoveVars.cs</c> is the other half,
/// and it must carry the same names, defaults and flags — a replicated ConVar
/// that exists on only one side is a prediction bug, silent, in one process.
/// </para>
///
/// <para>
/// <b>Nothing in this build reads any of the eleven movevars.</b> That is measured, not
/// assumed, and it is not a reason to delete them: the seven <c>r_*</c> ones are
/// the vehicle view-damping the HL2 jeep and airboat used, and the five
/// <c>sv_*</c> ones are stock movement knobs a server operator's config may
/// still set. They keep their stock names, defaults and help text so that a
/// config naming one still resolves.
/// </para>
/// </summary>
internal static class MoveVarsDoc
{
}

//-----------------------------------------------------------------------------
// The four movement knobs.
//-----------------------------------------------------------------------------

/// <summary>How much backwards motion is slowed. Stock default 0.6.</summary>
[ConVar("sv_backspeed", "0.6", Flags = ConVarFlags.Archive | ConVarFlags.Replicated,
        Help = "How much to slow down backwards motion")]
public sealed class SvBackspeedConVar : FloatConVar { }

/// <summary>Whether players' footstep sounds play.</summary>
[ConVar("sv_footsteps", "1", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Play footstep sound for players")]
public sealed class SvFootstepsConVar : BoolConVar { }

/// <summary>Acceleration while swimming. Stock default 10.</summary>
[ConVar("sv_wateraccelerate", "10", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvWaterAccelerateConVar : FloatConVar { }

/// <summary>Vertical view fixup when the eyes are near the water plane.</summary>
[ConVar("sv_waterdist", "12", Flags = ConVarFlags.Replicated,
        Help = "Vertical view fixup when eyes are near water plane.")]
public sealed class SvWaterDistConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// hl_gamemovement.cpp's three ladder knobs, read by the useable-ladder
// movement (GameMovement.Ladders) through this side's ladder world.
//-----------------------------------------------------------------------------

/// <summary><c>sv_autoladderdismount</c>: get off at a ladder's end without +USE.</summary>
[ConVar("sv_autoladderdismount", "1", Flags = ConVarFlags.Replicated,
        Help = "Automatically dismount from ladders when you reach the end (don't have to +USE).")]
public sealed class SvAutoLadderDismountConVar : BoolConVar { }

/// <summary><c>sv_ladderautomountdot</c>: how nearly along a ladder's axis a look auto-mounts it.</summary>
[ConVar("sv_ladderautomountdot", "0.4", Flags = ConVarFlags.Replicated,
        Help = "When auto-mounting a ladder by looking up its axis, this is the tolerance for looking now directly along the ladder axis.")]
public sealed class SvLadderAutoMountDotConVar : FloatConVar { }

/// <summary><c>sv_ladder_useonly</c>: ladders mount only by +USE.</summary>
[ConVar("sv_ladder_useonly", "0", Flags = ConVarFlags.Replicated,
        Help = "If set, ladders can only be mounted by pressing +USE")]
public sealed class SvLadderUseOnlyConVar : BoolConVar { }

//-----------------------------------------------------------------------------
// The seven vehicle view-damping knobs. All FCVAR_CHEAT as stock declared them.
//-----------------------------------------------------------------------------

/// <summary>Whether a vehicle's view is damped at all.</summary>
[ConVar("r_VehicleViewDampen", "1",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RVehicleViewDampenConVar : BoolConVar { }

/// <summary>Jeep view damping frequency.</summary>
[ConVar("r_JeepViewDampenFreq", "7.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewDampenFreqConVar : FloatConVar { }

/// <summary>Jeep view damping ratio.</summary>
[ConVar("r_JeepViewDampenDamp", "1.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewDampenDampConVar : FloatConVar { }

/// <summary>Jeep view height above the vehicle.</summary>
[ConVar("r_JeepViewZHeight", "10.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RJeepViewZHeightConVar : FloatConVar { }

/// <summary>Airboat view damping frequency.</summary>
[ConVar("r_AirboatViewDampenFreq", "7.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewDampenFreqConVar : FloatConVar { }

/// <summary>Airboat view damping ratio.</summary>
[ConVar("r_AirboatViewDampenDamp", "1.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewDampenDampConVar : FloatConVar { }

/// <summary>Airboat view height above the vehicle. Stock default 0.</summary>
[ConVar("r_AirboatViewZHeight", "0.0",
        Flags = ConVarFlags.Cheat | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class RAirboatViewZHeightConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// Not a movevar, and here because it is the same kind of move: a replicated
// value with no reader left on either link line.
//-----------------------------------------------------------------------------

/// <summary>
/// The scale applied to the push a physics object gives an entity it hits.
///
/// <para>
/// Out of <c>game/shared/takedamageinfo.cpp</c> and then the shim's
/// <c>takedamageinfo_library.cpp</c>. The rule that would read it is
/// <c>SourceSharp.Server/DamageRules.cs</c>, on this side of the boundary
/// already; nothing on either link line reads the declaration.
/// </para>
/// </summary>
[ConVar("phys_pushscale", "1", Flags = ConVarFlags.Replicated)]
public sealed class PhysPushScaleConVar : FloatConVar { }

/// <summary>
/// Traces every soundscript lookup the emitter performs.
///
/// <para>
/// A diagnostic, and a live one: <c>tools/7e-particle-sound-gate</c> turns it on
/// by name, parses the output into (script, wave, entindex, recipients) and
/// turns it off again. It moves rather than going, because a ConVar registered
/// from managed code answers <c>sv_soundemitter_trace 1</c> from the console
/// exactly as the native one did — what moved is where the default and the
/// meaning are written down, not whether the name resolves.
/// </para>
/// </summary>
[ConVar("sv_soundemitter_trace", "0", Flags = ConVarFlags.Replicated,
        Help = "Show all EmitSound calls including their symbolic name and the actual wave file they resolved to\n")]
public sealed class SvSoundEmitterTraceConVar : BoolConVar { }

//-----------------------------------------------------------------------------
// The other eighteen of movevars_shared.cpp's -- plan_server_novalve.md P2.3.
// Their native declarations are CLIENT_DLL-only now; server.so reads the ones
// it still reads through CManagedConVarRef, by name. Names, defaults, flags
// and help are stock's, byte for byte. The eight the mod's tuning is written
// into (MovementTuning) are MovementTunedConVar, so their changes are audited.
//-----------------------------------------------------------------------------

/// <summary>
/// World gravity. The mod's tuning writes it at startup. Tagged: off its
/// default, "gravity" is in sv_tags (<see cref="ServerTags"/>), as stock's
/// game rules listed it.
/// </summary>
[ConVar("sv_gravity", "800", Flags = ConVarFlags.Notify | ConVarFlags.Replicated, Help = "World gravity.",
        Tag = "gravity")]
public sealed class SvGravityConVar : MovementTunedConVar { }

/// <summary>Minimum stopping speed on the ground. Tuned.</summary>
[ConVar("sv_stopspeed", "100", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Minimum stopping speed when on ground.")]
public sealed class SvStopSpeedConVar : MovementTunedConVar { }

[ConVar("sv_noclipaccelerate", "5", Flags = ConVarFlags.Notify | ConVarFlags.Archive | ConVarFlags.Replicated)]
public sealed class SvNoclipAccelerateConVar : FloatConVar { }

[ConVar("sv_noclipspeed", "5", Flags = ConVarFlags.Archive | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvNoclipSpeedConVar : FloatConVar { }

[ConVar("sv_specaccelerate", "5", Flags = ConVarFlags.Notify | ConVarFlags.Archive | ConVarFlags.Replicated)]
public sealed class SvSpecAccelerateConVar : FloatConVar { }

[ConVar("sv_specspeed", "3", Flags = ConVarFlags.Archive | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvSpecSpeedConVar : FloatConVar { }

[ConVar("sv_specnoclip", "1", Flags = ConVarFlags.Archive | ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvSpecNoclipConVar : BoolConVar { }

/// <summary>Maximum ground speed. Tuned.</summary>
[ConVar("sv_maxspeed", "320", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvMaxSpeedConVar : MovementTunedConVar { }

/// <summary>Ground acceleration. Tuned.</summary>
[ConVar("sv_accelerate", "10", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvAccelerateConVar : MovementTunedConVar { }

/// <summary>Air acceleration. Tuned.</summary>
[ConVar("sv_airaccelerate", "10", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvAirAccelerateConVar : MovementTunedConVar { }

[ConVar("sv_waterfriction", "1", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvWaterFrictionConVar : FloatConVar { }

[ConVar("sv_rollspeed", "200", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvRollSpeedConVar : FloatConVar { }

[ConVar("sv_rollangle", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated, Help = "Max view roll angle")]
public sealed class SvRollAngleConVar : FloatConVar { }

/// <summary>World friction. Tuned.</summary>
[ConVar("sv_friction", "4", Flags = ConVarFlags.Notify | ConVarFlags.Replicated, Help = "World friction.")]
public sealed class SvFrictionConVar : MovementTunedConVar { }

[ConVar("sv_bounce", "0", Flags = ConVarFlags.Notify | ConVarFlags.Replicated,
        Help = "Bounce multiplier for when physically simulated objects collide with other objects.")]
public sealed class SvBounceConVar : FloatConVar { }

/// <summary>Per-axis speed cap for anything moving ballistically. Tuned.</summary>
[ConVar("sv_maxvelocity", "3500", Flags = ConVarFlags.Replicated,
        Help = "Maximum speed any ballistically moving object is allowed to attain per axis.")]
public sealed class SvMaxVelocityConVar : MovementTunedConVar { }

/// <summary>Step height. Tuned.</summary>
[ConVar("sv_stepsize", "18", Flags = ConVarFlags.Notify | ConVarFlags.Replicated)]
public sealed class SvStepSizeConVar : MovementTunedConVar { }

/// <summary>
/// The skybox. The engine resolves this one by NAME, which still works: a
/// managed declaration registers into the engine's own registry.
/// </summary>
[ConVar("sv_skyname", "sky_urb01", Flags = ConVarFlags.Archive | ConVarFlags.Replicated,
        Help = "Current name of the skybox texture")]
public sealed class SvSkyNameConVar : StringConVar { }
