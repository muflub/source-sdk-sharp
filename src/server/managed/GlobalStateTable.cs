//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The cross-level global state table: <c>game/server/globalstate.cpp</c>'s
/// <c>CGlobalState</c>, ported.
///
/// <para>
/// A map names a piece of state (<c>env_global</c>'s <c>globalstate</c>,
/// <c>logic_auto</c>'s and <c>multisource</c>'s <c>globalstate</c>, an entity's
/// <c>globalname</c>), and this table remembers, for the life of the PROCESS,
/// what that name is set to, which map last owned it, and a counter beside it.
/// It is the one piece of game state that deliberately outlives a level, which
/// is why nothing clears it between maps and why <c>GetMap</c> exists at all:
/// stock's <c>UTIL_LoadAndSpawn</c> arm reads it to decide whether an entity
/// with a <c>globalname</c> belongs to THIS level yet.
/// </para>
///
/// <para>
/// <b>Every line of it is a decision about what the game does</b> — whether an
/// entity spawns at all, whether <c>logic_auto</c> fires, whether a
/// <c>multisource</c> is considered triggered — which is why it is here and not
/// in C++. Nothing about it is a wire format, a vtable or a fixed-offset
/// struct: it is a list, a name lookup and three integers.
/// </para>
///
/// <para>
/// <b>Not static and not a singleton.</b> Stock kept <c>gGlobalState</c> at file
/// scope, which is what made every one of its accessors a free function and
/// what makes stock's version untestable without a running server. One instance
/// is registered for the game; a test makes its own and nothing leaks between
/// them.
/// </para>
/// </summary>
public sealed class GlobalStateTable(ILog log) : IServerGlobalState, IGameLifecycle
{
    /// <summary>
    /// One named piece of global state.
    ///
    /// A class rather than a record struct because <see cref="State"/> and
    /// <see cref="Counter"/> are written in place through the list, and a value
    /// type in a <c>List&lt;T&gt;</c> hands out copies.
    /// </summary>
    private sealed class Entry
    {
        public required string Name { get; init; }
        public required string LevelName { get; set; }
        public required GlobalState State { get; set; }
        public int Counter { get; set; }
    }

    // Insertion-ordered, because the INDEX is the identity every caller holds:
    // GlobalEntity_GetIndex hands one out and every other entry point takes one
    // back. Nothing is ever removed, so an index stays valid for the life of the
    // process -- which is exactly what stock's CUtlVector gave, and callers rely
    // on it across level changes.
    private readonly List<Entry> _entries = new();

    // Name -> index, so GetIndex is not a linear scan. Stock's was (it walked
    // m_list backwards comparing CUtlSymbols); this answers the same question
    // with the same case sensitivity.
    //
    // ORDINAL, not case-insensitive. Stock compared interned CUtlSymbols, and
    // CUtlSymbolTable's default constructor takes bCaseInsensitive = false --
    // so "Gordon_Precriminal" and "gordon_precriminal" are two different
    // globals to stock and are two here.
    private readonly Dictionary<string, int> _byName = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public int LifecycleOrder => (int)LifecycleStage.GlobalState;

    /// <summary>
    /// Whether a write is allowed to land. False for the duration of a level
    /// teardown — see <see cref="OnLevelShutdownPreEntity"/>.
    /// </summary>
    public bool UpdatesEnabled { get; private set; } = true;

    /// <summary>How many globals the table holds. Stock's <c>GetNumGlobals</c>.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// The map that is loaded, as <see cref="Add"/> stamps it on a global
    /// declared without one.
    ///
    /// <para>
    /// Stock's callers all pass <c>gpGlobals-&gt;mapname</c> by hand, and a
    /// managed entity has no equivalent to reach for. Remembering it here is
    /// where it belongs: the whole reason a global carries a map name is that
    /// this table outlives the map, so the table is the thing that knows which
    /// map it is currently outliving.
    /// </para>
    /// </summary>
    public string CurrentMap { get; private set; } = string.Empty;

    /// <summary>
    /// Stock's two platform globals, seeded on every level init.
    ///
    /// <para>
    /// <c>CWorld::Spawn</c> declared these — <c>world_adapter.cpp</c>'s
    /// <c>GlobalEntity_Add( "is_console", … )</c> and <c>"is_pc"</c> — and that
    /// pair was the LAST live caller of <c>game/server/globalstate.cpp</c>'s
    /// eleven forwarders. Seeding them here is what let that file leave the
    /// build; the forwarders were doing nothing but carrying these two names
    /// across the boundary to a table that is already on this side.
    /// </para>
    ///
    /// <para>
    /// <b>Not a behaviour change, and <see cref="Add"/> is why.</b> Add returns
    /// the EXISTING index for a name already in the table and changes nothing
    /// about it — not the state, not the level name, not the counter — so
    /// declaring them once per level init is exactly what stock did and has the
    /// same effect the second time as the first.
    /// </para>
    ///
    /// <para>
    /// <c>IsConsole()</c> is a compile-time constant in the SDK and is false for
    /// every platform this port builds for (Windows, Linux and macOS on x64,
    /// after Phase 0 of plan_shim.md). It is written as a constant here rather
    /// than asked of the engine because there is nothing to ask: the value stock
    /// read was decided by the preprocessor.
    /// </para>
    /// </summary>
    private const bool IsConsolePlatform = false;

    /// <summary>Stock's <c>LevelInitPreEntity</c>: the map name, then the two platform globals.</summary>
    public void OnLevelInitPreEntity(string mapName)
    {
        CurrentMap = mapName ?? string.Empty;

        // AFTER CurrentMap, because Add stamps a global declared with a null map
        // with whatever this property holds. Seeded with the map explicitly all
        // the same, so the two do not have to be read together to be understood.
        Add("is_console", CurrentMap, IsConsolePlatform ? GlobalState.On : GlobalState.Off);
        Add("is_pc", CurrentMap, IsConsolePlatform ? GlobalState.Off : GlobalState.On);
    }

    //-------------------------------------------------------------------------
    // THE WINDOW, AND WHY IT IS THE WHOLE REASON THIS CLASS IS AN IGameLifecycle
    //
    // CGlobalState's only two game-system stages were LevelShutdownPreEntity and
    // LevelShutdownPostEntity, and all they did was open and close this flag.
    // Stock says why at the site that needs it, in CBaseEntity::UpdateOnRemove
    // (baseentity.cpp:2542):
    //
    //     if ( m_iGlobalname != NULL_STRING )
    //     {
    //         // NOTE: During level shutdown the global list will suppress this
    //         // it assumes your changing levels or the game will end
    //         // causing the whole list to be flushed
    //         GlobalEntity_SetState( m_iGlobalname, GLOBAL_DEAD );
    //     }
    //
    // An entity with a globalname that is destroyed BECAUSE THE LEVEL IS GOING
    // AWAY is not dead; it is somewhere else. Without the window every such
    // entity marks its global GLOBAL_DEAD on the way out, and stock's own spawn
    // arm (util.cpp:1874) then DELETES it on the next map:
    //
    //     if ( GlobalEntity_GetState(globalIndex) == GLOBAL_DEAD )
    //     {  pEntity->Remove();  return -1;  }
    //
    // That is a cross-level failure. It is invisible on a map loaded once, and
    // it is invisible on a map whose entities have no globalname -- which is
    // every map this mod ships. It is pinned by test rather than by a load.
    //-------------------------------------------------------------------------

    /// <summary>
    /// How many times the window has been shut and reopened by a level
    /// shutdown, since the process started.
    ///
    /// <para>
    /// <b>Counted, and PRINTED by <c>dump_globals</c>, because "the participant
    /// is registered" is not "the participant is reached".</b> The whole reason
    /// this class exists is that <c>CGlobalState</c> was a row nobody added to
    /// <c>kServerSystems[]</c> — a bind that looked wired and delivered nothing,
    /// for six months, in silence. Two other lanes have since found the same
    /// shape (<c>UpdateAllClientData</c> walking a list that cannot answer, and
    /// a physics event ring whose only <c>SetSink</c> caller was a test), and
    /// the answer in all three cases is the same: make the walk say that it
    /// answered. After two map loads this reads 2 and 2.
    /// </para>
    /// </summary>
    public int WindowsShut { get; private set; }

    /// <inheritdoc cref="WindowsShut"/>
    public int WindowsReopened { get; private set; }

    /// <summary>
    /// <c>CGlobalState::LevelShutdownPreEntity</c>. The level's entities are
    /// still alive and are about to be destroyed; their destruction must not be
    /// read as their death.
    /// </summary>
    public void OnLevelShutdownPreEntity()
    {
        UpdatesEnabled = false;
        WindowsShut++;
    }

    /// <summary>
    /// <c>CGlobalState::LevelShutdownPostEntity</c>. The entities have gone;
    /// writes are meaningful again.
    /// </summary>
    public void OnLevelShutdownPostEntity()
    {
        UpdatesEnabled = true;
        WindowsReopened++;
    }

    /// <summary>
    /// Stock's <c>EnableStateUpdates</c>, which is the SAME flag reached by a
    /// second route: <c>CGlobalEntityList::FrameUpdatePostEntityThink</c> brackets
    /// its <c>respawn_entities</c> sweep with it, for this flag's reason —
    /// "Don't change globalstate owing to deletion here".
    ///
    /// <para>
    /// <b>It is a plain assignment and not a counter, deliberately.</b> Stock's
    /// is (<c>m_disableStateUpdates = !bEnable</c>), and the two users cannot
    /// nest: one is a level teardown and the other is a frame-end sweep on a
    /// live level.
    /// </para>
    /// </summary>
    public void EnableUpdates(bool enable) => UpdatesEnabled = enable;

    /// <inheritdoc/>
    public int Add(string? name, string? mapName, GlobalState state)
    {
        if (string.IsNullOrEmpty(name))
            return -1;

        // STOCK'S ORDER, AND IT MATTERS. AddEntity builds the record, then asks
        // GetIndex, and returns the EXISTING index if there is one -- so a
        // second Add of a name already in the table changes NOTHING: not the
        // state, not the level name, not the counter. That is what makes a
        // global survive a map that re-declares it, and it is why
        // world_adapter.cpp can call Add("is_console", ...) on every level init
        // without resetting anything.
        if (_byName.TryGetValue(name, out int existing))
            return existing;

        _entries.Add(new Entry
        {
            Name = name,
            // A null map means "the one that is loaded" -- what stock's callers
            // spell as gpGlobals->mapname and a managed entity cannot. An EMPTY
            // string is not the same request and is stored as given.
            LevelName = mapName ?? CurrentMap,
            State = state,
            Counter = 0,
        });

        int index = _entries.Count - 1;
        _byName[name] = index;
        return index;
    }

    /// <inheritdoc/>
    public int IndexOf(string? name)
        => name is not null && _byName.TryGetValue(name, out int i) ? i : -1;

    /// <summary>True when <paramref name="index"/> names an entry.</summary>
    private bool Valid(int index) => index >= 0 && index < _entries.Count;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>GLOBAL_OFF</c> for an index that names nothing, which is stock's
    /// answer and is load-bearing: every <c>GlobalEntity_GetState( "name" )</c>
    /// overload resolves the name FIRST and passes -1 through when it is not in
    /// the table, so "no such global" and "that global is off" are deliberately
    /// the same answer to a caller.
    /// </remarks>
    public GlobalState GetState(int index) => Valid(index) ? _entries[index].State : GlobalState.Off;

    /// <inheritdoc/>
    public void SetState(int index, GlobalState state)
    {
        if (!UpdatesEnabled || !Valid(index))
            return;
        _entries[index].State = state;
    }

    /// <inheritdoc/>
    public int GetCounter(int index) => Valid(index) ? _entries[index].Counter : 0;

    /// <inheritdoc/>
    public void SetCounter(int index, int counter)
    {
        if (!UpdatesEnabled || !Valid(index))
            return;
        _entries[index].Counter = counter;
    }

    /// <inheritdoc/>
    public int AddToCounter(int index, int delta)
    {
        if (!UpdatesEnabled || !Valid(index))
            return 0;
        return _entries[index].Counter += delta;
    }

    /// <inheritdoc/>
    public string? GetName(int index) => Valid(index) ? _entries[index].Name : null;

    /// <inheritdoc/>
    public string? GetMap(int index) => Valid(index) ? _entries[index].LevelName : null;

    /// <inheritdoc/>
    /// <remarks>
    /// <b>NOT gated on <see cref="UpdatesEnabled"/>, and stock's is not
    /// either.</b> <c>SetMap</c> is how a global is handed from one level to the
    /// next, which is the one write that has to survive the window the other
    /// setters are suppressed by.
    /// </remarks>
    public void SetMap(int index, string? mapName)
    {
        if (!Valid(index))
            return;
        _entries[index].LevelName = mapName ?? string.Empty;
    }

    /// <summary>
    /// Stock's <c>CGlobalState::DumpGlobals</c>, printed in stock's format so a
    /// reader comparing a managed session against an SDK one is comparing the
    /// same lines.
    /// </summary>
    public void Dump()
    {
        log.Msg("-- Globals --\n");
        foreach (Entry e in _entries)
            log.Msg($"{e.Name}: {e.LevelName} ({StateName(e.State)}) = {e.Counter}\n");

        // THE PORT'S OWN LINE, after stock's block rather than inside it, so a
        // reader comparing this dump against an SDK one is still comparing the
        // same lines above it. It is the only thing that can say the write
        // window is actually being driven -- see WindowsShut.
        log.Msg($"[SourceSharp] window: updates {(UpdatesEnabled ? "on" : "SHUT")}, "
                + $"shut {WindowsShut}x, reopened {WindowsReopened}x, map '{CurrentMap}'\n");
    }

    /// <summary>
    /// Stock's <c>estates[]</c> — <c>{ "Off", "On", "Dead" }</c>, indexed by the
    /// enum. Written as a switch so an unknown value cannot index past the end
    /// of an array, which stock's could.
    /// </summary>
    internal static string StateName(GlobalState state) => state switch
    {
        GlobalState.Off => "Off",
        GlobalState.On => "On",
        GlobalState.Dead => "Dead",
        _ => "?",
    };
}
