using System;
using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE FALL-DAMAGE RULE IS GONE, AND THESE FACTS SAY SO BY REFLECTION.
///
/// <para>
/// <c>IServerModule.GetFallDamage</c> was a boundary member: native's
/// <c>CSourceSharpGameRules::FlPlayerFallDamage</c> asked it through the
/// <c>GetFallDamage</c> slot of <c>ManagedApi</c> (a struct until plan_client_vtables CE-last). Lane
/// <c>3e-sharedsys</c> retired that slot at ABI 249 and lane <c>4e-abi</c>
/// removed the field, which left the managed half declared, implemented, and
/// asked by nobody — <b>a method that can only ever answer itself</b>.
/// </para>
///
/// <para>
/// <b>Fall damage still happens.</b> It is <see cref="MoveMath.FallDamage"/>,
/// called from inside the managed movement loop
/// (<c>SourceSharp.Abi/Movement.cs</c>, the landing arm), and what pins THAT is
/// <c>MovementTests.ALandingHandsTheWorldTheDamageForItsOwnFallVelocity</c>
/// asserting the number a simulated 800 u/s landing produces. So this file asserts only the absence;
/// the presence has its own fact elsewhere and duplicating it here would be two
/// statements of one rule, which is the defect the fall-damage work started by
/// removing.
/// </para>
///
/// <para>
/// <b>Reflection rather than a source scan, deliberately.</b> An absence check
/// over source text has to find the tree first, and a repo-root walk can climb
/// out of a worktree into the main checkout and answer about the wrong one.
/// These read the assemblies that were just built from this tree, so there is
/// no path to get wrong.
/// </para>
/// </summary>
public class FallDamageRuleRetirementTests
{
    [Fact]
    public void TheModuleContractNoLongerDeclaresIt()
    {
        // The control. A GetMethod that has stopped finding anything would
        // pass this whatever the interface says, so the same lookup is made
        // for a member that IS there.
        Assert.NotNull(typeof(IServerModule).GetMethod("OnPlayerSpawned"));
        Assert.Null(typeof(IServerModule).GetMethod("GetFallDamage"));
    }

    [Fact]
    public void TheServerModuleNoLongerImplementsIt()
    {
        var methods = typeof(ServerModule).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        // The control, for the reason above: an empty method array passes the
        // absence trivially.
        Assert.Contains(methods, m => m.Name == "OnPlayerSpawned");

        Assert.DoesNotContain(methods,
            m => m.Name.EndsWith("GetFallDamage", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeHasNoSlotToAskItThrough()
    {
        // The reason the two above are correct rather than merely tidy. If a
        // later pass puts the slot back, the member has to come back with it —
        // a slot assigned to nothing is a native call through a null pointer.
        // The control: a slot that IS still in the struct, so this cannot pass
        // because reflection stopped seeing fields. (A rules slot until
        // plan_engineapi_vtables E-last removed the server's; MoveProcessMovement
        // is the client's and stays.)
        Assert.True(ManagedSlots.Has("MoveProcessMovement"));
        Assert.False(ManagedSlots.Has("GetFallDamage"));
    }

    [Fact]
    public void ItsConVarWentWithIt()
    {
        // cs_sv_fall_damage had exactly one reader and it was the deleted
        // method. Left declared it would be a console knob that turns nothing,
        // which is worse than not offering it: the console would accept it, the
        // registry would list it, and no fall would change.
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(GameRules).Assembly);

        // The control: an empty registry would pass the absence for nothing.
        Assert.Contains(registry.All, c =>
            string.Equals(c.Name, "cs_sv_spawn_health", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(registry.All, c =>
            string.Equals(c.Name, "cs_sv_fall_damage", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// THE TEAMPLAY RULE IS GONE, ON THE SAME EVIDENCE AND WITH ONE EXTRA LINK.
///
/// <para>
/// It was a chain of four: <c>IServerModule.IsTeamplay</c> (the boundary
/// contract), <c>ServerModule.IsTeamplay</c> (a delegation),
/// <c>IGameRules.IsTeamplay</c> (the service contract) and
/// <c>GameRules.IsTeamplay</c> (the rule itself, a tri-state ConVar read).
/// Native asked the first through the <c>IsTeamplay</c> slot of
/// <c>ManagedApi</c> (a struct until plan_client_vtables CE-last), which lane <c>3e-sharedsys</c> retired at ABI 249;
/// the other three were only ever reachable from it.
/// </para>
///
/// <para>
/// <b>NOTHING ANSWERS "is this teamplay" IN THIS BUILD, and that is the point
/// rather than an omission.</b> <c>CGameRules::IsTeamplay</c> went with the
/// eighteen callerless <c>IGameRules</c> slots — <c>gamerules_interface_test</c>
/// asserts it stays gone from <c>igamerules.h</c> and
/// <c>shell_gamerules.h</c> — so after the ABI slot went there was no asker on
/// either side of the boundary. A rule whose every answer is returned to nobody
/// is not an extension point waiting for a mod; it is
/// <see cref="Decision.UseStockRule"/> handed to a shell that is not there.
/// </para>
/// </summary>
public class TeamplayRuleRetirementTests
{
    [Fact]
    public void TheModuleContractNoLongerDeclaresIt()
    {
        Assert.NotNull(typeof(IServerModule).GetMethod("OnPlayerSpawned"));
        Assert.Null(typeof(IServerModule).GetMethod("IsTeamplay"));
    }

    [Fact]
    public void TheServiceContractNoLongerDeclaresIt()
    {
        // IGameRules is what a map entity can see of the rules. The control is
        // a sibling that IS still on it.
        Assert.NotNull(typeof(IGameRules).GetMethod("CanRespawn"));
        Assert.Null(typeof(IGameRules).GetMethod("IsTeamplay"));
    }

    [Fact]
    public void NeitherImplementationIsLeftBehind()
    {
        // Both halves, because deleting one and keeping the other is the
        // failure that reads as tidied-up: a delegation to nothing, or a rule
        // nothing delegates to.
        Assert.DoesNotContain(
            typeof(ServerModule).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            m => m.Name.EndsWith("IsTeamplay", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(GameRules).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            m => m.Name.EndsWith("IsTeamplay", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeHasNoSlotToAskItThrough()
    {
        Assert.True(ManagedSlots.Has("MoveProcessMovement"));
        Assert.False(ManagedSlots.Has("IsTeamplay"));
    }

    [Fact]
    public void ItsConVarWentWithIt()
    {
        // cs_sv_teamplay's only reader was GameRules.IsTeamplay. The control is
        // cs_sv_fraglimit, declared beside it in the same file and still read
        // by GameRules.Think.
        var registry = new ConVarRegistry(TestServices.UnattachedConsole());
        registry.RegisterAll(typeof(GameRules).Assembly);

        Assert.Contains(registry.All, c =>
            string.Equals(c.Name, "cs_sv_fraglimit", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(registry.All, c =>
            string.Equals(c.Name, "cs_sv_teamplay", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// THE MANAGED COPY OF STOCK'S SKILL CLAMP IS GONE, AND SO IS THE ONE FACT THAT
/// CALLED IT.
///
/// <para>
/// <c>SkillIsStillNative.Clamp</c> was the last piece of an abandoned move of the
/// <c>skill</c> ConVar into C#. It had the shape the fall-damage and teamplay
/// rules had — declared, implemented, and asked by nothing but the fact written
/// to ask it — with one difference: it never had an ABI slot to lose, because
/// nothing native ever reached it. <c>internal static</c>, so it was not an
/// extension point either: no assembly outside <c>SourceSharp.Server</c> could
/// see it, and nothing inside called it.
/// </para>
///
/// <para>
/// <b>The clamp still happens, in C++.</b> <c>CGameRules::SetSkillLevel</c>
/// (<c>game/shared/gamerules.h</c>) clamps to 1–3 at the read, and the knob is
/// the engine's own <c>skill</c>: the shim's duplicate declaration went in
/// plan_server_novalve.md P2.3b, which
/// <c>GameRulesPolicyTests.TheShimDeclaresSkillNowhereBecauseTheEngineOwnsIt</c> asserts, so
/// deleting the managed duplicate leaves the live answer where it lives.
/// </para>
///
/// <para>
/// <b>Its documentation was not merely duplicated, it was contradicted.</b> The
/// removed comment said the obstacle to a managed <c>skill</c> was that
/// <c>client_csharp.vpc</c> "still compiles stock <c>game/shared/gamerules.cpp</c>".
/// <c>GameRulesPolicyTests.TheClientNoLongerCompilesTheStockFileThatBlockedTheMove</c>
/// asserts that this stopped being true, and <c>shell_gamerules.cpp</c> carries
/// the corrected account beside the declaration: the name belongs to the ENGINE,
/// which registers it before either game DLL loads, so no phase of this port
/// frees it. Keeping a stale explanation next to dead arithmetic is worse than
/// keeping neither.
/// </para>
/// </summary>
public class SkillClampRetirementTests
{
    [Fact]
    public void TheManagedDuplicateOfTheNativeClampIsGone()
    {
        var server = typeof(GameRules).Assembly;

        // The control. A GetType that has stopped resolving anything in this
        // assembly would pass the absence below for the wrong reason, and the
        // sibling named here is the one the deleted comment pointed at.
        Assert.NotNull(server.GetType("SourceSharp.Server.GameRulesPolicy"));

        Assert.Null(server.GetType("SourceSharp.Server.SkillIsStillNative"));
    }
}

/// <summary>
/// THE REST OF THE RULES LAYER IS NOT DEAD, AND THESE FACTS SAY WHICH EVIDENCE
/// SAYS SO — lane <c>8c-ruleslayer</c>'s survey, written down.
///
/// <para>
/// The argument that retired fall damage and teamplay was "the ABI slot went, so
/// nothing can ask". Four of <see cref="IGameRules"/>' seven members had THAT
/// SLOT, armed at both ends: a field on <c>ManagedApi</c> (a struct until plan_client_vtables CE-last), a prototype in
/// <c>host/sourcesharp.h</c>, and a thunk the bootstrap assigned into it at
/// startup. A later pass that sees "no managed caller" and reads it as death has
/// to fail here first.
/// </para>
///
/// <para>
/// <b>Armed is not the same as asked, and the lane measured which.</b> Of the
/// four, only <c>OnPlayerSpawned</c> had a native CALL SITE
/// (<c>shell_standalone_player.cpp</c>), and <c>CanTakeDamage</c> is still named
/// by native code. The ABI pass this class deferred to ran in
/// plan_engineapi_vtables Phase 0: <c>CanRespawn</c> and <c>GetRespawnDelay</c>,
/// which nothing native named, lost their slots and their IServerModule forwards
/// and stay on <see cref="IGameRules"/>, where an entity asks them.
/// </para>
///
/// <para>
/// The other three — <c>IsProtected</c>, <c>RoundOver</c>, <c>Forget</c> — have no
/// slot and never had one, and that is ALSO not death. They are the entity-facing
/// half: <c>ServerModule</c> registers the rules in the container <b>by
/// contract</b>, <c>AddInstance&lt;IGameRules&gt;(_rules)</c>, precisely so that
/// what a map entity can see is the interface and not the class — which is what
/// keeps <c>Think</c>, <c>ResetRound</c> and <c>Reset</c> out of reach of anything
/// running inside a tick. "No override today" is the expected state of an
/// override nobody has written yet.
/// </para>
/// </summary>
public class RulesLayerSurveyTests
{
    /// <summary>
    /// The two that keep a slot, at every link of the chain. "Slotted" rather
    /// than "asked": see the class remark.
    /// </summary>
    // The rules native still asks. Native's link is the managed-built
    // IManagedClients since plan_engineapi_vtables E10 (the struct's slots were
    // removed at E-last); CanTakeDamage left the list then, having no native
    // caller to ask it.
    private static readonly string[] SlottedAtEveryLink =
    {
        "OnPlayerSpawned",
    };

    /// <summary>
    /// plan_engineapi_vtables Phase 0 answered the question this class left to
    /// the ABI pass: CanRespawn and GetRespawnDelay had a slot and no native
    /// call site, so their slots were retired and the IServerModule forwards
    /// went with them. The rules themselves stay on <see cref="IGameRules"/>,
    /// which is what a map entity asks.
    /// </summary>
    private static readonly string[] RetiredFromTheBoundary =
    {
        "CanRespawn", "GetRespawnDelay",
    };

    [Fact]
    public void TheRulesWhoseSlotWasRetiredStayOnTheServiceContract()
    {
        foreach (string rule in RetiredFromTheBoundary)
        {
            Assert.False(ManagedSlots.Has(rule));          // no slot (ABI 252)
            Assert.Null(typeof(IServerModule).GetMethod(rule));      // no boundary forward
            Assert.NotNull(typeof(IGameRules).GetMethod(rule));      // the service still declares it
        }

        // The controls: a slot still in the struct, and a rule still on the
        // boundary, answer non-null on the same lookups.
        Assert.True(ManagedSlots.Has("MoveProcessMovement"));
        Assert.NotNull(typeof(IServerModule).GetMethod("OnPlayerSpawned"));
    }

    [Fact]
    public void EverySlottedRuleIsDeclaredAtEveryLinkOfTheChain()
    {
        foreach (string rule in SlottedAtEveryLink)
        {
            Assert.NotNull(typeof(IManagedClients).GetMethod(rule)); // native can ask (E10)
            Assert.NotNull(typeof(IServerModule).GetMethod(rule));  // the boundary declares it
            Assert.NotNull(typeof(IGameRules).GetMethod(rule));     // the service declares it
        }

        // The in-fact control, kept here rather than as a one-off mutation: it
        // is what stops all three lookups passing because reflection stopped
        // discriminating. A retired name must answer null on the same calls the
        // live ones answer non-null on.
        Assert.Null(typeof(IManagedClients).GetMethod("GetFallDamage"));
        Assert.Null(typeof(IServerModule).GetMethod("GetFallDamage"));
        Assert.Null(typeof(IGameRules).GetMethod("IsTeamplay"));
    }

    [Fact]
    public void TheQueriesWithNoAbiSlotAreStillOnTheContractAnEntityResolves()
    {
        // No slot — so a census of the boundary finds nothing for these, and
        // that measurement must not be read as "delete them".
        Assert.False(ManagedSlots.Has("IsProtected"));
        Assert.False(ManagedSlots.Has("RoundOver"));

        // The control: a field on the same struct that IS there, so the two
        // nulls cannot pass because GetField stopped finding fields.
        Assert.True(ManagedSlots.Has("MoveProcessMovement"));

        // And they are still what a map entity can ask, which is why they stay.
        Assert.NotNull(typeof(IGameRules).GetMethod("IsProtected"));
        Assert.NotNull(typeof(IGameRules).GetProperty("RoundOver"));
        Assert.NotNull(typeof(IGameRules).GetMethod("Forget"));
    }

    [Fact]
    public void TheRulesAreReachableByContractRatherThanByClass()
    {
        // The registration is AddInstance<IGameRules>(_rules), so an entity
        // resolves the INTERFACE. If GameRules ever stopped implementing it the
        // container would hand out nothing and every extension point above would
        // be unreachable — which no compile error would report, because
        // ServerModule holds the concrete class in a field of its own.
        Assert.Contains(typeof(IGameRules), typeof(GameRules).GetInterfaces());

        // The lifecycle members are deliberately NOT on the contract. Asserting
        // their absence is what keeps the interface's promise — "anything here
        // can be asked at any moment without surprising what is running" — from
        // being widened by accident.
        Assert.Null(typeof(IGameRules).GetMethod("Think"));
        Assert.Null(typeof(IGameRules).GetMethod("ResetRound"));

        // The control for those two nulls: both exist on the class.
        Assert.NotNull(typeof(GameRules).GetMethod("Think"));
        Assert.NotNull(typeof(GameRules).GetMethod("ResetRound"));
    }
}

/// <summary>
/// Whether native has a managed slot of this name: a row of
/// docs/managed-interfaces.tsv, the one list of managed-built slots since
/// plan_client_vtables CE-last deleted the ManagedApi struct these facts read.
/// </summary>
internal static class ManagedSlots
{
    public static bool Has(string method)
    {
        string path = RepoLayout.Repo("docs", "managed-interfaces.tsv");
        Assert.True(File.Exists(path), $"{path} was not found; a lookup that read nothing would answer no");
        return File.ReadLines(path).Any(l => l.Split('\t') is [_, var m, ..] && m == method);
    }
}
