using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The two map entities that declare a <c>globalstate</c> key — and, until
/// plan_entity.md's Phase 4 exit, DID NOT READ IT.
///
/// <para>
/// <b>This is the project's recorded silent-loss shape, found for the sixth
/// time.</b> <c>LogicAutoVars.GlobalState</c> and <c>MultiSourceVars.GlobalState</c>
/// have existed since those entities were ported; the map could set them, the
/// keyvalue landed, and neither body ever consulted the value. Stock gates both
/// on it:
/// </para>
///
/// <code>
/// logicauto.cpp:87        if (!m_globalstate || GlobalEntity_GetState(m_globalstate) == GLOBAL_ON)
/// logicentities.cpp:1366  if ( !m_globalstate || GlobalEntity_GetState( m_globalstate ) == GLOBAL_ON )
/// </code>
///
/// <para>
/// It could not be fixed before now because the state those keys name had no
/// managed owner: <c>globalstate.cpp</c> was unowned by any plan step and its
/// table was native. It cost nothing on <c>dm_lockdown</c>, whose
/// <c>logic_auto</c> sets no <c>globalstate</c> — which is exactly why no live
/// gate could have found it.
/// </para>
/// </summary>
public class GlobalStateGatedEntityTests
{
    private static readonly EntityHandle Ent = new(0x0001_0001u);

    private static (ScriptedEntityRegistry Registry, GlobalStateTable Globals) Game()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(LogicAuto).Assembly);

        var services = Services();
        registry.Services = services;

        // The container's own instance rather than a replacement, so these tests
        // exercise the registration ServerModule makes and not one of their own.
        return (registry, (GlobalStateTable)services.Resolve<IServerGlobalState>()!);
    }

    private static T Spawn<T>(ScriptedEntityRegistry registry, string className)
        where T : ScriptedEntity
    {
        registry.Spawn(Ent, className);
        return (T)registry.Get(Ent)!;
    }

    //-------------------------------------------------------------------------
    // logic_auto
    //-------------------------------------------------------------------------

    [Fact]
    public void ALogicAutoWithNoGlobalStateKeyStillFires()
    {
        // The first clause of stock's condition. An unnamed global resolves to
        // index -1, which reads Off -- so an implementation that dropped
        // `!m_globalstate` would silence EVERY logic_auto on every map, which is
        // the failure mode opposite to the one being fixed.
        var (registry, _) = Game();
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.Equal(1, auto.OnMapSpawn.FireCount);
    }

    [Fact]
    public void ALogicAutoGatedOnAGlobalThatIsOffFiresNothing()
    {
        var (registry, globals) = Game();
        globals.Add("chapter_two_started", "dm_lockdown", GlobalState.Off);

        registry.KeyValue(Ent, EntityNames.LogicAuto, "globalstate", "chapter_two_started");
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.Equal(0, auto.OnMapSpawn.FireCount);
        Assert.Equal(0, auto.OnMultiNewMap.FireCount);
    }

    [Fact]
    public void ALogicAutoGatedOnAGlobalThatIsOnFires()
    {
        var (registry, globals) = Game();
        globals.Add("chapter_two_started", "dm_lockdown", GlobalState.On);

        registry.KeyValue(Ent, EntityNames.LogicAuto, "globalstate", "chapter_two_started");
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.Equal(1, auto.OnMapSpawn.FireCount);
    }

    [Fact]
    public void ALogicAutoGatedOnAGlobalThatIsDeadFiresNothing()
    {
        // Stock tests for GLOBAL_ON, not for "not off": DEAD is also a refusal.
        var (registry, globals) = Game();
        globals.Add("g", "dm_lockdown", GlobalState.Dead);

        registry.KeyValue(Ent, EntityNames.LogicAuto, "globalstate", "g");
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.Equal(0, auto.OnMapSpawn.FireCount);
    }

    [Fact]
    public void ALogicAutoNamingAGlobalNobodyDeclaredFiresNothing()
    {
        // A named global that is not in the table reads Off, so a typo in the
        // map silences the entity -- which is stock's behaviour and is what a
        // mapper debugging with dump_globals expects to see.
        var (registry, _) = Game();

        registry.KeyValue(Ent, EntityNames.LogicAuto, "globalstate", "not_declared");
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.Equal(0, auto.OnMapSpawn.FireCount);
    }

    [Fact]
    public void ABlockedFireOnceLogicAutoIsNotRemoved()
    {
        // logicauto.cpp wraps the outputs AND the SF_AUTO_FIREONCE removal in
        // the one `if`, so a logic_auto whose global is off SURVIVES. Removing
        // it here would make the block permanent, and would be invisible against
        // a stock build on any map where the global happens to be on.
        var (registry, globals) = Game();
        globals.Add("g", "dm_lockdown", GlobalState.Off);

        registry.KeyValue(Ent, EntityNames.LogicAuto, "globalstate", "g");
        registry.KeyValue(Ent, EntityNames.LogicAuto, "spawnflags", "1");
        var auto = Spawn<LogicAuto>(registry, EntityNames.LogicAuto);

        auto.OnSpawn();
        registry.Think(Ent);

        Assert.True(registry.Get(Ent) is not null,
            "a blocked logic_auto is still there to be enabled later");
    }

    //-------------------------------------------------------------------------
    // multisource
    //-------------------------------------------------------------------------

    [Fact]
    public void AMultiSourceWithNoGlobalStateKeyStillFires()
    {
        var (registry, _) = Game();
        var multi = Spawn<MultiSource>(registry, EntityNames.Multisource);

        registry.Input(Ent, "Trigger", "", 0.0f, new EntityHandle(11));

        Assert.Equal(1, multi.OnTrigger.FireCount);
    }

    [Fact]
    public void AMultiSourceGatedOnAGlobalThatIsOffDoesNotFire()
    {
        var (registry, globals) = Game();
        globals.Add("g", "dm_lockdown", GlobalState.Off);

        registry.KeyValue(Ent, EntityNames.Multisource, "globalstate", "g");
        var multi = Spawn<MultiSource>(registry, EntityNames.Multisource);

        registry.Input(Ent, "Trigger", "", 0.0f, new EntityHandle(11));

        Assert.Equal(0, multi.OnTrigger.FireCount);
    }

    [Fact]
    public void AMultiSourceRememberedItsSourcesWhileTheGlobalWasOff()
    {
        // The global gates the FIRING and not the COUNTING, which is stock's
        // shape: IsTriggered checks the global last, after the sources are in.
        // So nothing is consumed by a refusal and the entity fires as soon as
        // the global comes on and another source reports.
        var (registry, globals) = Game();
        int i = globals.Add("g", "dm_lockdown", GlobalState.Off);

        registry.KeyValue(Ent, EntityNames.Multisource, "globalstate", "g");
        var multi = Spawn<MultiSource>(registry, EntityNames.Multisource);

        registry.Input(Ent, "Trigger", "", 0.0f, new EntityHandle(11));
        Assert.Equal(0, multi.OnTrigger.FireCount);

        globals.SetState(i, GlobalState.On);
        registry.Input(Ent, "Trigger", "", 0.0f, new EntityHandle(12));

        Assert.Equal(1, multi.OnTrigger.FireCount);
    }
}
