
using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two things <c>game/server/globalstate.cpp</c> left behind when
/// plan_shim.md Phase 2c took it out of the build: the platform globals its
/// last native caller declared, and the admin rule its two ConCommands opened
/// with.
///
/// <para>
/// Held against the C++ rather than against an opinion, because the C++ is
/// still on disk: the rule is
/// <c>sourcesharp/game/server/shell_commandclient.cpp</c>'s
/// <c>UTIL_IsCommandIssuedByServerAdmin</c>, and the two globals are the two
/// <c>GlobalEntity_Add</c> calls that stood at <c>world_adapter.cpp:1018-1019</c>.
/// </para>
/// </summary>
public class ServerAdminCommandsTests
{
    //-------------------------------------------------------------------------
    // 1. The admin rule, statement for statement against the C++.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheServerConsoleIsAdminOnAListenServer()
        => Assert.True(ServerAdmin.MayRun(ServerAdmin.ServerConsole, dedicatedServer: false));

    [Fact]
    public void TheServerConsoleIsAdminOnADedicatedServer()
    {
        // The C++ refuses only `IsDedicatedServer() && issuingPlayerIndex > 0`,
        // and the dedicated console's index is 0. This is the case ent_fire's
        // own rule deliberately refuses and this one must not: rcon and the
        // server console ARE the operator.
        Assert.True(ServerAdmin.MayRun(ServerAdmin.ServerConsole, dedicatedServer: true));
    }

    [Fact]
    public void TheListenServerHostIsAdmin()
        => Assert.True(ServerAdmin.MayRun(1, dedicatedServer: false));

    [Fact]
    public void AClientOnADedicatedServerIsNotAdminEvenInSlotOne()
        => Assert.False(ServerAdmin.MayRun(1, dedicatedServer: true));

    [Fact]
    public void ASecondPlayerIsNeverAdmin()
    {
        Assert.False(ServerAdmin.MayRun(2, dedicatedServer: false));
        Assert.False(ServerAdmin.MayRun(2, dedicatedServer: true));
    }

    [Fact]
    public void TheRuleIsNotEntFiresRule()
    {
        // The two differ on exactly one input and that difference is the reason
        // ServerAdmin exists rather than a call to EntFireAccess. A copy of the
        // wrong one here would make `dump_globals` unusable on a dedicated
        // server, silently -- the command would return without a word.
        Assert.True(ServerAdmin.MayRun(ServerAdmin.ServerConsole, dedicatedServer: true));
        Assert.False(EntFireAccess.SlotMayFire(
            EntFireAccess.IssuerSlot(EntFireAccess.NoIssuer, dedicatedServer: true),
            dedicatedServer: true));
    }

    //-------------------------------------------------------------------------
    // 2. The two platform globals CWorld::Spawn used to declare.
    //-------------------------------------------------------------------------

    private static GlobalStateTable AfterLevelInit(string map)
    {
        var table = new GlobalStateTable(new RecordingLog());
        ((IGameLifecycle)table).OnLevelInitPreEntity(map);
        return table;
    }

    [Fact]
    public void LevelInitDeclaresIsConsoleOff()
    {
        var table = AfterLevelInit("dm_lockdown");
        int i = table.IndexOf("is_console");

        Assert.True(i >= 0, "is_console was not declared at level init; "
            + "world_adapter.cpp no longer declares it either, so nothing does");
        Assert.Equal(GlobalState.Off, table.GetState(i));
    }

    [Fact]
    public void LevelInitDeclaresIsPcOn()
    {
        var table = AfterLevelInit("dm_lockdown");
        int i = table.IndexOf("is_pc");

        Assert.True(i >= 0, "is_pc was not declared at level init");
        Assert.Equal(GlobalState.On, table.GetState(i));
    }

    [Fact]
    public void TheTwoPlatformGlobalsAreOpposites()
    {
        // Stock wrote them as `IsConsole()` and `!IsConsole()`. Asserted as a
        // relation and not as two constants, so a future port to a platform
        // where the answer differs cannot leave them both On.
        var table = AfterLevelInit("dm_lockdown");

        Assert.NotEqual(table.GetState(table.IndexOf("is_console")),
                        table.GetState(table.IndexOf("is_pc")));
    }

    [Fact]
    public void ThePlatformGlobalsCarryTheMapThatDeclaredThem()
    {
        var table = AfterLevelInit("dm_lockdown");

        Assert.Equal("dm_lockdown", table.GetMap(table.IndexOf("is_console")));
        Assert.Equal("dm_lockdown", table.GetMap(table.IndexOf("is_pc")));
    }

    [Fact]
    public void ASecondLevelInitDoesNotResetThem()
    {
        // world_adapter.cpp called GlobalEntity_Add on EVERY level init and
        // relied on Add's "existing index, changed nothing" rule. This is the
        // same reliance, and it is what makes a global survive a map change:
        // if the second init reset the entry, is_console would carry the NEW
        // map's name and a global's map name is the whole of how the table
        // outlives a level.
        var table = new GlobalStateTable(new RecordingLog());
        ((IGameLifecycle)table).OnLevelInitPreEntity("dm_lockdown");

        int index = table.IndexOf("is_console");
        table.SetState(index, GlobalState.On);

        ((IGameLifecycle)table).OnLevelInitPreEntity("dm_runoff");

        Assert.Equal(index, table.IndexOf("is_console"));
        Assert.Equal(GlobalState.On, table.GetState(index));
        Assert.Equal("dm_lockdown", table.GetMap(index));
    }

    [Fact]
    public void TheTwoGlobalsAreTheOnlyOnesLevelInitDeclares()
    {
        // A floor AND a ceiling. Two is what world_adapter.cpp declared; a
        // third appearing here would be a global this port invented, and
        // env_global reads the table by name.
        var table = AfterLevelInit("dm_lockdown");

        Assert.Equal(2, table.Count);
    }

    //-------------------------------------------------------------------------
    // 3. The commands themselves -- that they are declared, and gated.
    //-------------------------------------------------------------------------

    [Fact]
    public void DumpGlobalsIsDeclaredUnderStocksName()
    {
        var attr = (ConCommandAttribute?)System.Attribute.GetCustomAttribute(
            typeof(DumpGlobalsCommand), typeof(ConCommandAttribute));

        Assert.NotNull(attr);
        Assert.Equal("dump_globals", attr!.Name);
    }

    [Fact]
    public void ServerGameTimeIsDeclaredUnderStocksName()
    {
        var attr = (ConCommandAttribute?)System.Attribute.GetCustomAttribute(
            typeof(ServerGameTimeCommand), typeof(ConCommandAttribute));

        Assert.NotNull(attr);
        Assert.Equal("server_game_time", attr!.Name);
    }

    /// <summary>
    /// The project's own engine double, answering only the members the rule
    /// reads. A rule that started asking the engine for something else would
    /// show up here as an unanswered member rather than as a silent default.
    /// </summary>
    private static RecordingEngine Engine(bool dedicated, EntityHandle commandClient,
                                          int slotHoldingIt = 0)
    {
        var engine = new RecordingEngine();
        engine.Returns("Host.IsDedicatedServer", dedicated);
        engine.Returns("Players.GetCommandClient", commandClient);

        // GetHandle is asked once per slot and the double answers per MEMBER,
        // so the slot is picked out by a factory that hands the real handle back
        // on the nth call. EntFireAccess.SlotOf walks upward from slot 1, so the
        // nth call IS slot n.
        int asked = 0;
        engine.ReturnsEachCall("Players.GetHandle",
            () => ++asked == slotHoldingIt ? commandClient : EntityHandle.None);

        return engine;
    }

    [Fact]
    public void ACommandFromTheServerConsoleIsAdminThroughTheEngineOverload()
    {
        // EntityHandle.None is what the engine answers when nobody typed it,
        // and SlotOf turns that into ServerConsole -- which is the mapping
        // UTIL_GetCommandClientIndex performs with CommandClient().Slot()+1.
        var engine = Engine(dedicated: false, commandClient: EntityHandle.None);

        Assert.True(ServerAdmin.MayRun(engine, engine));
    }

    [Fact]
    public void ACommandFromTheListenHostIsAdminThroughTheEngineOverload()
    {
        var host = new EntityHandle(4u);
        var engine = Engine(dedicated: false, commandClient: host, slotHoldingIt: 1);

        Assert.True(ServerAdmin.MayRun(engine, engine));
    }

    [Fact]
    public void ACommandFromSlotTwoIsRefusedThroughTheEngineOverload()
    {
        var second = new EntityHandle(7u);
        var engine = Engine(dedicated: false, commandClient: second, slotHoldingIt: 2);

        Assert.False(ServerAdmin.MayRun(engine, engine));
    }
}
