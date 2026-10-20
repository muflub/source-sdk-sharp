using System.Collections.Immutable;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Every console command whose BODY is native, declared here —
/// plan_server_novalve.md P2.3c. server.so declares no command of its own: each
/// row below is registered through <see cref="ConCommandRegistry.RegisterNativeBodied"/>
/// like any managed command, and the native carrier the registration builds
/// finds the body of the same name (<c>SS_NATIVE_COMMAND</c>,
/// native_command_body.h) and runs it with no crossing.
///
/// <para>
/// <b>Why the bodies stay native.</b> Each reads native state — the physics
/// environment, the slot table, the transmit set, a payload's words, the
/// crossing counters — and prints it, or (use, cs_sv_reload) routes a line
/// somewhere. That is mechanism beside the state it reads. The name, help and
/// flags are what a player sees and are declared here, with the rest of the
/// server's commands.
/// </para>
///
/// <para>
/// Grouped by the file holding the body. NativeCommandTableTests holds this
/// table and the native bodies to each other, name for name.
/// </para>
/// </summary>
public static class NativeCommands
{
    public static readonly ImmutableArray<ConCommandAttribute> Server =
    [
        // shell_standalone_player.cpp
        new("use") { Help = "Use a particular weapon.\n\tArguments: <weapon classname>" },

        // sourcesharp_gamesystem.cpp
        new("cs_sv_reload") { Help = "Reload the SERVER's managed assemblies in place, without restarting the map" },
    ];

    /// <summary>
    /// The DIAGNOSTICS: commands whose native body lives in a <c>*_debug.cpp</c>,
    /// which CMake compiles only for a debug build (server_sourcesharp_debug_sources;
    /// user ruling 2026-10-01). Declared in every build so the table test can hold
    /// them to their bodies; registered only under <c>#if DEBUG</c> (ServerModule),
    /// the managed side's matching configuration.
    /// </summary>
    public static readonly ImmutableArray<ConCommandAttribute> Debug =
    [
        // shell_areaportal_debug.cpp
        // shell_debugoverlays_debug.cpp
        new("clear_debug_overlays") { Help = "clears debug overlays" },
        // shell_engineboundary_debug.cpp
        new("cs_sv_engine_vectors") { Help = "SourceSharp: have the engine fill two vectors on this map and give them back to its allocator (engine_heap.h)" },
        // shell_gamedll_debug.cpp
        new("cs_sv_transmitset") { Help = "SourceSharp: `arm` captures the next CheckTransmit's edict set for one client; no argument dumps it (plan_server.md Gate 5)" },
        // shell_gameents_debug.cpp
        new("ss_boundary_probe_report") { Help = "ss_boundary_probe_report -- engine-boundary call counts, with the caller's module and offset" },
        new("ss_boundary_probe_reset") { Help = "ss_boundary_probe_reset -- zero the engine-boundary counters" },
        // shell_nettable_debug.cpp
        new("cs_sv_payload_agree") { Help = "Check an entity's payload props against the bytes its blob holds, or all of them" },
        new("cs_sv_poke_payload") { Help = "cs_sv_poke_payload <entindex> <word> <value> — write one dword into an entity's blob", Flags = ConVarFlags.Cheat },
        new("cs_sv_payload_probe") { Help = "cs_sv_payload_probe <entindex> [seed] — fill an entity's payload with a checkable pattern", Flags = ConVarFlags.Cheat },
        new("cs_sv_break_schema") { Help = "cs_sv_break_schema <entindex> [hash] — send a wrong layout fingerprint so the client refuses it", Flags = ConVarFlags.Cheat },
        new("cs_sv_dump_types") { Help = "The published type table: classname, id, schema hash and tier" },
        // shell_playerinfo_debug.cpp
        // shell_scripted_debug.cpp
        new("cs_sv_invisible_reach") { Help = "SourceSharp: handles from the engine-invisible range that reached native code (plan_entity.md step 2e)" },
        // shell_standalone_base_debug.cpp
        new("cs_sv_handle_cross_check") { Help = "plan_entity.md 3.3: the slot table's two resolvers against the compiler's own crossing, on every standalone entity" },
        // shell_standalone_class_debug.cpp
        new("dumpentityfactories") { Help = "Lists every entity classname the server DLL can create. Pass any argument for entity sizes as well." },
        // shell_standalone_debug.cpp
        new("cs_standalone_spawn") { Help = "Spawn a standalone entity at x y z" },
        // shell_standalone_player_debug.cpp
        new("ss_ground") { Help = "What the standalone player is standing on, and how fast it is moving", Flags = ConVarFlags.Cheat },
        // shell_te_forwards_debug.cpp
        new("cs_sv_te_dispatch") { Help = "Fire DT_TEEffectDispatch through DispatchEffect() by effect name. cs_sv_te_dispatch <name|live|probe|all> [x y z]  -- 'live' fires the four names this build's own callers use, 'probe' fires four names no client effect claims so the client prints back the string it resolved from each index (needs developer 2), 'all' does both, interleaved.", Flags = ConVarFlags.Cheat },
        // shell_tempents_debug.cpp
        new("cs_sv_te_dump") { Help = "Every registered temp entity: its props as the ENGINE holds them, beside what C# declared" },
        // world_adapter_debug.cpp
        new("cs_sv_world") { Help = "Print worldspawn's networked state as the server holds it" },
        // gamestringpool_debug.cpp
        new("dumpgamestringtable") { Help = "Dump the contents of the game string table to the console.", Flags = ConVarFlags.Cheat },
        // generic_payload_debug.cpp
        new("cs_sv_dump_payload") { Help = "Build a generic-table payload prop set standalone and dump its offsets and proxies" },
        // shell_diagnostics_debug.cpp
        new("cs_sv_notshipped") { Help = "SourceSharp: what reached a path this server build does not ship." },
        new("cs_sv_crossings") { Help = "Print the managed<->native ABI crossing counts. Pass 'reset' to print the interval and start a new one." },
        // shell_movement_debug.cpp
        new("cs_sv_move_managed_report") { Help = "cs_sv_move_managed_report -- how many times each ported movement method ran in C# on the SERVER" },
        // shell_entityslots_debug.cpp
        new("cs_sv_slot_table") { Help = "SourceSharp: the entity slot table (plan_entity.md 3.3)" },
    ];

}
