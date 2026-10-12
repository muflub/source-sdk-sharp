using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a handler for a named game event.
///
/// Unlike user messages, the declaration ORDER carries no meaning: the engine
/// dispatches game events by name, so this is a subscription rather than a
/// protocol. Adding, removing or reordering handlers cannot desynchronise
/// anything.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GameEventAttribute : Attribute
{
    public GameEventAttribute(string name)
    {
        Name = name;
    }

    /// <summary>Event name as the engine knows it, e.g. "player_death".</summary>
    public string Name { get; }
}

/// <summary>
/// Reads fields off the game event currently being dispatched.
///
/// Wraps the native IGameEvent, which is valid only for the duration of the
/// callback — so this is a ref struct: the compiler will refuse to let it be
/// stored on a field or captured, which is exactly the mistake that would turn
/// into a use-after-free.
/// </summary>
public readonly ref struct GameEventReader
{
    private readonly IEngineGameEvents _events;
    private readonly IntPtr _handle;

    /// <summary>
    /// Over the dispatching side's own event area (plan_abi_hotreload D8): the
    /// registry hands over the one its composition gave it, so a reader never
    /// asks whichever side happens to be active.
    /// </summary>
    internal GameEventReader(IEngineGameEvents events, IntPtr handle)
    {
        _events = events;
        _handle = handle;
    }

    public int    GetInt(string key)    => _events.EventGetInt(_handle, key);
    public float  GetFloat(string key)  => _events.EventGetFloat(_handle, key);
    public bool   GetBool(string key)   => _events.EventGetBool(_handle, key);
    public string GetString(string key) => _events.EventGetString(_handle, key);
}

/// <summary>
/// The entry point the registry calls on a declared event class.
///
/// A plain named delegate rather than an Action&lt;T&gt;, because
/// <see cref="GameEventReader"/> is a ref struct and cannot be a generic type
/// argument. That restriction is the point: it is what stops the reader from
/// being captured or stored past the callback.
/// </summary>
public delegate void GameEventDispatch(GameEventReader reader);

/// <summary>
/// One declared game event, as its subscribers see it: a typed C# event raised
/// once per engine event, carrying the decoded data.
///
/// <para>
/// The contract a subscriber takes through its constructor. The declared event
/// classes are INSTANCES registered in each side's container (see
/// <see cref="GameEventRegistry.AddEventsTo"/>) rather than static classes with
/// static events, so a subscription is to one module's event and nothing else's:
/// two containers never share a subscriber list, and a test raising an event
/// reaches only what it built.
/// </para>
/// </summary>
public interface IGameEvent<TArgs>
{
    /// <summary>Raised once per engine event, after decoding.</summary>
    event Action<TArgs>? Fired;

    /// <summary>
    /// Raises <see cref="Fired"/> with already-decoded data. What the event's
    /// own Dispatch does once it has read the engine's event; public so a
    /// caller holding typed data (a test, a replay) does not need an engine.
    /// </summary>
    void Raise(TArgs args);
}

/// <summary>
/// Discovers <see cref="GameEventAttribute"/> classes, subscribes to each named
/// event, and dispatches by name.
///
/// The classes are INSTANCES held by the side's service container: each is a
/// sealed class with a public parameterless-or-injectable constructor and a
/// public instance <c>Dispatch(GameEventReader)</c>, created and registered by
/// <see cref="AddEventsTo"/> (under its own type and every
/// <see cref="IGameEvent{TArgs}"/> it implements) and subscribed by
/// <see cref="RegisterAll(Assembly, IServiceLocator)"/>. Each class decodes the
/// raw event once and raises its own typed event, so subscribers work with real
/// data rather than a borrowed reader they are not allowed to keep. They used
/// to be static classes with static events, which made every subscriber
/// process-wide state.
///
/// More than one class may claim the same event; all of them run. That is a
/// deliberate difference from ConCommands, where a duplicate name is a
/// programming error — here it is a reasonable way to keep unrelated concerns
/// apart.
/// </summary>
public sealed class GameEventRegistry
{
    /// <summary>The method every declared event class must expose.</summary>
    public const string DispatchMethodName = "Dispatch";

    private readonly Dictionary<string, List<GameEventDispatch>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The side's event area, which subscribes natively and which every reader
    /// this registry dispatches reads through (plan_abi_hotreload D8: it was the
    /// static <c>Engine</c>, which answered for whichever side was active).
    /// </summary>
    private readonly IEngineGameEvents _events;

    /// <param name="log">Where a declaration problem or an undispatchable event is said (statics to zero).</param>
    public GameEventRegistry(IEngineGameEvents events, ILog log)
    {
        ArgumentNullException.ThrowIfNull(events);
        _events = events;
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private readonly ILog _log;

    /// <summary>Distinct event names subscribed to.</summary>
    public int Count => _byName.Count;

    /// <summary>Event names subscribed to.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>
    /// Creates every attributed event class in every assembly loaded beside
    /// <paramref name="assembly"/> and registers it in
    /// <paramref name="services"/>, under its own type and under every
    /// <see cref="IGameEvent{TArgs}"/> it implements. Returns the number added.
    ///
    /// <para>
    /// Separate from <see cref="RegisterAll(Assembly, IServiceLocator)"/>, and
    /// run early in a module's init, because the SUBSCRIBERS are built early —
    /// the scoreboard and the rules take their events through their
    /// constructors — while the native subscription belongs where it always
    /// was. Makes no engine call. A static class, or one that cannot be built,
    /// is skipped here and reported by <c>RegisterAll</c>.
    /// </para>
    /// </summary>
    public static int AddEventsTo(ServiceContainer services, Assembly assembly, ILog log)
    {
        ArgumentNullException.ThrowIfNull(services);
        int added = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, log))
        {
            if (type.GetCustomAttribute<GameEventAttribute>() is null)
                continue;

            if (type.IsAbstract || services.IsRegistered(type))
                continue;

            if (!services.TryCreate(type, out object? instance, out _) || instance is null)
                continue;

            services.AddInstance(type, instance);

            foreach (var contract in type.GetInterfaces())
            {
                if (contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IGameEvent<>)
                    && !services.IsRegistered(contract))
                    services.AddInstance(contract, instance);
            }

            added++;
        }

        return added;
    }

    /// <summary>
    /// Subscribes, for every attributed event class in every assembly loaded
    /// beside <paramref name="assembly"/>, the instance
    /// <paramref name="services"/> holds for it to the event it names. Returns
    /// the number registered.
    /// </summary>
    public int RegisterAll(Assembly assembly, IServiceLocator services)
    {
        ArgumentNullException.ThrowIfNull(services);
        int registered = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, _log))
        {
            var attr = type.GetCustomAttribute<GameEventAttribute>();
            if (attr is null)
                continue;

            // A static class is abstract+sealed in metadata. It is refused: its
            // event would be one subscriber list for the whole process.
            if (type.IsAbstract)
            {
                _log.Warning($"[SourceSharp] [GameEvent({attr.Name})] on {type.Name}, " +
                               $"which is not an instance class; skipped\n");
                continue;
            }

            var method = type.GetMethod(DispatchMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null, new[] { typeof(GameEventReader) }, null);

            if (method is null)
            {
                _log.Warning($"[SourceSharp] [GameEvent({attr.Name})] on {type.Name}, which has no " +
                               $"public void {DispatchMethodName}(GameEventReader); skipped\n");
                continue;
            }

            var instance = services.Resolve(type);

            if (instance is null)
            {
                _log.Warning($"[SourceSharp] [GameEvent({attr.Name})] on {type.Name}, which the " +
                               $"container does not hold (AddEventsTo could not build it); skipped\n");
                continue;
            }

            // Skipped rather than fatal: unlike a user message, a missing event
            // handler costs only its own behaviour — nothing downstream shifts.
            Subscribe(attr.Name,
                      (GameEventDispatch)method.CreateDelegate(typeof(GameEventDispatch), instance));
            registered++;
        }

        _log.Msg($"[SourceSharp] registered {registered} game event class(es) " +
                   $"across {_byName.Count} event(s)\n");
        return registered;
    }

    /// <summary>
    /// Adds a dispatcher for an event, subscribing natively the first time that
    /// event is claimed. Subscribing once per event rather than once per claim
    /// keeps the native listener from being added twice and delivering doubles.
    /// </summary>
    public void Subscribe(string eventName, GameEventDispatch dispatch)
    {
        if (!_byName.TryGetValue(eventName, out var list))
        {
            list = new List<GameEventDispatch>();
            _byName[eventName] = list;
            _events.ListenForGameEvent(eventName);
        }

        list.Add(dispatch);
    }

    /// <summary>
    /// Dispatches a fired event to every class that claimed it.
    ///
    /// One throwing does not stop the others: they are independent subscribers,
    /// and a fault in one is no reason to silently drop the rest.
    /// </summary>
    private readonly Dictionary<string, long> _received = new(StringComparer.Ordinal);

    /// <summary>Every claimed event dispatched at least once, with its count: what the dev API reports.</summary>
    public IReadOnlyList<(string Name, long Count)> ReceivedCounts()
        => _received.Select(p => (p.Key, p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal).ToArray();

    public void Dispatch(string eventName, IntPtr handle)
    {
        if (!_byName.TryGetValue(eventName, out var list))
            return;

        _received[eventName] = _received.GetValueOrDefault(eventName) + 1;

        foreach (var dispatch in list)
        {
            try
            {
                dispatch(new GameEventReader(_events, handle));
            }
            catch (Exception e)
            {
                _log.Warning($"[SourceSharp] game event dispatch for '{eventName}' " +
                               $"threw: {e.Message}\n");
            }
        }
    }
}
