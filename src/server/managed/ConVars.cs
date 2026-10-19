using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Server ConVars. Declared in the Server assembly, not Shared — a ConVar in
/// Shared would register from both DLLs and Source rejects the same name from
/// client and server.
/// </summary>
[ConVar("cs_sv_greeting", "hello", Help = "Greeting logged when a player joins",
        Flags = ConVarFlags.Archive)]
public sealed class GreetingConVar : StringConVar
{
    protected override void OnChanged(string oldValue, string newValue)
        => Log.Msg($"[SourceSharp/Server] greeting '{oldValue}' -> '{newValue}'\n");
}

[ConVar("cs_sv_verbose_joins", "1", Help = "Log player joins from C# (1/0)")]
public sealed class VerboseJoinsConVar : BoolConVar
{
}

/// <summary>
/// Scales all incoming player damage. A game rule, decided in C#.
///
/// Clamped by the engine rather than checked here, and the difference matters: a
/// negative multiplier would heal whatever it hit, and a check made in OnChanged
/// would only see the value after every other reader already had it.
/// </summary>
/// <summary>
/// The fraction of a hit that reaches HEALTH while the player has armour.
/// Stock's 0.2 — armour takes 80%.
///
/// A convar because it is the single number that decides how much armour is
/// worth, and a deathmatch mod tuning its pacing will reach for it before
/// anything else. It could not be one at all while the rule lived in
/// CBasePlayer::OnTakeDamage.
/// </summary>
[ConVar("cs_sv_armour_ratio", "0.2", Help = "Fraction of damage that reaches health through armour",
        Min = 0f, Max = 1f)]
public sealed class ArmourRatioConVar : FloatConVar { }

/// <summary>
/// How many points of armour one point of absorbed damage costs. Stock's modern
/// value is 1.0; the old rule was 0.5, which made a point of armour worth two
/// of health.
/// </summary>
[ConVar("cs_sv_armour_bonus", "1.0", Help = "Armour points spent per point of damage absorbed",
        Min = 0.01f)]
public sealed class ArmourBonusConVar : FloatConVar { }

[ConVar("cs_sv_damage_scale", "1.0", Help = "Multiplier applied to all player damage",
        Min = 0f)]
public sealed class DamageScaleConVar : FloatConVar
{
    protected override void OnChanged(float oldValue, float newValue)
        => Log.Msg($"[SourceSharp/Server] damage scale {oldValue:F2} -> {newValue:F2}\n");
}

/// <summary>Health players spawn with. Applied by C# on spawn.</summary>
[ConVar("cs_sv_spawn_health", "100", Help = "Health a player spawns with")]
public sealed class SpawnHealthConVar : IntConVar
{
}

[ConVar("cs_sv_allow_flashlight", "1", Help = "Allow the flashlight (1/0)")]
public sealed class AllowFlashlightConVar : BoolConVar
{
}

/// <summary>Comma-separated names refused at connect time.</summary>
[ConVar("cs_sv_banned_names", "", Help = "Comma-separated player names to refuse", Flags = ConVarFlags.Archive)]
public sealed class BannedNamesConVar : StringConVar
{
}

[ConVar("cs_sv_friendly_fire", "1", Help = "Allow friendly fire (1/0)")]
public sealed class FriendlyFireConVar : BoolConVar
{
}

//=============================================================================
// THE SHIM'S POLICY KNOBS, MOVED OUT OF C++.
//
// plan_entity.md §6's ConVar rule: ConVar and ConCommand REGISTRATION is
// mechanism the engine owns, but no ConVar's default or semantics is declared
// natively. A `ConVar foo( "foo", "1", 0, "help" )` line in C++ IS the tuning
// value -- the name, the default, the flags and the help text are the whole of
// it -- and while it sat in a shim .cpp no managed code could read it, set it
// or list it.
//
// Each declaration below is stock's to the character. The native readers that
// remain resolve the SAME convar out of the engine's registry by name through
// CManagedConVarRef (src/sourcesharp/game/shared/managed_convar_ref.h), so
// nothing crosses the managed boundary per read -- a point the lane's gate
// checks rather than asserts, by watching the per-op crossing counters.
//
// DECLARED PER SIDE, NOT IN SHARED. Four of these came out of files in
// game/shared that compile into BOTH DLLs, so they were registered twice
// already -- once from server.so and once from client.so. That is reproduced
// by declaring them in this assembly AND in SourceSharp.Client, which is what
// the native `static ConVar` in a shared file did. A declaration in
// SourceSharp.Shared would be found from both ALCs and is the trap
// ConVarRegistry's own summary warns about.
//=============================================================================

//-----------------------------------------------------------------------------
// The violence family (violence_hblood, violence_hgibs, violence_ablood,
// violence_agibs) is NOT declared here: the engine defines all four before
// either game DLL loads, so a declaration here only aliased the engine's
// (convar_ownership.h refuses that now). UTIL_IsLowViolence and
// UTIL_ShouldShowBlood read them natively by name; Corpses reads them through
// IEngineConsole.
//-----------------------------------------------------------------------------

/// <summary>
/// Whether a notarget entity is invisible to everything rather than merely
/// untargeted. From game/server/server_util_library.cpp, where
/// UTIL_FindClientInPVSGuts still reads it.
///
/// <para>
/// A rule about what the AI can see, which is as squarely "decides what the
/// game does" as a convar gets.
/// </para>
/// </summary>
[ConVar("sv_strict_notarget", "0",
        Help = "If set, notarget will cause entities to never think they are in the pvs")]
public sealed class StrictNotargetConVar : BoolConVar { }

/// <summary>
/// The server's cap on a client-requested move magnitude. From
/// sourcesharp/game/server/shell_gameclients.cpp, where
/// <c>CServerGameClients::ProcessUsercmds</c> still reads it -- three times,
/// once per axis -- to zero a usercmd whose forward, side or up move exceeds
/// it.
///
/// <para>
/// IT WAS DECLARED NATIVELY, as a field of the CServerGameClients singleton,
/// and plan_shim.md's rule is that a natively declared ConVar has to be
/// engine-transmitted to stay. This one is not: its flags were literally
/// <c>0</c> -- no <c>FCVAR_REPLICATED</c>, so no client ever sees it, and
/// nothing in the engine reads it by name. It is a server-side CLAMP, which is
/// a rule about what the game does with what a client asked for, and that is
/// policy.
/// </para>
///
/// <para>
/// Native still reads it, through <c>CManagedConVarRef</c> by name, which is
/// the same shape <c>sv_strict_notarget</c> already uses from
/// server_util_library.cpp. What moved is the DECLARATION and therefore the
/// ownership of the default and the help text; the clamp itself stays where
/// the usercmd codec is, because that is engine mechanism.
/// </para>
/// </summary>
[ConVar("sv_max_usercmd_move_magnitude", "1000",
        Help = "Maximum move magnitude that can be requested by client.")]
public sealed class MaxUsercmdMoveMagnitudeConVar : FloatConVar { }

// sv_alternateticks is the engine's (convar_ownership.h); the native
// EntitySim_IsSimulatingOnAlternateTicks reads it by name.

/// <summary>
/// Whether the AI logs shot statistics. From
/// game/server/shell_ai_orphans.cpp, which Phase 1b deleted — its
/// EntityShot_UpdateStatistics had no caller left on the server link line, and
/// the body it would have run was empty in any case. Nothing reads this now.
/// Stock declares it with no help text and that is kept.
/// </summary>
[ConVar("ai_shot_stats", "0")]
public sealed class AiShotStatsConVar : BoolConVar { }

// sv_unlockedchapters is the engine's (convar_ownership.h), archive flags and
// all; nothing in this build reads it.

/// <summary>
/// Whether navigation runs after the frame rather than inside it. From
/// game/server/shell_engineboundary.cpp. Its stock readers (ai_basenpc.cpp and
/// ai_navigator.cpp) are not compiled into this build. Stock declares it with
/// no help text.
/// </summary>
[ConVar("ai_post_frame_navigation", "0")]
public sealed class AiPostFrameNavigationConVar : BoolConVar { }

/// <summary>
/// Which line-of-sight mode the AI uses. From game/server/ai_basenpc.cpp by way
/// of game/server/shell_ai_absent.cpp, deleted in Phase 1b.
///
/// <para>
/// Nothing reads it on either side: ai_basenpc.cpp is not compiled, and its
/// other stock readers were in baseentity.cpp, which has left
/// server_csharp.vpc. It comes across rather than being dropped because it is a
/// knob stock has and a console that has stopped answering a name is a
/// behaviour change nobody asked for.
/// </para>
///
/// <para>
/// FCVAR_REPLICATED is stock's and is kept. Whether a managed declaration
/// round-trips that flag is an open question the plan records (§10, "Does
/// managed RegisterConVar take FCVAR_REPLICATED"), and it is unobservable here
/// either way: there is no reader on the server and none on the client, so
/// there is no value for replication to carry. Nothing about this declaration
/// depends on the answer, and the experiment that settles it is a live join.
/// </para>
/// </summary>
[ConVar("ai_LOS_mode", "0", Flags = ConVarFlags.Replicated)]
public sealed class AiLosModeConVar : BoolConVar { }

/// <summary>
/// Scales the autoaim radius while the player is driving a vehicle. From
/// game/server/shell_subsystems_absent.cpp, deleted in Phase 1b.
///
/// <para>
/// Nothing reads it, on either side of the boundary, and that is stock's
/// arrangement rather than a gap this port left: the one read was in
/// baseentity.cpp's autoaim maths behind a "is the player in a vehicle"
/// condition, and this build has no vehicles for that condition to become
/// true. The declaration comes across anyway so that the number the autoaim
/// radius is scaled by still exists to be set and listed, and so that vehicles
/// arriving in C# later find it rather than having to re-derive it. Stock
/// declares it with no flags and no help text.
/// </para>
/// </summary>
[ConVar("sv_vehicle_autoaim_scale", "8")]
public sealed class VehicleAutoaimScaleConVar : FloatConVar { }

/// <summary>
/// Whether a map's <c>point_servercommand</c> entities may type at the server
/// console. From game/server/client.cpp by way of
/// game/server/shell_client_orphans.cpp, deleted in Phase 1b.
///
/// <para>
/// The only convar in the SDK built to switch an entity off, and the entity is
/// the most dangerous one there is: an untrusted map that can run
/// <c>rcon_password</c> or <c>exec</c> owns the server.
/// <see cref="PointServerCommand"/> reads it BY NAME through
/// <c>IEngineConsole.FindConVarValue</c>, which is why this move does not touch
/// that entity — a by-name read does not care which side declared the name.
/// </para>
///
/// <para>
/// <b>Why the ordering worry the C++ carried does not apply.</b>
/// shell_client_orphans.cpp argued the registration had to stay native because
/// "a native convar in a game DLL is constructed at DLL load, before the engine
/// executes server.cfg, and a managed one is registered when the managed side
/// starts, which is later" — so an operator's
/// <c>sv_allow_point_servercommand disallow</c> in a config could be silently
/// discarded. Later than the static constructors, yes; later than server.cfg,
/// no. The engine cannot execute server.cfg before the game DLL has told it
/// which file that is: <c>servercfgfile</c> is a game-DLL ConVar
/// (game/server/game.cpp:30). The managed side comes up inside
/// <c>CServerGameDLL::DLLInit</c> — <c>LoadManagedRuntime( this )</c>, which
/// reaches <c>ConVarRegistry.RegisterAll</c> in ServerModule.cs — and DLLInit
/// is the first call the engine makes into the game DLL, long before any
/// config is executed. MultiplayerConVars.cs already keeps eleven declarations
/// alive here for exactly this reason: "so that a server.cfg that names one"
/// still finds it.
/// </para>
///
/// <para>
/// The default, the flags and the help text are stock's to the character, minus
/// the two <c>#ifdef TF_DLL</c> arms, which this mod is not. The DECISION —
/// what "always", "disallow" and anything else mean — was already in C# and
/// stays there; this is the declaration only.
/// </para>
/// </summary>
[ConVar("sv_allow_point_servercommand", "always",
        Help = "Allow use of point_servercommand entities in map. Potentially dangerous for untrusted maps.\n"
             + "  disallow : Always disallow\n"
             + "  always   : Allow for all maps")]
public sealed class AllowPointServerCommandConVar : StringConVar { }

/// <summary>
/// Whether an explosion uses the pre-Orange-Box rule for deciding what it can
/// reach. From <c>game/shared/gamerules.cpp:213</c> by way of
/// <c>sourcesharp/game/shared/shell_gamerules.cpp</c>, where it was one of the
/// symbols that file's eviction left undefined on the server.
///
/// <para>
/// <b>It is already read from C#.</b> <see cref="BlastSweep"/> asks for it by
/// name through <c>IEngineConsole.FindConVarValue</c>, once per explosion, and
/// has since radius damage became managed policy. A by-name read does not care
/// which side declared the name, so this move changes nothing about how it is
/// consumed — it moves the DECLARATION to sit beside the rule that reads it
/// instead of in a C++ file that no longer has any other reason to mention it.
/// </para>
///
/// <para>
/// <b>There is no native reader left.</b> The one the C++ declaration existed
/// for was <c>CGameRules::RadiusDamage</c> at gamerules.cpp:596, and
/// gamerules.cpp is in neither vpc —
/// <c>grep -n 'gamerules' src/game/client/client_csharp.vpc</c> names
/// <c>shell_gamerules.cpp</c> and nothing else.
/// </para>
///
/// <para>
/// <b>FCVAR_REPLICATED is stock's and is kept.</b> The value is the server's
/// and the client is told it; the flag is what makes that happen. Unlike
/// <see cref="AiLosModeConVar"/>, whose comment records the flag as an open
/// question, this one is settled: managed registration ORs the side's own DLL
/// identifier (<c>FCVAR_GAMEDLL</c> here) before construction, which is what a
/// replicated ConVar needs to be linked to its opposite number, and
/// <c>convar_dllflag_test</c> pins it.
/// </para>
/// </summary>
[ConVar("old_radiusdamage", "0.0", Flags = ConVarFlags.Replicated)]
public sealed class OldRadiusDamageConVar : FloatConVar { }

//-----------------------------------------------------------------------------
// THE TWO THAT STILL CANNOT COME, AND THE TWO THAT STOPPED BEING BLOCKED.
//
// This block used to say THREE COULD NOT COME, and gave a link error as the
// proof: ai_LOS_mode, sv_vehicle_autoaim_scale and default_fov were moved here
// as a probe and put back in C++ when the server link failed with
// `baseentity.o: undefined reference to 'ai_LOS_mode'`. An `extern ConVar` in a
// stock translation unit needs a native ConVar OBJECT at link time, and a
// managed [ConVar] does not provide one.
//
// TWO OF THOSE FOUR ARE NO LONGER BLOCKED, because the object that blocked them
// is no longer built. game/server/baseentity.cpp has left server_csharp.vpc:
// there is no baseentity.o on the server link line at all, so there is nothing
// left to hold the extern. Measured on a cold build at 216533fa8, over the
// objects the ninja graph's link line names (143 of them; 143 on disk, no
// evicted-TU ghosts):
//
//     ./1b-census.sh sym server ai_LOS_mode              -> 0 importers
//     ./1b-census.sh sym server sv_vehicle_autoaim_scale -> 0 importers
//
// Both declarations are now HERE, and the C++ that held them
// (shell_ai_absent.cpp, shell_subsystems_absent.cpp) is deleted in the same
// phase. PolicyKnobDeclarationTests has a fact for each.
//
// ONE IS STILL BLOCKED, AND IT WAS TWO -- each by a DIFFERENT object, so the two
// facts are independent:
//
//     default_fov     view.o (game/client/view.cpp) still imports it --
//                     measured 1 importer on the CLIENT link line (271 objects,
//                     no ghosts). view.cpp is Phase 7's file.
//     hud_fastswitch  MOVED, by lane 4c-render (plan_shim.md §6), to
//                     SourceSharp.Client/ConVars.cs. The ownership block this
//                     note recorded expired when that lane reached shell_hud.cpp;
//                     the measurement here was right and is what made the move a
//                     one-line decision rather than a re-investigation.
//
// They are named here rather than left out silently, so that the next reader
// asking "why are two of the shim's gameplay convars still in C++" finds the
// answer beside the ones that moved -- and so that nobody declares one here
// while the C++ declaration still stands, which is neither a compile error nor
// a link error and leaves the game with two convars of one name.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// plan_server_novalve.md P2.3b: THE LAST DECLARATIONS server.so MADE NATIVELY.
//
// Each keeps its stock (or shim) name, default, flags and help text, and the
// native code that still reads one holds a CManagedConVarRef of the same name
// (managed_convar_ref.h). Where an unresolved ref's 0 would select a different
// behaviour than the default, the native reader answers the default until the
// ConVar is registered; that is written at each reading site.
//
// `skill` and `developer` are not here: engine.so declares both, and the
// server's native copies were duplicates of names the engine already owned.
//-----------------------------------------------------------------------------

/// <summary>
/// The server's log verbosity: 0 errors, 1 startup, 2 per call. Read by
/// shell_diagnostics.cpp's SourceSharp_GetVerbosity (the client's
/// cl_sourcesharp_verbose is still native).
/// </summary>
[ConVar("sv_sourcesharp_verbose", "1",
        Help = "SourceSharp log verbosity: 0=errors, 1=startup, 2=verbose")]
public sealed class SvSourceSharpVerboseConVar : IntConVar { }

/// <summary>
/// A/B switch for re-asking vphysics's cached collision filter when a
/// standalone entity's solidity or collision group changes
/// (shell_standalone_base.cpp).
/// </summary>
[ConVar("sourcesharp_collision_rules_recheck", "1",
        Help = "SourceSharp: tell vphysics to re-ask its cached collision filter when a " +
               "standalone entity's solidity or collision group changes. 0 is the build " +
               "before that fix.")]
public sealed class CollisionRulesRecheckConVar : BoolConVar { }

/// <summary>
/// A/B switch for stock's VPhysicsInitNormal order: solidity before the
/// physics object (shell_standalone_base.cpp).
/// </summary>
[ConVar("sourcesharp_solidity_before_physics", "1",
        Help = "SourceSharp: set a standalone entity's solidity BEFORE creating its " +
               "physics object, as CBaseEntity::VPhysicsInitNormal does. 0 is the order " +
               "this port had: create, wake, then set solidity.")]
public sealed class SolidityBeforePhysicsConVar : BoolConVar { }

/// <summary>gamerules.cpp:62. German gibs and blood; read by world_adapter.cpp and the blood UTILs.</summary>
[ConVar("g_Language", "0", Flags = ConVarFlags.Replicated)]
public sealed class GLanguageConVar : IntConVar { }

/// <summary>
/// The movement port's A/B switch, whose 0 arm is now a named fatal refusal
/// (shell_movement.cpp). The client's half is still native.
/// </summary>
[ConVar("sourcesharp_movement_managed", "1",
        Flags = ConVarFlags.Replicated | ConVarFlags.Notify,
        Help = "Run the ten ported CGameMovement methods in C# (1) or in the stock C++ they forward from (0).")]
public sealed class MovementManagedConVar : BoolConVar { }

/// <summary>
/// The server's half of a replicated pair; the client's is still native
/// because stock view.cpp holds an <c>extern ConVar default_fov</c>.
/// </summary>
[ConVar("default_fov", "90", Flags = ConVarFlags.Replicated | ConVarFlags.Cheat,
        Help = "Default field of view.")]
public sealed class DefaultFovConVar : IntConVar { }

/// <summary>Which HL2 this is. No reader on either side; kept so a config naming it resolves.</summary>
[ConVar("hl2_episodic", "0", Flags = ConVarFlags.Replicated)]
public sealed class HL2EpisodicConVar : BoolConVar { }

//-----------------------------------------------------------------------------
// plan_server_novalve.md P2.3c: physics.cpp's three. Read there by name.
//-----------------------------------------------------------------------------

/// <summary>Stock's per-frame physics timing print (physics.cpp's PhysFrame).</summary>
[ConVar("phys_speeds", "0")]
public sealed class PhysSpeedsConVar : BoolConVar { }

/// <summary>
/// Stock's physics timescale, applied between the step clamps. A change resets
/// the simulation clock on the next physics frame (stock's change callback did
/// it at once).
/// </summary>
[ConVar("phys_timescale", "1", Help = "Scale time for physics")]
public sealed class PhysTimescaleConVar : FloatConVar { }

/// <summary>The automatic [SS-PHYSWORLD] teardown report; ss_physworld_report is the explicit one.</summary>
[ConVar("sourcesharp_physworld_report", "0",
        Help = "Write the [SS-PHYSWORLD] teardown report to /tmp/ss-physworld-<pid>.log. " +
               "Investigation scaffolding for the spawned-prop fall; off by default.")]
public sealed class PhysWorldReportConVar : BoolConVar { }

/// <summary>
/// Stock's per-hit damage print (player.cpp), asked by
/// <c>SsPlayer</c> after the rules let a hit through; this build's line also
/// names the hit's hitgroup. Cheat-protected, as stock's is.
/// </summary>
[ConVar("player_debug_print_damage", "0", Flags = ConVarFlags.Cheat,
        Help = "When true, print amount and type of all damage received by player to console.")]
public sealed class PlayerDebugPrintDamageConVar : BoolConVar { }
