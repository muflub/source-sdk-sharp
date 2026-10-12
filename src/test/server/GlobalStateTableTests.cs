using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The cross-level global state table — <c>game/server/globalstate.cpp</c>'s
/// <c>CGlobalState</c>, ported by plan_entity.md's Phase 4 exit.
///
/// <para>
/// <b>The facts about the WRITE WINDOW are the reason this file exists.</b> The
/// window is what stops a level teardown being read as "every entity with a
/// globalname just died", and it had had no caller since Phase 7a converted
/// <c>CGlobalState</c> from a <c>CAutoGameSystem</c> to a <c>CServerSystem</c>
/// without adding a row for it to <c>kServerSystems[]</c>. Nothing warned,
/// nothing crashed, and no gate that loads one map could see it — it is the
/// project's recorded silent-loss shape, in this lane's own subject.
/// </para>
/// </summary>
public class GlobalStateTableTests
{
    private static GlobalStateTable Table() => new(new RecordingLog());

    //-------------------------------------------------------------------------
    // The table itself.
    //-------------------------------------------------------------------------

    [Fact]
    public void AnAddedGlobalReadsBackByIndex()
    {
        var table = Table();
        int i = table.Add("gordon_precriminal", "dm_lockdown", GlobalState.On);

        Assert.Equal(GlobalState.On, table.GetState(i));
        Assert.Equal("gordon_precriminal", table.GetName(i));
        Assert.Equal("dm_lockdown", table.GetMap(i));
    }

    [Fact]
    public void AnAddedGlobalIsFoundByName()
    {
        var table = Table();
        int i = table.Add("super_phys_gun", "dm_lockdown", GlobalState.On);

        Assert.Equal(i, table.IndexOf("super_phys_gun"));
    }

    [Fact]
    public void AGlobalThatWasNeverAddedHasNoIndex()
        => Assert.Equal(-1, Table().IndexOf("never_declared"));

    [Fact]
    public void NameMatchingIsCaseSensitive()
    {
        // Stock compared interned CUtlSymbols and CUtlSymbolTable's default
        // constructor takes bCaseInsensitive = false, so these are two globals
        // to the SDK and must be two here.
        var table = Table();
        table.Add("Gordon", "a", GlobalState.On);

        Assert.Equal(-1, table.IndexOf("gordon"));
    }

    [Fact]
    public void AnEmptyNameIsRefused()
        => Assert.Equal(-1, Table().Add("", "dm_lockdown", GlobalState.On));

    [Fact]
    public void AnIndexThatNamesNothingReadsOff()
    {
        // Load-bearing, not defensive: every by-NAME overload in globalstate.h
        // resolves the name first and passes -1 straight through, so "no such
        // global" and "that global is off" are deliberately one answer.
        var table = Table();

        Assert.Equal(GlobalState.Off, table.GetState(-1));
        Assert.Equal(GlobalState.Off, table.GetState(7));
    }

    [Fact]
    public void AnIndexThatNamesNothingHasNoNameAndNoMap()
    {
        var table = Table();

        Assert.Null(table.GetName(-1));
        Assert.Null(table.GetMap(3));
    }

    [Fact]
    public void AnIndexThatNamesNothingHasACounterOfZero()
        => Assert.Equal(0, Table().GetCounter(-1));

    [Fact]
    public void TheCountIsTheNumberOfDistinctGlobals()
    {
        var table = Table();
        table.Add("a", "m", GlobalState.On);
        table.Add("b", "m", GlobalState.Off);
        table.Add("a", "m", GlobalState.Dead);

        Assert.Equal(2, table.Count);
    }

    //-------------------------------------------------------------------------
    // Re-declaring a global. Stock's AddEntity builds the record, asks GetIndex,
    // and returns the EXISTING index without writing anything -- which is what
    // lets world_adapter.cpp call Add("is_console", ...) on every level init
    // without erasing what an earlier level did.
    //-------------------------------------------------------------------------

    [Fact]
    public void ReAddingAGlobalAnswersTheIndexItAlreadyHad()
    {
        var table = Table();
        int first = table.Add("is_console", "dm_lockdown", GlobalState.Off);

        Assert.Equal(first, table.Add("is_console", "dm_powerhouse", GlobalState.On));
    }

    [Fact]
    public void ReAddingAGlobalDoesNotOverwriteItsState()
    {
        var table = Table();
        int i = table.Add("is_console", "dm_lockdown", GlobalState.Off);
        table.SetState(i, GlobalState.Dead);

        table.Add("is_console", "dm_powerhouse", GlobalState.On);

        // Looked up BY NAME rather than through the index taken before, and the
        // count asserted beside it. Reading the old index alone passes even when
        // the re-add appends a SECOND entry under the same name -- which is
        // exactly what the early return prevents, and what a mutation of it
        // produces. Verified: mutating the early return out reds this.
        Assert.Equal(1, table.Count);
        Assert.Equal(GlobalState.Dead, table.GetState(table.IndexOf("is_console")));
        Assert.Equal(i, table.IndexOf("is_console"));
    }

    [Fact]
    public void ReAddingAGlobalDoesNotOverwriteItsMap()
    {
        var table = Table();
        int i = table.Add("is_console", "dm_lockdown", GlobalState.Off);

        table.Add("is_console", "dm_powerhouse", GlobalState.On);

        Assert.Equal(1, table.Count);
        Assert.Equal("dm_lockdown", table.GetMap(table.IndexOf("is_console")));
    }

    [Fact]
    public void ReAddingAGlobalDoesNotResetItsCounter()
    {
        var table = Table();
        int i = table.Add("hunters_to_run_over", "dm_lockdown", GlobalState.On);
        table.SetCounter(i, 4);

        table.Add("hunters_to_run_over", "dm_lockdown", GlobalState.On);

        Assert.Equal(1, table.Count);
        Assert.Equal(4, table.GetCounter(table.IndexOf("hunters_to_run_over")));
    }

    //-------------------------------------------------------------------------
    // The counter.
    //-------------------------------------------------------------------------

    [Fact]
    public void ACounterStartsAtZero()
        => Assert.Equal(0, Table().GetCounter(Table().Add("c", "m", GlobalState.On)));

    [Fact]
    public void AddToCounterAnswersTheNewValue()
    {
        var table = Table();
        int i = table.Add("c", "m", GlobalState.On);
        table.SetCounter(i, 10);

        Assert.Equal(7, table.AddToCounter(i, -3));
    }

    [Fact]
    public void AddToCounterOnAnIndexThatNamesNothingAnswersZero()
        => Assert.Equal(0, Table().AddToCounter(-1, 5));

    //-------------------------------------------------------------------------
    // THE WRITE WINDOW. See this file's header.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheWindowIsOpenOnAFreshTable()
        => Assert.True(Table().UpdatesEnabled);

    [Fact]
    public void ALevelShutdownShutsTheWindow()
    {
        var table = Table();
        table.OnLevelShutdownPreEntity();

        Assert.False(table.UpdatesEnabled);
    }

    [Fact]
    public void TheEndOfALevelShutdownReopensTheWindow()
    {
        var table = Table();
        table.OnLevelShutdownPreEntity();
        table.OnLevelShutdownPostEntity();

        Assert.True(table.UpdatesEnabled);
    }

    [Fact]
    public void AnEntityDyingDuringALevelTeardownCannotMarkItsGlobalDead()
    {
        // THE BUG THE WINDOW EXISTS FOR, written as the sequence that produces
        // it. CBaseEntity::UpdateOnRemove (baseentity.cpp:2542) runs
        // GlobalEntity_SetState( m_iGlobalname, GLOBAL_DEAD ) for every entity
        // with a globalname, and during a level teardown every such entity is
        // removed. Without the window they are all GLOBAL_DEAD afterwards, and
        // util.cpp:1874 then DELETES each of them on every later map.
        var table = Table();
        int i = table.Add("gordon_precriminal", "dm_lockdown", GlobalState.On);

        table.OnLevelShutdownPreEntity();
        table.SetState(i, GlobalState.Dead);
        table.OnLevelShutdownPostEntity();

        Assert.Equal(GlobalState.On, table.GetState(i));
    }

    [Fact]
    public void AWriteLandsAgainOnceTheLevelHasGone()
    {
        var table = Table();
        int i = table.Add("g", "m", GlobalState.On);

        table.OnLevelShutdownPreEntity();
        table.OnLevelShutdownPostEntity();
        table.SetState(i, GlobalState.Dead);

        Assert.Equal(GlobalState.Dead, table.GetState(i));
    }

    [Fact]
    public void TheWindowAlsoSuppressesACounterAssignment()
    {
        var table = Table();
        int i = table.Add("g", "m", GlobalState.On);
        table.SetCounter(i, 3);

        table.OnLevelShutdownPreEntity();
        table.SetCounter(i, 99);

        Assert.Equal(3, table.GetCounter(i));
    }

    [Fact]
    public void TheWindowAlsoSuppressesACounterIncrement()
    {
        var table = Table();
        int i = table.Add("g", "m", GlobalState.On);
        table.SetCounter(i, 3);

        table.OnLevelShutdownPreEntity();

        Assert.Equal(0, table.AddToCounter(i, 5));
        Assert.Equal(3, table.GetCounter(i));
    }

    [Fact]
    public void TheWindowDoesNotSuppressHandingAGlobalToANewMap()
    {
        // SetMap is deliberately NOT gated, and stock's is not either: handing a
        // global from one level to the next is the one write a level transition
        // has to be able to make while the window is shut.
        var table = Table();
        int i = table.Add("g", "dm_lockdown", GlobalState.On);

        table.OnLevelShutdownPreEntity();
        table.SetMap(i, "dm_powerhouse");

        Assert.Equal("dm_powerhouse", table.GetMap(i));
    }

    [Fact]
    public void TheRespawnSweepCanShutTheWindowByItself()
    {
        // CGlobalEntityList::FrameUpdatePostEntityThink brackets its
        // respawn_entities sweep with EnableStateUpdates(false/true), for the
        // same reason and on a LIVE level rather than at teardown.
        var table = Table();
        int i = table.Add("g", "m", GlobalState.On);

        table.EnableUpdates(false);
        table.SetState(i, GlobalState.Dead);
        table.EnableUpdates(true);

        Assert.Equal(GlobalState.On, table.GetState(i));
    }

    //-------------------------------------------------------------------------
    // The cross-level property. Nothing clears this table, on purpose.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheTableSurvivesALevelShutdown()
    {
        // The global state table is the ONE piece of game state that is meant to
        // outlive a level -- that is what GetMap is for. A Reset at shutdown
        // would be a behaviour change wearing a bug fix's clothes, so the
        // absence of one is asserted rather than left to be noticed.
        var table = Table();
        int i = table.Add("g", "dm_lockdown", GlobalState.Dead);

        table.OnLevelShutdownPreEntity();
        table.OnLevelShutdownPostEntity();

        Assert.Equal(1, table.Count);
        Assert.Equal(GlobalState.Dead, table.GetState(i));
        Assert.Equal("dm_lockdown", table.GetMap(i));
    }

    //-------------------------------------------------------------------------
    // Where it sits in the sequence.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheWindowCountsThatItWasActuallyDriven()
    {
        // "REGISTERED" IS NOT "REACHED". CGlobalState was a game system nobody
        // added a row for, and the window went unshut for six months with
        // nothing saying so. dump_globals prints these two, so a live session
        // can answer the question a registration check cannot.
        var table = Table();

        Assert.Equal(0, table.WindowsShut);
        Assert.Equal(0, table.WindowsReopened);

        table.OnLevelShutdownPreEntity();
        table.OnLevelShutdownPostEntity();
        table.OnLevelShutdownPreEntity();
        table.OnLevelShutdownPostEntity();

        Assert.Equal(2, table.WindowsShut);
        Assert.Equal(2, table.WindowsReopened);
    }

    [Fact]
    public void TheTableIsALifecycleParticipant()
        => Assert.IsAssignableFrom<IGameLifecycle>(Table());

    [Fact]
    public void TheWindowShutsBeforeAnyParticipantThatDestroysAnEntity()
    {
        // LifecycleDriver dispatches every stage in ASCENDING order -- the
        // shutdown passes are not walked backwards here -- so the window has to
        // open at a number below every participant that tears entities down.
        int window = ((IGameLifecycle)Table()).LifecycleOrder;

        Assert.True(window < (int)LifecycleStage.Standalone,
            "the parallel hierarchy's teardown deletes every standalone entity");
        Assert.True(window < (int)LifecycleStage.Corpses,
            "corpses hold entity handles and are cleared in the same pass");
        Assert.True(window < (int)LifecycleStage.Arsenal,
            "inventories hold handles that do not survive the map");
        Assert.Equal((int)LifecycleStage.GlobalState, window);
    }

    //-------------------------------------------------------------------------
    // The dump.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryStateHasStocksOwnName()
    {
        // Stock's estates[] = { "Off", "On", "Dead" }, indexed by the enum. A
        // reader comparing a managed dump_globals against an SDK one is
        // comparing the same lines.
        Assert.Equal("Off", GlobalStateTable.StateName(GlobalState.Off));
        Assert.Equal("On", GlobalStateTable.StateName(GlobalState.On));
        Assert.Equal("Dead", GlobalStateTable.StateName(GlobalState.Dead));
    }

    [Fact]
    public void AStateOutsideTheEnumDoesNotIndexPastTheEnd()
    {
        // Stock's estates[state] would read off the end of a three-element array
        // for anything else, and the value arrives from a map keyvalue cast
        // straight to the enum.
        Assert.Equal("?", GlobalStateTable.StateName((GlobalState)9));
    }

    //-------------------------------------------------------------------------
    // The numbers are the wire.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheEnumCarriesStocksOwnNumbers()
    {
        // env_global carries the initial state as an INTEGER map key and stock
        // casts it straight to GLOBALESTATE, so a map authored for the SDK sets
        // these by number. They may not be renumbered.
        Assert.Equal(0, (int)GlobalState.Off);
        Assert.Equal(1, (int)GlobalState.On);
        Assert.Equal(2, (int)GlobalState.Dead);
    }
}
