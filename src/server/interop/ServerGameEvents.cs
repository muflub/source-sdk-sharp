using System;

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IEngineGameEvents"/> on the server, where an event is created,
/// fired and freed through <c>IGameEventManager2</c>'s forwarders
/// (<see cref="GameEventManagerFwd"/>, plan_server_native_v2 T3).
///
/// <para>
/// Those three were IShimEventsServer slots whose native bodies made one
/// engine call each. The forwarder makes the same call, with stock's default
/// arguments written out (<c>bForce</c> false, <c>bDontBroadcast</c> false),
/// and the warning for an undeclared event is this class's. Every other
/// member is the inner area's, unchanged: reading an event, setting its
/// fields and listening are still slots.
/// </para>
/// </summary>
public sealed unsafe class ServerGameEvents : IEngineGameEvents
{
    /// <summary>The engine's factory name for the manager.</summary>
    public const string InterfaceVersion = "GAMEEVENTSMANAGER002";

    private readonly IEngineGameEvents _inner;
    private readonly ILog _log;
    private readonly void* _self;
    private readonly GameEventManagerFwd* _fwd;

    private ServerGameEvents(IEngineGameEvents inner, ILog log, void* self, GameEventManagerFwd* fwd)
    {
        _inner = inner;
        _log = log;
        _self = self;
        _fwd = fwd;
    }

    /// <summary>
    /// The decorator over <paramref name="inner"/>, through
    /// <paramref name="engine"/>'s own side's factory, or null with the reason.
    /// </summary>
    public static IEngineGameEvents? Acquire(ModuleEngine engine, IEngineGameEvents inner, ILog log, out string why)
        => Forwarders.TryAcquire<GameEventManagerFwd>(
               (engine ?? throw new ArgumentNullException(nameof(engine))).Interfaces,
               SourceSharpFactoryType.AppSystem, InterfaceVersion,
               out void* self, out GameEventManagerFwd* table, out why)
            ? new ServerGameEvents(inner ?? throw new ArgumentNullException(nameof(inner)),
                                   log ?? throw new ArgumentNullException(nameof(log)), self, table)
            : null;

    /// <summary>
    /// A new event of the declared kind <paramref name="name"/>, or zero (with
    /// a warning) when no such event is declared. Fire it (<see cref="EventFire"/>,
    /// which frees it) or free it.
    /// </summary>
    public IntPtr EventCreate(string name)
    {
        if (string.IsNullOrEmpty(name))
            return IntPtr.Zero;

        void* created;
        fixed (byte* p = Vtable.Utf8(name))
            created = _fwd->CreateEvent(_self, p, 0);

        if (created == null)
            _log.Warning($"[SourceSharp] could not create game event '{name}': no such event is declared\n");

        return (IntPtr)created;
    }

    /// <summary>Fires an event made by <see cref="EventCreate"/>; the engine frees it.</summary>
    public bool EventFire(IntPtr handle)
        => handle != IntPtr.Zero && _fwd->FireEvent(_self, (void*)handle, 0) != 0;

    /// <inheritdoc/>
    public bool EventGetBool(IntPtr handle, string key) => _inner.EventGetBool(handle, key);

    /// <inheritdoc/>
    public float EventGetFloat(IntPtr handle, string key) => _inner.EventGetFloat(handle, key);

    /// <inheritdoc/>
    public int EventGetInt(IntPtr handle, string key) => _inner.EventGetInt(handle, key);

    /// <inheritdoc/>
    public string EventGetString(IntPtr handle, string key) => _inner.EventGetString(handle, key);

    /// <inheritdoc/>
    public void ListenForGameEvent(string name) => _inner.ListenForGameEvent(name);

    /// <inheritdoc/>
    public void EventSetInt(IntPtr handle, string key, int value) => _inner.EventSetInt(handle, key, value);

    /// <inheritdoc/>
    public void EventSetBool(IntPtr handle, string key, bool value) => _inner.EventSetBool(handle, key, value);

    /// <inheritdoc/>
    public void EventSetString(IntPtr handle, string key, string value) => _inner.EventSetString(handle, key, value);
}
