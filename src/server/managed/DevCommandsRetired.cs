using SourceSharp.Abi;

namespace SourceSharp.Server;

//-----------------------------------------------------------------------------
// The development commands this build cannot honour.
//
// One class each rather than one row each in a table, and the difference is not
// only tidiness: a class carries its own registration and its own help text, so
// the console can print WHY before the word is even typed, and `./debug-api
// commands managed` lists it beside everything else. A table row could do
// neither, and a table can disagree with the registrations — which is exactly
// how three of these came to be guarded out of the stock files and never
// re-registered while a test still called them answered.
//
// See RetiredDevCommand in DevCommands.cs for why they are registered at all
// rather than simply deleted.
//-----------------------------------------------------------------------------

[ConCommand("killvector",
    Help = "Retired: kills a player found by name through UTIL_PlayerByIndex. Use kill.")]
public sealed class KillVectorRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "it kills a player found BY NAME through UTIL_PlayerByIndex, which cannot " +
        "see a standalone player, and then calls ClientKill, which dereferences the " +
        "cast with no NULL check (divergences.md, Verdict C). Use kill or explode.";
}

[ConCommand("explodevector", Help = "Retired: see killvector. Use explode.")]
public sealed class ExplodeVectorRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "same as killvector — and stock's own registration passed bExplode=false, " +
        "so it never exploded anything even where it worked. Use explode.";
}

[ConCommand("test_dispatcheffect", Help = "Retired: DispatchEffect has no managed crossing.")]
public sealed class TestDispatchEffectRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "DispatchEffect has no managed crossing: the client's effect dispatcher is " +
        "reached through CEffectData, which is CBaseEntity-indexed.";
}

[ConCommand("particle_test_start",
    Help = "Retired: no particle system is wired to the managed hierarchy.")]
public sealed class ParticleTestStartRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "no particle system is wired to the managed hierarchy; particle_test_file " +
        "and its attach modes address entities through CBaseEntity indices.";
}

[ConCommand("particle_test_stop", Help = "Retired: nothing can have been started.")]
public sealed class ParticleTestStopRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "nothing can have been started — see particle_test_start.";
}

[ConCommand("physics_debug_entity", Help = "Retired: dumps a CBaseEntity's physics object.")]
public sealed class PhysicsDebugEntityRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "it dumps a CBaseEntity's IPhysicsObject through the SDK's own debug " +
        "printer; the managed physics surface (IEngineEntityPhysics) is the way in.";
}

[ConCommand("test_entity_blocker",
    Help = "Retired: entity blockers block NPCs, and this build compiles no AI.")]
public sealed class TestEntityBlockerRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "CEntityBlocker is a CBaseEntity that exists to block NPCs, and this build " +
        "compiles no AI (shell_ai_absent.cpp).";
}

[ConCommand("ent_call", Help = "Retired: VScript is not built into this mod.")]
public sealed class EntCallRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "VScript is not built into this mod; there is no script scope to call into.";
}

[ConCommand("ent_script_dump", Help = "Retired: VScript is not built into this mod.")]
public sealed class EntScriptDumpRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "VScript is not built into this mod; no entity has a script scope to dump.";
}

[ConCommand("ent_show_response_criteria", Help = "Retired: the response system left with the AI.")]
public sealed class EntShowResponseCriteriaRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "the response system left with the AI; nothing here selects responses.";
}

[ConCommand("ent_autoaim", Help = "Retired: autoaim left with the SDK weapon code.")]
public sealed class EntAutoaimRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "autoaim is a CBaseCombatCharacter radius read by the SDK weapon code that " +
        "left the build at the -player flip.";
}

[ConCommand("ent_attachments",
    Help = "Retired: NDebugOverlay has no managed crossing. Use ./debug-api attachments.")]
public sealed class EntAttachmentsRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "attachment points are drawn with NDebugOverlay, which has no managed " +
        "crossing. ./debug-api attachments reports the same data as text.";
}

[ConCommand("ent_messages", Help = "Retired: NDebugOverlay has no managed crossing.")]
public sealed class EntMessagesRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "it toggles an entity's I/O message OVERLAY, drawn in the world by the debug " +
        "overlay system, which has no managed crossing. ./debug-api watches I/O instead.";
}

[ConCommand("ent_cancelpendingentfires",
    Help = "Retired: cancellation is keyed by a CBaseEntity caller that a console ent_fire does not have.")]
public sealed class EntCancelPendingEntFiresRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "CEventQueue::CancelEvents cancels by CBaseEntity caller pointer, and an " +
        "ent_fire from this console has no CBaseEntity caller to name.";
}

[ConCommand("groundlist",
    Help = "Retired: it walks gEntList and the SDK ground-link table, neither of which a managed entity is in.")]
public sealed class GroundListRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "it walked gEntList and printed each entity's SDK ground-link list, and a " +
        "managed entity is in neither: the standalone hierarchy is not in gEntList, " +
        "and its ground entity is a handle in the engine block that " +
        "physics_main.cpp's groundlink table never sees. It went with " +
        "game/server/client.cpp at the Phase 4 exit, where it had been printing " +
        "'--- 0 links' for every entity this mod creates.";
}

[ConCommand("bug_swap",
    Help = "Retired: bugbait selection lived on the CBasePlayer inventory, which is gone.")]
public sealed class BugSwapRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "bugbait selection lived on the CBasePlayer inventory, which this build does " +
        "not have; the native body had already been `return;` under SOURCESHARP since " +
        "step 13c, so the command silently did nothing. It is registered here so it " +
        "SAYS nothing happened instead, which is what §7 asks of a refusal.";
}

[ConCommand("test_stringpool", Help = "Retired: the server's string pool is not tier1's CStringPool.")]
public sealed class TestStringPoolRetired(ILog log) : RetiredDevCommand(log)
{
    public override string Reason =>
        "it exercised tier1's CStringPool, which server.so no longer links " +
        "(plan_server_novalve.md P3); the game string pool is CServerStringPool, " +
        "held to its contract by server_string_pool_test. The client's still runs.";
}
