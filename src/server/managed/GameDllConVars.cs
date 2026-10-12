using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// The seventeen ConVars <c>game/server/game.cpp</c> declared.
///
/// <para>
/// Phase 1d deleted that file and kept its declarations verbatim in
/// <c>sourcesharp/game/server/shell_gamedll_convars.cpp</c>, because a ConVar
/// is reached from outside a DLL by NAME and no link-line census can see such a
/// reader. plan_shim.md Phase 2c finishes the move: the names are still
/// registered in the engine's registry — <c>RegisterConVar</c> constructs a real
/// native <c>ConVar</c> — so every by-name reader still resolves, and the
/// DECLARATION (the name, the default, the flags, the help text) is where
/// plan_entity.md §6 says it belongs.
/// </para>
///
/// <para>
/// <b>The by-name readers this is holding up, re-measured on this branch:</b>
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>displaysoundlist</c> — <c>SourceSharp.Server/SoundEnt.cs:623</c>, through
/// <c>IntConVar("displaysoundlist")</c> / <c>FindConVarValue</c>. An
/// unregistered name answers nothing there and the caller takes its own
/// default, which for this one is indistinguishable from "the display is off".
/// </description></item>
/// <item><description>
/// <c>mapcyclefile</c> — the ENGINE, which looks it up by name.
/// </description></item>
/// <item><description>
/// <c>mp_teamplay</c>, <c>mp_weaponstay</c>, <c>mp_forcerespawn</c>,
/// <c>mp_footsteps</c>, <c>mp_flashlight</c> — the dedicated server's settings
/// dialog, <c>game/mod_test/cfg-seed/settings.scr</c> lines 79, 86, 93, 100 and 107,
/// which set them by name.
/// </description></item>
/// </list>
///
/// <para>
/// <b>Zero of the seventeen has a native reader any more, and plan_shim.md §7
/// says three do.</b> That section lists <c>displaysoundlist</c> ←
/// <c>shell_soundent.cpp</c>, <c>footsteps</c> ← a file Phase 1b deleted, and
/// <c>suitvolume</c> ← <c>shell_sentences.cpp</c>, and schedules the move for
/// Phase 5 behind them. Measured on this branch with
/// <c>tools/shim-census</c> plus a per-symbol <c>nm</c> join over all 118 server
/// objects on the debug link line, <c>shell_gamedll_convars.o</c> has exactly
/// three importers and they take <c>g_pDeveloper</c> (×2) and
/// <c>InitializeCvars</c> (×1). The two files §7 names mention the ConVars only
/// in comments (<c>shell_soundent.cpp:64</c>, <c>shell_sentences.cpp:67</c>).
/// So the move is not blocked, and it happens here rather than in Phase 5.
/// </para>
///
/// <para>
/// <b>Declared in the SERVER assembly, not Shared.</b> A ConVar class in Shared
/// is discovered by both modules' <c>RegisterAll</c> and registers the same name
/// twice in one process. See <see cref="ConVars"/>.
/// </para>
///
/// <para>
/// <b>Defaults, flags and help text are stock's exactly</b>, and
/// <c>GameDllConVarsTests</c> holds them against <c>game/server/game.cpp</c> —
/// the file this port deleted but did not throw away — rather than against a
/// table of expected strings written here.
/// </para>
/// </summary>
internal static class GameDllConVarsDoc
{
}

/// <summary>
/// Whether <c>CSoundEnt</c> draws its live sounds. Read by name from
/// <c>SoundEnt.cs</c>; stock declared it with no flags and no help.
/// </summary>
[ConVar("displaysoundlist", "0")]
public sealed class DisplaySoundListConVar : IntConVar { }

/// <summary>
/// The map cycle file the engine reads.
///
/// <para>
/// <b>Stock's <c>MapCycleFileChangedCallback</c> is NOT ported, and that is a
/// deletion of dead code rather than a dropped behaviour.</b> Its whole body was
/// <c>GameRules()-&gt;ResetMapCycleTimeStamp()</c>, and the only gamerules this
/// build has declares that method as
/// <c>virtual void ResetMapCycleTimeStamp( void ) OVERRIDE { return; }</c>
/// (<c>shell_gamerules.h:326</c>). The callback fired into an empty body.
/// Re-creating it here as an <c>OnChanged</c> that calls nothing would be a
/// managed copy of a no-op, and would read as a behaviour this port preserves.
/// </para>
///
/// <para>
/// The trailing space in the help string is stock's and is kept: the help text
/// is visible in the console and in <c>cvarlist</c>, and the test holds this
/// class against the .cpp rather than against a tidied copy.
/// </para>
/// </summary>
[ConVar("mapcyclefile", "mapcycle.txt",
        Help = "Name of the .txt file used to cycle the maps on multiplayer servers ")]
public sealed class MapCycleFileConVar : StringConVar { }

/// <summary>The config a dedicated server executes at startup.</summary>
[ConVar("servercfgfile", "server.cfg")]
public sealed class ServerCfgFileConVar : StringConVar { }

/// <summary>The config a listen server executes at startup.</summary>
[ConVar("lservercfgfile", "listenserver.cfg")]
public sealed class ListenServerCfgFileConVar : StringConVar { }

/// <summary>Team play. Set by name from the dedicated server's settings dialog.</summary>
[ConVar("mp_teamplay", "0", Flags = ConVarFlags.Notify)]
public sealed class TeamPlayConVar : BoolConVar { }

/// <summary>Whether players take falling damage.</summary>
[ConVar("mp_falldamage", "0", Flags = ConVarFlags.Notify)]
public sealed class FallDamageRuleConVar : BoolConVar { }

/// <summary>Whether a picked-up weapon stays where it was. Settings dialog.</summary>
[ConVar("mp_weaponstay", "0", Flags = ConVarFlags.Notify)]
public sealed class WeaponStayConVar : BoolConVar { }

/// <summary>Whether a dead player respawns without pressing a key. Settings dialog.</summary>
[ConVar("mp_forcerespawn", "1", Flags = ConVarFlags.Notify)]
public sealed class ForceRespawnConVar : BoolConVar { }

/// <summary>Whether players make footstep sounds. Settings dialog.</summary>
[ConVar("mp_footsteps", "1", Flags = ConVarFlags.Notify)]
public sealed class MpFootstepsConVar : BoolConVar { }

/// <summary>
/// Whether players may use the flashlight. Settings dialog.
///
/// <para>
/// <b>Stock declares this one TWICE</b> — <c>#ifdef CSTRIKE</c> at "1" and
/// <c>#else</c> at "0". CSTRIKE is on neither command line of this build, so
/// the CSTRIKE arm has always been dead; the arm this build takes is the one
/// declared here. Counter-Strike is not this mod.
/// </para>
/// </summary>
[ConVar("mp_flashlight", "0", Flags = ConVarFlags.Notify)]
public sealed class MpFlashlightConVar : BoolConVar { }

/// <summary>Whether the crosshair auto-aims.</summary>
[ConVar("mp_autocrosshair", "1", Flags = ConVarFlags.Notify)]
public sealed class AutoCrosshairConVar : BoolConVar { }

/// <summary>How many decals the world keeps.</summary>
[ConVar("decalfrequency", "10", Flags = ConVarFlags.Notify)]
public sealed class DecalFrequencyConVar : IntConVar { }

/// <summary>Stock's semicolon-separated team list.</summary>
[ConVar("mp_teamlist", "hgrunt;scientist", Flags = ConVarFlags.Notify)]
public sealed class TeamListConVar : StringConVar { }

/// <summary>Whether the map's own team settings override the server's.</summary>
[ConVar("mp_teamoverride", "1")]
public sealed class TeamOverrideConVar : BoolConVar { }

/// <summary>The team a player joins with no choice made.</summary>
[ConVar("mp_defaultteam", "0")]
public sealed class DefaultTeamConVar : IntConVar { }

/// <summary>Whether NPCs may spawn in multiplayer.</summary>
[ConVar("mp_allowNPCs", "1", Flags = ConVarFlags.Notify)]
public sealed class AllowNpcsConVar : BoolConVar { }

/// <summary>
/// The HEV suit's voice volume. The one of the seventeen stock made
/// <c>FCVAR_ARCHIVE</c>, so it is written to config.cfg and survives a restart.
/// </summary>
[ConVar("suitvolume", "0.25", Flags = ConVarFlags.Archive)]
public sealed class SuitVolumeConVar : FloatConVar { }
