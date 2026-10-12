using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Turns a game event into a map output.
///
/// Game events are the engine's own broadcast channel — a death, a join, a team
/// change — and this is the bridge from that channel into map logic, so a map can
/// open a door when somebody dies without anything in the map knowing how deaths
/// are reported. One entity per event, named by the EventName key.
///
/// The port is a subscription rather than a translation, because C# already owns
/// the events: SourceSharp.Shared/GameEvents.cs decodes each one and raises a
/// typed C# event, and this listens to those. That is also its limit — it can
/// listen for the events this mod has declared, and nothing else. See
/// <see cref="GameEventFeed"/>.
///
/// Note the default: a listener with no IsEnabled key never fires. That is
/// stock's behaviour rather than an oversight — the keyfield starts zeroed — and
/// it means a map placing one must either set the key or send Enable.
/// </summary>
[ScriptedEntity(EntityNames.LogicEventlistener)]
public class LogicEventListener : ScriptedEntity
{
    public LogicEventListener(IEngineGameEvents events, IGameEventFeed feed)
    {
        Events = events;
        _feed = feed;
    }

    /// <summary>The module's feed, which delivers the events to live listeners.</summary>
    private readonly IGameEventFeed _feed;

    /// <summary>the engine's own event bus.</summary>
    protected readonly IEngineGameEvents Events;
    /// <summary>What a map may set on this entity. See <see cref="LogicEventListenerVars"/>.</summary>
    [ScriptVars] public LogicEventListenerVars Vars = new();

    public readonly Output OnEventFired = new();

    /// <summary>
    /// The event this listens for. A property rather than the field directly, so
    /// a subclass listening for one fixed event can say so.
    /// </summary>
    protected virtual string Listens => Vars.EventName;

    /// <summary>Whether this is currently reporting. For the tests.</summary>
    public bool IsEnabled => Vars.IsEnabled;

    public override void OnSpawn() => _feed.Add(this);

    // Every map entity is removed at level change, so this is also how the feed
    // is emptied between maps — there is no other moment at which a list of
    // live entities would be told the world had been replaced.
    public override void OnRemoved() => _feed.Remove(this);

    [ScriptedEvent] private void OnEnable() => Vars.IsEnabled = true;
    [ScriptedEvent] private void OnDisable() => Vars.IsEnabled = false;

    /// <summary>
    /// An event happened. Report it if it is the one being listened for and it
    /// passes the team filter.
    ///
    /// <paramref name="team"/> is the team of the player the event was about, and
    /// is <see cref="GameEventFeed.NoTeam"/> when that is not known — which fails
    /// a filter naming a team, deliberately: firing for a player whose side has
    /// not been learned would report the wrong side.
    /// </summary>
    internal void Deliver(string name, int userId, int team)
    {
        if (!Vars.IsEnabled)
            return;

        if (!string.Equals(name, Listens, StringComparison.OrdinalIgnoreCase))
            return;

        if (Vars.Team > 0 && team != Vars.Team)
            return;

        OnEventFired.Fire();
    }
}

/// <summary>
/// The live logic_eventlistener entities, and the events that reach them: one
/// feed per server module, taken by each listener through its constructor.
/// </summary>
public interface IGameEventFeed
{
    /// <summary>A listener has spawned. Attaches to the events on the first.</summary>
    void Add(LogicEventListener listener);

    /// <summary>A listener has been removed.</summary>
    void Remove(LogicEventListener listener);

    /// <summary>How many listeners are live.</summary>
    int ListenerCount { get; }

    /// <summary>The team a userid was last seen joining, or <see cref="GameEventFeed.NoTeam"/>.</summary>
    int TeamOf(int userId);

    /// <summary>Hands an event to every live listener.</summary>
    void Deliver(string name, int userId);
}

/// <summary>
/// The one place the live listeners are kept, and the one place the events reach
/// them.
///
/// It exists because a listener is per-map-entity while a subscription is
/// per-module: subscribing each instance separately would attach and detach
/// handlers on the module's events as maps load, and a handler left behind
/// there is a listener that fires for a map that is no longer running. So the
/// handlers are attached once, and what changes is this list.
///
/// <para>
/// A SERVICE, registered in the server module's container. It was a static
/// class holding the list, the team table and a "subscribed" flag for the
/// process, which is why the tests that spawn listeners had to run serially.
/// The team table lives as long as the feed — the module — exactly as it lived
/// as long as the process before: a player keeps their side across a map
/// change.
/// </para>
///
/// **What it can hear is the mod's declared vocabulary, and no more.** The five
/// events below are the ones SourceSharp.Shared/GameEvents.cs declares; a map
/// naming anything else gets a listener that never fires. Subscribing to an
/// arbitrary name at runtime is possible on the native ABI — Events.ListenForGameEvent
/// takes a string — but the dispatch that comes back is routed by the module's
/// GameEventRegistry, which is private to it, so an entity cannot register a name
/// with it from here. Adding an event to GameEvents.cs is the cheap way to widen
/// this, and it widens it for everything else too.
/// </summary>
public sealed class GameEventFeed : IGameEventFeed
{
    /// <summary>The team of a player nobody has heard of.</summary>
    public const int NoTeam = -1;

    private readonly IGameEvent<PlayerConnectArgs> _connects;
    private readonly IGameEvent<PlayerDeathArgs> _deaths;
    private readonly IGameEvent<PlayerActivateArgs> _activates;
    private readonly IGameEvent<PlayerTeamArgs> _teams;
    private readonly IGameEvent<PlayerDisconnectArgs> _disconnects;

    /// <summary>The module's five player events, which this attaches to on first use.</summary>
    public GameEventFeed(IGameEvent<PlayerConnectArgs> connects,
                         IGameEvent<PlayerDeathArgs> deaths,
                         IGameEvent<PlayerActivateArgs> activates,
                         IGameEvent<PlayerTeamArgs> teams,
                         IGameEvent<PlayerDisconnectArgs> disconnects)
    {
        _connects = connects;
        _deaths = deaths;
        _activates = activates;
        _teams = teams;
        _disconnects = disconnects;
    }

    private readonly List<LogicEventListener> _listeners = new();

    // Team by userid. Every event but player_team carries only a userid, and
    // there is no way to ask the engine for a player by userid — the ABI indexes
    // players by slot, which is a different number — so the side a player is on
    // is remembered from the one event that says. player_team fires on the
    // initial assignment as well as on changes, so a player is in here from the
    // moment they pick one.
    private readonly Dictionary<int, int> _teamOf = new();

    private bool _subscribed;

    /// <inheritdoc/>
    public void Add(LogicEventListener listener)
    {
        Subscribe();
        _listeners.Add(listener);
    }

    /// <inheritdoc/>
    public void Remove(LogicEventListener listener) => _listeners.Remove(listener);

    /// <summary>How many listeners are live. For the tests.</summary>
    public int ListenerCount => _listeners.Count;

    /// <inheritdoc/>
    public int TeamOf(int userId) => _teamOf.TryGetValue(userId, out int team) ? team : NoTeam;

    /// <summary>
    /// Attaches to the events, once, when the first listener spawns — the
    /// moment the static version attached, so the team table starts recording
    /// at the same point it always did.
    /// </summary>
    private void Subscribe()
    {
        if (_subscribed)
            return;

        _subscribed = true;

        _connects.Fired += e => Deliver("player_connect", e.UserId);
        _deaths.Fired += e => Deliver("player_death", e.UserId);
        _activates.Fired += e => Deliver("player_activate", e.UserId);

        // Remembered before the event is reported, so a listener filtering on the
        // team somebody just joined matches the change that put them there.
        _teams.Fired += e =>
        {
            _teamOf[e.UserId] = e.Team;
            Deliver("player_team", e.UserId);
        };

        // And forgotten after, for the mirror-image reason: a listener filtering
        // by team still wants to hear that one of its team left.
        _disconnects.Fired += e =>
        {
            Deliver("player_disconnect", e.UserId);
            _teamOf.Remove(e.UserId);
        };
    }

    /// <summary>
    /// Hands an event to every live listener.
    ///
    /// Over a copy of the list, because an output can remove entities — including
    /// a listener, including this one — and a map that kills a listener from its
    /// own output should get a fired output rather than an exception.
    /// </summary>
    public void Deliver(string name, int userId)
    {
        int team = TeamOf(userId);

        foreach (var listener in _listeners.ToArray())
            listener.Deliver(name, userId, team);
    }

    /// <summary>Records a team without an event. For the tests.</summary>
    internal void Remember(int userId, int team) => _teamOf[userId] = team;
}

/// <summary>
/// The map keys <see cref="LogicEventListener"/> declares.
/// </summary>
[ScriptVars]
public partial class LogicEventListenerVars
{
    /// <summary>Which event, by the name the engine broadcasts it under.</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>Off until the map says otherwise. See the class comment.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Only report the event when it was about a player on this team. Zero and
    /// below mean "anybody", which is what a map that does not set the key gets.
    /// </summary>
    [VarName("TeamNum")]
    public int Team { get; set; }

    /// <summary>
    /// Stock copies the event's fields into the entity's VScript scope so a
    /// script can read them. This mod's scripting is C# and there is no scope to
    /// copy them into, so the key is carried and does nothing — the same answer
    /// logic_script gives.
    /// </summary>
    public bool FetchEventData { get; set; }
}
