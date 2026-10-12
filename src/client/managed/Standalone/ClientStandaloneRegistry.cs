using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using SourceSharp.Abi;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// Pairs a client shell with its managed object, by classname.
///
/// Deliberately the same shape as the server's <c>StandaloneRegistry</c>. The
/// one difference is WHEN it is asked: the server creates the managed half at
/// spawn, because that is when the entity exists; the client creates it on the
/// first data update, because until then the shell has none of the server's
/// state and does not yet know what it is.
/// </summary>
public sealed class ClientStandaloneRegistry : IClientStandalones
{
    private readonly Dictionary<string, Type> _byClassName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// <b>WHERE THE LIVE ENTITIES ACTUALLY ARE</b> — <c>plan_entity.md</c> §3.5,
    /// lane <c>entlist-client</c>.
    ///
    /// <para>
    /// This registry used to BE the client's entity table: a
    /// <c>Dictionary&lt;uint, ClientStandaloneEntity&gt;</c> of live entities
    /// and a second <c>Dictionary&lt;int, …&gt;</c> by engine index, with no
    /// find-by-class, no find-by-name and no notification. §3.5 puts all of
    /// that in the DIRECTORY, one per side module, and leaves the registry with
    /// the one job its name describes: turning a classname into an instance.
    /// </para>
    ///
    /// <para>
    /// So the storage moved and this class kept its interface. Every lookup
    /// below now forwards, and what a caller gets back is unchanged except in
    /// two respects, both improvements: an index erase is one store rather than
    /// a walk of every live entity, and the entry a lookup answers from is
    /// re-validated against the HANDLE, so a recycled index cannot answer the
    /// entity that used to hold it.
    /// </para>
    /// </summary>
    public ClientEntityDirectory Directory { get; } = new();

    // Worked out once per entity class, for the same reason the server's
    // registry keeps one: this side creates an instance per networked entity as
    // it comes into the PVS, and reflecting over a constructor once per
    // instance is the expensive half of making one.
    private readonly Dictionary<Type, ServiceActivator> _activators = new();


    // THE ENGINE THIS REGISTRY BINDS ITS ENTITIES TO (plan_abi_hotreload D8):
    // the container of the composition that built it, holding that side's
    // engine areas. It was the active side's process-wide defaults; like the
    // server's registry, it is now handed in, so two registries over two
    // compositions share no engine. A container in Services answers first and
    // this behind it, as on the server: a test registering one service must
    // not thereby lose the engine.
    private readonly IServiceLocator _engine;

    /// <param name="engine">This side's engine areas: the composition's container.</param>
    // The [NetState] fields per class, remembered for this registry's load
    // (statics to zero: it was a process-wide memo).
    private NetStateFields _netStateFields => _netStateFieldsMade ??= new NetStateFields(Log);
    private NetStateFields? _netStateFieldsMade;

    /// <summary>
    /// This registry's log: its composition's (statics to zero), resolved
    /// once from the container it was built over, which registers it with
    /// the engine areas after this registry is constructed.
    /// </summary>
    internal ILog Log => _log ??= _engine.Require<ILog>();

    /// <summary>The lines this registry's console prints (a fact's way to read what it logged).</summary>
    internal ConsoleLines Lines => _engine.Require<ConsoleLines>();
    private ILog? _log;

    public ClientStandaloneRegistry(IServiceLocator engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
    }

    private IServiceLocator? _resolver;

    private IServiceLocator Resolver
        => _resolver ??= Services is null ? _engine : new FallbackLocator(Services, _engine);

    /// <summary>
    /// The container this registry builds entities from, or null to build them
    /// with their parameterless constructors alone.
    ///
    /// <para>
    /// The client half of the same arrangement the server has, and it is what
    /// the client side of this change is FOR: a drawn entity had no way at all
    /// to reach anything the module owned. Its only route was a static, and a
    /// static on this side is worse than on the server — the client module is
    /// reloadable, so a static holding the previous load's HUD keeps drawing
    /// into an object nothing paints any more.
    /// </para>
    /// </summary>
    public IServiceLocator? Services
    {
        get => _services;
        set { _services = value; _resolver = null; }
    }

    private IServiceLocator? _services;

    public int RegisterAll(Assembly assembly)
    {
        int found = 0;

        foreach (var type in LoadedAssemblies.Types(assembly, Log))
        {
            if (type.IsAbstract || !typeof(ClientStandaloneEntity).IsAssignableFrom(type))
                continue;

            var attribute = type.GetCustomAttribute<ClientStandaloneEntityAttribute>();
            if (attribute is null)
                continue;

            _byClassName[attribute.ClassName] = type;
            found++;
        }

        return found;
    }

    /// <summary>
    /// Whether a client class claims this name. Asked when deciding whether to
    /// publish a class's networked state: a table on this side with nothing to
    /// decode it is at best waste and at worst a class the server does not have.
    /// </summary>
    public bool IsRegistered(string className) => _byClassName.ContainsKey(className);

    /// <summary>
    /// Every classname registered here. The client's half of what Step 6 needs:
    /// a type id is only a name if this side has a table to resolve it in, and
    /// the classes that declare no networked state are the majority.
    /// </summary>
    public IReadOnlyCollection<string> ClassNames => _byClassName.Keys;

    /// <summary>
    /// False when no client class claims the name, which is ordinary: an entity
    /// may be entirely server-side as far as logic goes and still be drawn by
    /// the shell alone.
    /// </summary>
    public bool Create(EntityHandle handle, string className)
    {
        if (Directory.Resolve(handle) is not null)
            return true;

        if (!_byClassName.TryGetValue(className, out var type))
            return false;

        if (!Activate(type, out var instance))
            return false;

        instance.Handle = handle;
        instance.Log = Log;

        var entities = Resolver.Require<IEngineEntities>();

        instance.BindEngine(entities, Resolver.Require<IEngineHost>());

        // Created but NOT bound: this side has no writable blob, only a
        // borrowed one per update. See NetStateBinding.Attach.
        instance.NetStates = NetStateBinding.AttachLocal(instance, handle, _netStateFields);

        // THE ONE ASK, and everything downstream reads the answer rather than
        // asking again. This is the EntityGetIndex crossing; the shell writes
        // m_nEntIndex once in its constructor and never again, so the answer is
        // a constant for the entity's life and the entity carries it.
        //
        // THROUGH THE INJECTED ENGINE rather than EntityHandle.Index, which
        // reaches the STATIC Engine and therefore a different object from the
        // one every consumer of this index was already using — the lights read
        // their key off `Entities.GetIndex(Handle)`, which is this. Two routes
        // to one number is how a mirror ends up agreeing with a slot nobody
        // reads.
        int index = entities.GetIndex(handle);

        // BEFORE THE BIND, because the directory erases an entity's index entry
        // using the index the ENTITY carries. An entity bound while still
        // carrying -1 would be indexed here and unindexable on the way out.
        instance.BindIndex(index);

        // ONE CALL for both tables and the notification. A bind that took the
        // handle table without the class index would leave the entity findable
        // by handle and invisible to OfClass, which is the shape of failure the
        // two hand-maintained dictionaries this replaced could produce.
        Directory.Bind(handle, index, instance);

        return true;
    }

    /// <summary>
    /// Builds one entity class, through the container.
    ///
    /// The server's <c>ScriptedEntityRegistry.Activate</c> line for line, and
    /// deliberately so: the two registries are the same shape on purpose, and
    /// an entity class that cannot be built should fail the same way on both
    /// sides. A refusal is a warning naming the class; the shell still exists
    /// and still draws whatever it draws with no managed half, which is exactly
    /// what an unclaimed classname already does here.
    /// </summary>
    private bool Activate(Type type, [NotNullWhen(true)] out ClientStandaloneEntity? instance)
    {
        instance = null;

        if (!_activators.TryGetValue(type, out var activator))
        {
            try
            {
                _activators[type] = activator = ServiceActivator.For(type);
            }
            catch (ServiceResolutionException failure)
            {
                Log.Warning($"[SourceSharp] {type.Name} cannot be built: {failure.Message}\n");
                return false;
            }
        }

        object made;

        try
        {
            // The caller's container first, the real engine behind it. See
            // FallbackLocator.
            made = activator.Create(Resolver);
        }
        catch (ServiceResolutionException failure)
        {
            Log.Warning($"[SourceSharp] {type.Name} cannot be built: {failure.Message}\n");
            return false;
        }
        catch (TargetInvocationException failure)
        {
            Log.Warning($"[SourceSharp] {type.Name} threw while being built: " +
                           $"{failure.InnerException?.Message ?? failure.Message}\n");
            return false;
        }

        if (made is not ClientStandaloneEntity built)
        {
            Log.Warning($"[SourceSharp] could not instantiate {type.Name}\n");
            return false;
        }

        instance = built;
        return true;
    }

    /// <summary>
    /// The managed client entity at an ENTITY INDEX, or null if that index is
    /// not one of ours — which for a trace means it hit the world, a brush, or
    /// an entity this side does not own.
    ///
    /// <para>
    /// This is what makes the legacy pool's collision arm answerable in C#.
    /// Stock asks <c>dynamic_cast&lt;CDynamicProp *&gt;</c> and
    /// <c>dynamic_cast&lt;CBaseDoor *&gt;</c> of what it hit, and in this build
    /// both are hardcoded false — a managed prop or door is not a native type
    /// to cast to, so a gib falls through both (behaviour ledger row C4). Asked
    /// on this side, of entities this side owns, it is an ordinary question.
    /// </para>
    /// </summary>
    public ClientStandaloneEntity? GetByIndex(int entityIndex)
        => Directory.ByIndex(entityIndex);

    public ClientStandaloneEntity? Get(EntityHandle handle)
        => Directory.Resolve(handle);

    /// <summary>
    /// Every live client entity of a classname, which may end in '*' —
    /// <c>plan_entity.md</c> §3.5's find-by-class, answered from the
    /// directory's index rather than by a walk.
    ///
    /// <para>
    /// It exists because the callers already did: eighteen sites walked
    /// <see cref="Live"/> and compared classnames by hand, which is the same
    /// question asked eighteen times with eighteen chances to spell the
    /// comparison differently. Two of them used
    /// <c>Contains(…, OrdinalIgnoreCase)</c> — a SUBSTRING match, which is not
    /// what any Source classname lookup has ever meant — so the answers
    /// genuinely differed.
    /// </para>
    /// </summary>
    public IEnumerable<ClientStandaloneEntity> OfClass(string? className)
        => Directory.OfClass(className);

    /// <summary>
    /// Every live client entity answering to a targetname, which may end in
    /// '*'.
    ///
    /// <para>
    /// Nothing NAMES a client entity today — targetnames are not networked —
    /// so this answers empty on a live client, and that is the correct answer
    /// rather than a gap. It is here because §3.5 puts find-by-name in the
    /// directory, and because the alternative is the first caller that needs it
    /// adding a walk at its own call site, which is precisely how the native
    /// list grew the arm this wave is taking away.
    /// </para>
    /// </summary>
    public IEnumerable<ClientStandaloneEntity> OfName(string? name)
        => Directory.OfName(name);

    /// <summary>
    /// Every client entity that exists right now, newest first in no particular
    /// order.
    ///
    /// <para>
    /// A COPY rather than the dictionary's own enumerator, because the obvious
    /// caller is a diagnostic that asks each entity a question, and asking an
    /// entity a question is allowed to make one — a model resolving, a class
    /// deciding it is done. Enumerating the live table while it changes throws,
    /// and a debugging command that crashes the client is worse than no command.
    /// </para>
    /// </summary>
    public IReadOnlyList<ClientStandaloneEntity> Live()
        => Directory.Snapshot();

    /// <summary>Told each removal, after the entity's own OnRemoved: the client's skeletons forget it.</summary>
    public Action<EntityHandle>? RemovalForget { get; set; }

    public void Remove(EntityHandle handle)
    {
        if (Directory.Resolve(handle) is not ClientStandaloneEntity e)
            return;

        e.OnRemoved();

        // The entity's skeleton goes with it (ClientSkeletons.Forget).
        RemovalForget?.Invoke(handle);

        // BY THE INDEX THE ENTITY CARRIES, in one store. This used to walk
        // every live entity looking for the object by reference, so a removal
        // cost O(live) and a map full of gibs paid it once per gib; the
        // directory holds the index the entity was bound under and clears it
        // only if it still names THIS handle.
        Directory.Unbind(handle);

        // AFTER Unbind and after OnRemoved, not before either: a light takes
        // itself out of the dlight registry from OnRemoved, releasing a pool
        // slot needs the key it was allocated under, and the directory reads
        // this very field to find the index entry to clear. Cleared at all
        // because indices are recycled — an entity that kept its number after
        // it went would hand a stale key to whatever took the slot.
        e.BindIndex(-1);
    }

    //-------------------------------------------------------------------------
    // Hot reload. The server's ScriptedEntityRegistry line for line, with less
    // to carry: there are no map keys and no outputs on this side.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every live client entity's state, into the ABI, where it outlives this
    /// assembly.
    /// </summary>
    /// <param name="store">The side's reload store (statics to zero): its state's, handed in by the module.</param>
    public int SaveForReload(ReloadStore store)
    {
        List<SavedEntity> states = Collect();

        ReloadCarrier.Put(store, states);

        return states.Count;
    }

    /// <summary>
    /// The same walk, answering the records instead of putting them anywhere —
    /// the client's half of docs/managed-saverestore.md, and the server's
    /// <c>ScriptedEntityRegistry.Collect</c> line for line.
    ///
    /// <para>
    /// A SAVE MUST NOT TOUCH THE RELOAD STORE. That store is a handover between
    /// two halves of one reload; a save writing into it would leave records
    /// there for a reload that is not coming, and the next <c>Take</c> would
    /// hand them over.
    /// </para>
    /// </summary>
    public List<SavedEntity> Collect()
    {
        var states = new List<SavedEntity>(Directory.Count);

        // A2. See the report below the walk, and the server's Collect for the
        // whole of the argument -- this is the same guard on the other
        // registry, written out rather than shared because the two walks are
        // separate implementations and that is exactly the situation where one
        // is fixed and the other is not.
        int skipped = 0;
        string? firstSkipped = null;

        foreach (var (handle, instance) in Directory.Bindings.ToArray())
        {
            var state = new SavedEntity(handle.Value, instance.ClassName, string.Empty);

            try
            {
                instance.SaveState(state);

                if (!state.DefaultRan)
                {
                    Log.Warning($"[SourceSharp] {instance.GetType().Name}.SaveState did not call " +
                                $"base.SaveState; its networked fields were saved anyway\n");

                    instance.SaveDeclaredState(state);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SourceSharp] {instance.ClassName}: SaveState threw ({e.Message}); " +
                            $"this entity's state is not carried across the reload\n");

                skipped++;
                firstSkipped ??= instance.ClassName;
                continue;
            }

            states.Add(state);
        }

        // A2, and the client's half of it. See ScriptedEntityRegistry.Collect.
        if (skipped > 0)
        {
            Log.Warning($"[SourceSharp] collect: {states.Count} of {Directory.Count} entities " +
                        $"carried; {skipped} SKIPPED because SaveState threw, first " +
                        $"'{firstSkipped}'. A block that silently omits an entity restores " +
                        "a world missing it, and from outside that looks like a save that " +
                        "worked\n");
        }

        return states;
    }

    /// <summary>
    /// Rebuilds the managed half of every client entity that had one, and puts
    /// its state back.
    ///
    /// <para>
    /// Through <see cref="Create"/>, which is the same path an entity arriving
    /// from the server takes — so a class the new build no longer declares is
    /// refused there, once, in the place that already knows how to say so.
    /// </para>
    /// </summary>
    /// <param name="store">The side's reload store, the one <see cref="SaveForReload"/> wrote.</param>
    public int RestoreAfterReload(ReloadStore store)
        => RestoreFrom(ReloadCarrier.Take(store));

    /// <summary>
    /// The same rebuild, from records the caller supplies. See
    /// <see cref="Collect"/>.
    /// </summary>
    public int RestoreFrom(IReadOnlyList<SavedEntity> saved)
    {
        int restored = 0;

        foreach (var state in saved)
        {
            var handle = new EntityHandle(state.Handle);

            if (!Create(handle, state.ClassName))
                continue;

            var instance = Get(handle);

            if (instance is null)
                continue;

            state.DefaultRan = false;

            try
            {
                instance.LoadState(state);

                if (!state.DefaultRan)
                {
                    Log.Warning($"[SourceSharp] {instance.GetType().Name}.LoadState did not call " +
                                $"base.LoadState; its networked fields were restored anyway\n");

                    instance.LoadDeclaredState(state);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"[SourceSharp] {state.ClassName}: LoadState threw ({e.Message}); " +
                            $"this entity is back but its state is not\n");
                continue;
            }

            restored++;
        }

        return restored;
    }
}
