namespace SourceSharp.Abi;

/// <summary>
/// What a named global can be set to — stock's <c>GLOBALESTATE</c>
/// (<c>game/server/globalstate.h:14</c>), value for value.
///
/// <para>
/// <b>The numbers are the wire.</b> <c>env_global</c> carries the initial state
/// as an integer map key and stock's <c>CEnvGlobal</c> casts it straight to this
/// enum, so a map authored for the SDK sets these numbers by number. They may
/// not be renumbered.
/// </para>
/// </summary>
public enum GlobalState
{
    /// <summary>Stock's <c>GLOBAL_OFF</c>. Also the answer for a global that is not in the table at all.</summary>
    Off = 0,

    /// <summary>Stock's <c>GLOBAL_ON</c>.</summary>
    On = 1,

    /// <summary>
    /// Stock's <c>GLOBAL_DEAD</c>: the entity this global names has been
    /// destroyed for good, and must not be spawned again on any later map.
    /// </summary>
    Dead = 2,
}

/// <summary>
/// The cross-level global state table, as its callers see it — the managed half
/// of <c>game/server/globalstate.h</c>.
///
/// <para>
/// <b>An index, not a name, is the identity.</b> Every entry point below takes
/// the integer <see cref="IndexOf"/> or <see cref="Add"/> handed out, and that
/// integer stays valid for the life of the process. That is stock's contract —
/// its <c>CUtlVector</c> never removed an element — and native callers that
/// still hold one across a level change depend on it.
/// </para>
/// </summary>
public interface IServerGlobalState
{
    /// <summary>How many globals the table holds. Stock's <c>GlobalEntity_GetNumGlobals</c>.</summary>
    int Count { get; }

    /// <summary>
    /// Whether writes are landing. False only for the duration of a level
    /// teardown; see <c>GlobalStateTable.OnLevelShutdownPreEntity</c>.
    /// </summary>
    bool UpdatesEnabled { get; }

    /// <summary>
    /// Declares a global if it is not already declared, and answers its index.
    /// Stock's <c>GlobalEntity_Add</c>.
    ///
    /// <para>
    /// <b>An existing name is left completely alone</b> and its index returned.
    /// Nothing about the entry is overwritten — not the state, not the map, not
    /// the counter — which is stock's behaviour and is what lets a level init
    /// re-declare the globals it cares about without erasing what a previous
    /// level did to them.
    /// </para>
    /// </summary>
    /// <param name="mapName">
    /// The map to stamp on a NEW global. <b>Null means "the map that is
    /// loaded"</b> — <see cref="CurrentMap"/> — which is what stock's callers
    /// spell as <c>gpGlobals-&gt;mapname</c> and a managed entity has no way to
    /// reach. An empty string is a different request and is stored as given.
    /// </param>
    /// <returns>The index, or -1 for an empty name.</returns>
    int Add(string? name, string? mapName, GlobalState state);

    /// <summary>
    /// The map that is loaded, as <see cref="Add"/> stamps it on a global
    /// declared with a null map name.
    /// </summary>
    string CurrentMap { get; }

    /// <summary>The index of a declared global, or -1. Stock's <c>GlobalEntity_GetIndex</c>.</summary>
    int IndexOf(string? name);

    /// <summary>
    /// The state of a global by index, or <see cref="GlobalState.Off"/> for an
    /// index that names nothing.
    /// </summary>
    GlobalState GetState(int index);

    /// <summary>Sets a global's state. Ignored while <see cref="UpdatesEnabled"/> is false.</summary>
    void SetState(int index, GlobalState state);

    /// <summary>A global's counter, or 0 for an index that names nothing.</summary>
    int GetCounter(int index);

    /// <summary>Sets a global's counter. Ignored while <see cref="UpdatesEnabled"/> is false.</summary>
    void SetCounter(int index, int counter);

    /// <summary>
    /// Adds to a global's counter and answers the new value, or 0 when the
    /// write was refused.
    /// </summary>
    int AddToCounter(int index, int delta);

    /// <summary>A global's name, or null for an index that names nothing.</summary>
    string? GetName(int index);

    /// <summary>
    /// The map that last owned this global, or null for an index that names
    /// nothing. Stock's <c>GlobalEntity_GetMap</c>, read by the spawn arm to
    /// decide whether an entity with a <c>globalname</c> belongs to this level
    /// yet.
    /// </summary>
    string? GetMap(int index);

    /// <summary>
    /// Hands a global to a map. <b>Not gated on <see cref="UpdatesEnabled"/></b> —
    /// stock's is not either, because this is the one write a level transition
    /// has to be able to make while the window is shut.
    /// </summary>
    void SetMap(int index, string? mapName);

    /// <summary>
    /// Prints the table. Stock's <c>CGlobalState::DumpGlobals</c>, behind the
    /// <c>dump_globals</c> console command.
    /// </summary>
    void Dump();

    /// <summary>
    /// Opens or shuts the write window. Stock's
    /// <c>GlobalEntity_EnableStateUpdates</c>.
    /// </summary>
    void EnableUpdates(bool enable);
}

/// <summary>
/// The order a map's standalone entities spawn in — plan_entity.md Phase 4
/// exit, lane p4-lifecycle.
///
/// <para>
/// <b>What crosses is the ORDER, not the decision, and not one call per
/// entity.</b> The map loader hands over the classnames it has queued and gets
/// back the sequence to spawn them in: one crossing per map load whatever the
/// entity count, which is the same shape and the same argument as
/// <c>PlayerCommands</c>' flatten in <c>sourcesharp.h</c>.
/// </para>
/// </summary>
public interface IServerMapSpawnOrder
{
    /// <summary>
    /// Writes into <paramref name="order"/> the indices of
    /// <paramref name="classNames"/>, in the order those entities should spawn.
    ///
    /// <para>
    /// Every index appears exactly once: this is a permutation, not a filter.
    /// Nothing here may drop an entity — a map entity that does not spawn is
    /// the failure this whole lane exists to not cause.
    /// </para>
    /// </summary>
    /// <returns>
    /// How many indices were written, or the count NEEDED when
    /// <paramref name="order"/> was too small and nothing was written — the
    /// size-then-fill contract this ABI already uses.
    /// </returns>
    int Order(IReadOnlyList<string?> classNames, Span<int> order);
}
