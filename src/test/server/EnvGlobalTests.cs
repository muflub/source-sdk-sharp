using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>env_global</c> against the ONE global state table — stock's
/// <c>CEnvGlobal</c> (<c>game/server/logicentities.cpp:972</c>).
///
/// <para>
/// Before plan_entity.md's Phase 4 exit this entity wrote into a
/// <c>private static Dictionary&lt;string,int&gt;</c> of its own that nothing in
/// the tree ever read, while <c>logic_auto</c> and <c>multisource</c> — which
/// stock gates on the SAME table — read nothing at all. Every fact below was
/// unobservable then, because there was nowhere to observe it from.
/// </para>
/// </summary>
public class EnvGlobalTests
{
    private static readonly EntityHandle Ent = new(0x0001_0001u);

    private static (ScriptedEntityRegistry Registry, GlobalStateTable Globals, RecordingEngine Engine) Game()
    {
        // An entity's Remove() goes to its own registry's removal route
        // (plan_abi_hotreload D7), so no process-wide side has to be pinned
        // here any more: it was, because the route was one static that checked
        // Engine.ActiveSide, and this fact passed or failed on test order.
        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvGlobal).Assembly);

        var services = Services(out RecordingEngine engine);
        registry.Services = services;

        return (registry, (GlobalStateTable)services.Resolve<IServerGlobalState>()!, engine);
    }

    /// <summary>
    /// Spawns an <c>env_global</c> with <c>SF_GLOBAL_SET</c> ticked, which is
    /// what a mapper who wants the entity to declare anything must set.
    /// </summary>
    private static EnvGlobal? Spawn(ScriptedEntityRegistry registry, RecordingEngine engine,
                                    string globalName,
                                    int initialState = 1, int counter = 0, int spawnflags = 1)
    {
        // SpawnFlagsAs reads the ENGINE and not the keyvalue, so the double has
        // to answer -- the shape FuncButtonTests and EnvFogControllerTests use.
        engine.Returns("Entities.GetSpawnFlags", spawnflags);

        registry.KeyValue(Ent, EntityNames.EnvGlobal, "globalstate", globalName);
        registry.KeyValue(Ent, EntityNames.EnvGlobal, "initialstate", initialState.ToString());
        registry.KeyValue(Ent, EntityNames.EnvGlobal, "counter", counter.ToString());
        registry.Spawn(Ent, EntityNames.EnvGlobal);

        // registry.Spawn binds and keys the entity; OnSpawn is the entity's own
        // and is called by the map load, which a test stands in for.
        var global = registry.Get(Ent) as EnvGlobal;
        global?.OnSpawn();
        return registry.Get(Ent) as EnvGlobal;
    }

    //-------------------------------------------------------------------------
    // Spawn.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASpawnedEnvGlobalDeclaresItsGlobalInTheSharedTable()
    {
        // The whole point: a map's env_global now writes where logic_auto reads.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "predisaster", initialState: (int)GlobalState.On);

        Assert.Equal(GlobalState.On, globals.GetState(globals.IndexOf("predisaster")));
    }

    [Fact]
    public void TheInitialStateKeyIsAGlobalStateByNumber()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.Dead);

        Assert.Equal(GlobalState.Dead, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void WithoutTheSpawnflagNothingIsDeclaredAtSpawn()
    {
        // SF_GLOBAL_SET (logicentities.cpp:970) is "set global state to initial
        // state on spawn". Without it stock adds nothing, so the global stays
        // whatever a PREVIOUS map left it -- which is the entire point of a
        // cross-level table, and what an unconditional publish overwrote.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", spawnflags: 0);

        Assert.Equal(0, globals.Count);
    }

    [Fact]
    public void AnEnvGlobalNamingNoGlobalRemovesItself()
    {
        // Stock's first two lines: an env_global that names no global has
        // nothing to be.
        var (registry, _, engine) = Game();
        Spawn(registry, engine, "");

        // Remove() MARKS; the directory frees the batch at the end of the frame,
        // which is what FreeRemoved stands in for here. Asserting on Get alone
        // would pass whether or not the entity had asked to go.
        registry.Directory.FreeRemoved();

        Assert.True(registry.Get(Ent) is null, "an env_global with no globalstate key is gone");
    }

    [Fact]
    public void TheCounterKeyIsAppliedAtSpawn()
    {
        // Declared in EnvGlobalVars and read by NOTHING before this lane.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", counter: 5);

        Assert.Equal(5, globals.GetCounter(globals.IndexOf("g")));
    }

    [Fact]
    public void ACounterOfZeroIsNotApplied()
    {
        // Stock guards on `if ( m_counter != 0 )`, so a map that leaves the key
        // alone does not stamp a zero over a counter an earlier map set.
        var (registry, globals, engine) = Game();
        int i = globals.Add("g", "earlier_map", GlobalState.On);
        globals.SetCounter(i, 9);

        Spawn(registry, engine, "g", counter: 0);

        Assert.Equal(9, globals.GetCounter(i));
    }

    [Fact]
    public void SpawningOverAGlobalAnEarlierMapDeclaredDoesNotResetItsState()
    {
        // GlobalEntity_Add returns the existing index without writing, so the
        // state a previous level left survives this level's env_global.
        var (registry, globals, engine) = Game();
        int i = globals.Add("g", "earlier_map", GlobalState.Dead);

        Spawn(registry, engine, "g", initialState: (int)GlobalState.On);

        Assert.Equal(GlobalState.Dead, globals.GetState(i));
    }

    [Fact]
    public void ADeclaredGlobalCarriesTheMapThatIsLoaded()
    {
        var (registry, globals, engine) = Game();
        globals.OnLevelInitPreEntity("dm_lockdown");

        Spawn(registry, engine, "g");

        Assert.Equal("dm_lockdown", globals.GetMap(globals.IndexOf("g")));
    }

    //-------------------------------------------------------------------------
    // Inputs.
    //-------------------------------------------------------------------------

    [Fact]
    public void TurnOffSetsTheGlobalOff()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.On);

        registry.Input(Ent, "TurnOff", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.Off, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void TurnOnSetsTheGlobalOn()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.Off);

        registry.Input(Ent, "TurnOn", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.On, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void AnInputDeclaresTheGlobalWhenTheSpawnflagDidNot()
    {
        // Stock's every input opens with
        // `if ( !GlobalEntity_IsInTable(...) ) GlobalEntity_Add(...)`.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", spawnflags: 0);
        Assert.Equal(0, globals.Count);

        registry.Input(Ent, "TurnOn", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.On, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void RemoveMarksTheGlobalDeadRatherThanDeletingIt()
    {
        // THE OPPOSITE MEANING, and it was the other way round before this lane.
        // Deleting the entry reads as merely OFF; DEAD is a permanent refusal
        // that later maps honour -- util.cpp:1874 deletes any entity whose
        // globalname reads dead.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g");

        registry.Input(Ent, "Remove", "", 0.0f, EntityHandle.None);

        Assert.Equal(1, globals.Count);
        Assert.Equal(GlobalState.Dead, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void ToggleFlipsOnToOff()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.On);

        registry.Input(Ent, "Toggle", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.Off, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void ToggleFlipsOffToOn()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.Off);

        registry.Input(Ent, "Toggle", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.On, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void ToggleLeavesADeadGlobalDead()
    {
        // Stock's InputToggle returns without a write for anything that is not
        // exactly ON or OFF. A dead global is a decision a previous map made and
        // a toggle is not entitled to reverse it. The old implementation flipped
        // it, because it only knew about 0 and 1.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.Dead);

        registry.Input(Ent, "Toggle", "", 0.0f, EntityHandle.None);

        Assert.Equal(GlobalState.Dead, globals.GetState(globals.IndexOf("g")));
    }

    [Fact]
    public void SetCounterWritesTheCounterAndNotTheState()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g", initialState: (int)GlobalState.Off);

        registry.Input(Ent, "SetCounter", "7", 7.0f, EntityHandle.None);

        int i = globals.IndexOf("g");
        Assert.Equal(7, globals.GetCounter(i));
        Assert.Equal(GlobalState.Off, globals.GetState(i));
    }

    [Fact]
    public void AddToCounterAccumulates()
    {
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "g");

        registry.Input(Ent, "SetCounter", "4", 4.0f, EntityHandle.None);
        registry.Input(Ent, "AddToCounter", "3", 3.0f, EntityHandle.None);

        Assert.Equal(7, globals.GetCounter(globals.IndexOf("g")));
    }

    [Fact]
    public void GetCounterFiresTheCounterOutput()
    {
        // Stock's InputGetCounter / m_outCounter. There was NO managed half at
        // all: both the input and the output were open rows in
        // ContractBaseline, and both rows are deleted with this.
        var (registry, globals, engine) = Game();
        var global = Spawn(registry, engine, "g", counter: 12)!;

        registry.Input(Ent, "GetCounter", "", 0.0f, EntityHandle.None);

        Assert.Equal(1, global.Counter.FireCount);
        Assert.Equal("12", global.Counter.LastValue);
    }

    //-------------------------------------------------------------------------
    // The table it writes into is the one the other entities read.
    //-------------------------------------------------------------------------

    [Fact]
    public void AGlobalSetByEnvGlobalGatesALogicAuto()
    {
        // THE END-TO-END FACT, and it is the one nothing in this project could
        // have expressed before: the writer and the readers are one table.
        var (registry, _, engine) = Game();
        Spawn(registry, engine, "gate", initialState: (int)GlobalState.Off);

        EntityHandle autoEnt = new(0x0002_0002u);
        registry.KeyValue(autoEnt, EntityNames.LogicAuto, "globalstate", "gate");
        registry.Spawn(autoEnt, EntityNames.LogicAuto);
        var auto = (LogicAuto)registry.Get(autoEnt)!;

        auto.OnSpawn();
        registry.Think(autoEnt);

        Assert.Equal(0, auto.OnMapSpawn.FireCount);

        // And the other way: with the env_global on, the logic_auto fires.
        registry.Input(Ent, "TurnOn", "", 0.0f, EntityHandle.None);
        registry.Think(autoEnt);

        Assert.Equal(1, auto.OnMapSpawn.FireCount);
    }

    [Fact]
    public void GlobalNamesAreCaseSensitive()
    {
        // The private dictionary was OrdinalIgnoreCase; stock's CUtlSymbolTable
        // is built with bCaseInsensitive = false.
        var (registry, globals, engine) = Game();
        Spawn(registry, engine, "Predisaster");

        Assert.Equal(-1, globals.IndexOf("predisaster"));
    }
}
