using System;

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IEngineGameEvents"/> on the server, where an event is created,
/// fired and freed through <c>IGameEventManager2</c>'s forwarders
/// (<see cref="GameEventManagerFwd"/>, plan_server_native_v2 T3).
///
/// <para>
/// Those three were IShimEventsServer slots whose native bodies made one
/// engine call each. The forwarder makes the same call, with the create's
/// <c>bForce</c> written out as the false stock's callers pass it; the fire's
/// <c>bDontBroadcast</c> is the caller's argument, which the seam carries,
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
    /// A new event of the declared kind <paramref name="name"/>, or zero. Fire
    /// it (<see cref="EventFire"/>, which frees it) or free it.
    ///
    /// <para>
    /// The manager answers null to an unforced create for two reasons: no
    /// such event is declared, or it is declared and nothing listens for it
    /// (stock's callers create unforced, and fire nothing then). Only the
    /// first is a fault, so a null is asked again forced: an event that comes
    /// back is declared, and is freed unfired, quietly; one that does not is
    /// warned of.
    /// </para>
    /// </summary>
    public IntPtr EventCreate(string name)
    {
        if (string.IsNullOrEmpty(name))
            return IntPtr.Zero;

        void* created;
        void* forced = null;
        fixed (byte* p = Vtable.Utf8(name))
        {
            created = _fwd->CreateEvent(_self, p, 0);
            if (created == null)
                forced = _fwd->CreateEvent(_self, p, 1);
        }

        if (forced != null)
            _fwd->FreeEvent(_self, forced);
        else if (created == null)
            _log.Warning($"[SourceSharp] could not create game event '{name}': it is not declared in any loaded event file\n");

        return (IntPtr)created;
    }

    /// <summary>
    /// Fires an event made by <see cref="EventCreate"/>; the engine frees it.
    /// The broadcast flag is the caller's, as in stock's
    /// <c>FireEvent(pEvent, bDontBroadcast)</c>: the ones that carry it
    /// (payload_pushed, in team_train_watcher.cpp:1275) are server-only.
    /// </summary>
    public bool EventFire(IntPtr handle, bool dontBroadcast = false)
        => handle != IntPtr.Zero && _fwd->FireEvent(_self, (void*)handle, (byte)(dontBroadcast ? 1 : 0)) != 0;

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
