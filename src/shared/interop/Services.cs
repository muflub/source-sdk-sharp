namespace SourceSharp.Abi;

/// <summary>
/// The read side of the container: what a service, an entity or a console
/// command is handed so it can find the systems it needs.
///
/// <para>
/// Two keys, deliberately. A TYPE is the key for a system — an entity asking
/// for <c>IGameRules</c> names something the compiler checked, and a rename
/// carries. A NAME is the key for a convar or a console command, because that
/// is the only key those have ever had: a mapper, a config file and a player at
/// the console all reach <c>sv_gravity</c> by its name, and a class asking for
/// the same setting should be able to say the same word.
/// </para>
///
/// <para>
/// Read-only on purpose. Everything is registered while a module initialises,
/// by the module, and a system that could add to the container from inside a
/// tick would make what is registered depend on what has happened in the map
/// so far — which is exactly the thing that makes a container hard to reason
/// about. <see cref="ServiceContainer"/> is the write side, and only the module
/// holds one.
/// </para>
/// </summary>
public interface IServiceLocator
{
    /// <summary>
    /// The service registered for a type, or null if there is none.
    ///
    /// Null rather than a throw, because the caller usually knows what a miss
    /// means and the container does not: a constructor parameter with no
    /// default treats it as fatal, one with a default treats it as "carry on
    /// without".
    /// </summary>
    object? Resolve(Type serviceType);

    /// <summary>
    /// What is registered under a console name — a convar or a console command.
    /// Null if nothing is. Case-insensitive, as the console is.
    /// </summary>
    object? ResolveNamed(string name);

    /// <summary>Whether a type has a service, without building one.</summary>
    bool IsRegistered(Type serviceType);

    /// <summary>Every service type registered, for a listing or a diagnostic.</summary>
    IReadOnlyCollection<Type> ServiceTypes { get; }

    /// <summary>Every console name registered.</summary>
    IReadOnlyCollection<string> Names { get; }
}

/// <summary>
/// Typed conveniences over <see cref="IServiceLocator"/>.
///
/// Extension methods rather than members of the interface so that the interface
/// stays the two lookups it really is — which is what makes it a couple of
/// lines to fake in a test.
/// </summary>
public static class ServiceLocatorExtensions
{
    /// <summary>The service for <typeparamref name="T"/>, or null.</summary>
    public static T? Resolve<T>(this IServiceLocator services) where T : class
        => services.Resolve(typeof(T)) as T;

    /// <summary>
    /// The service for <typeparamref name="T"/>, or a throw naming it.
    ///
    /// For the caller that cannot carry on without it and would otherwise
    /// dereference a null several frames later, where the message names a field
    /// rather than the service nobody registered.
    /// </summary>
    public static T Require<T>(this IServiceLocator services) where T : class
        => services.Resolve(typeof(T)) as T
           ?? throw new ServiceResolutionException(
               $"no service is registered for {typeof(T).Name}");

    /// <summary>Whether a type has a service.</summary>
    public static bool IsRegistered<T>(this IServiceLocator services) where T : class
        => services.IsRegistered(typeof(T));

    /// <summary>
    /// What is registered under a console name, if it is of the type asked for.
    ///
    /// Null covers both misses — no such name, and a name holding something
    /// else — because a class reaching for a convar by name has no way to tell
    /// the two apart and nothing useful to do differently about either.
    /// </summary>
    public static T? ResolveNamed<T>(this IServiceLocator services, string name) where T : class
        => services.ResolveNamed(name) as T;

    /// <summary>The convar of that name, whatever its declared type.</summary>
    public static ConVarBase? ConVar(this IServiceLocator services, string name)
        => services.ResolveNamed(name) as ConVarBase;

    /// <summary>The console command of that name.</summary>
    public static IConCommand? Command(this IServiceLocator services, string name)
        => services.ResolveNamed(name) as IConCommand;
}

/// <summary>
/// Two containers, asked in order.
///
/// <para>
/// What lets a test register the one thing it cares about and still get an
/// entity built. Before the engine came from a container an entity called
/// <c>Engine</c> directly and it worked from anywhere; a test that
/// supplies a container holding a fake damage area must not thereby lose the
/// other twenty-one areas its entity's base classes need.
/// </para>
///
/// <para>
/// Order matters and it is the obvious one: the caller's container wins. A fake
/// registered for an area is what the entity gets, and the fallback only
/// answers for what the caller said nothing about.
/// </para>
/// </summary>
public sealed class FallbackLocator : IServiceLocator
{
    private readonly IServiceLocator _first;
    private readonly IServiceLocator _second;

    public FallbackLocator(IServiceLocator first, IServiceLocator second)
    {
        _first = first;
        _second = second;
    }

    /// <inheritdoc/>
    public object? Resolve(Type serviceType)
        => _first.Resolve(serviceType) ?? _second.Resolve(serviceType);

    /// <inheritdoc/>
    public object? ResolveNamed(string name)
        => _first.ResolveNamed(name) ?? _second.ResolveNamed(name);

    /// <inheritdoc/>
    public bool IsRegistered(Type serviceType)
        => _first.IsRegistered(serviceType) || _second.IsRegistered(serviceType);

    /// <inheritdoc/>
    public IReadOnlyCollection<Type> ServiceTypes
        => _first.ServiceTypes.Concat(_second.ServiceTypes).Distinct().ToArray();

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Names
        => _first.Names.Concat(_second.Names).Distinct().ToArray();
}

/// <summary>
/// One row of the container, as a listing sees it.
///
/// <para>
/// Both of the container's tables in one shape, because to somebody asking
/// "what can this entity ask for" they are one question with two spellings.
/// <paramref name="LookedUpBy"/> says which: <c>type</c> for a contract written
/// in a constructor and checked by the compiler, <c>name</c> for the string a
/// <c>[Named]</c> parameter carries.
/// </para>
///
/// <para>
/// <paramref name="Implementation"/> is the concrete class that answers, and it
/// is worth a column of its own: a contract tells you what you may call, and
/// only the implementation tells you WHICH object arrives — which is the thing
/// that is wrong when an entity consults something that is not the live system.
/// </para>
///
/// <para>
/// <paramref name="Built"/> is false only for a lazy registration nothing has
/// asked for yet. It is not a fault: laziness is the point, and a service
/// nothing has needed is a service nothing has paid for.
/// </para>
/// </summary>
public readonly record struct ServiceInfo(
    string Key,
    string LookedUpBy,
    string Kind,
    string Implementation,
    bool   Built);

/// <summary>
/// The write side: what a module builds during init and hands to everything it
/// creates afterwards.
///
/// <para>
/// SINGLETONS ONLY, and that is not a simplification to be regretted later. The
/// things in here are the mod's systems — the rules, the player records, the
/// pickup bookkeeping — and there is exactly one of each per side per load.
/// Scopes and transients exist for request-shaped programs; a game tick is not
/// a request, and an entity that wants a fresh object per instance makes one
/// with <c>new</c>.
/// </para>
///
/// <para>
/// Registration is by INTERFACE. An entity asking for <c>IGameRules</c> can be
/// tested against a stub, and the concrete class stays free to grow without
/// widening what every caller can see. Registering a concrete type as well is
/// allowed and is what convars do, since a convar class IS its own contract.
/// </para>
///
/// <para>
/// Not thread-safe for WRITES, and it does not pretend to be. Everything is
/// registered on the game thread while the module initialises, before the
/// development API's socket exists. Reads are safe once that is done, which is
/// what the API's listing threads need.
/// </para>
/// </summary>
public sealed class ServiceContainer : IServiceLocator
{
    /// <summary>
    /// One registration. Either an instance handed over ready-made, or a type
    /// to build the first time somebody asks.
    /// </summary>
    private sealed class Entry
    {
        public object? Instance;
        public Type? ImplementationType;

        // Built by a function on first resolve, once (AddFactory). Its
        // concrete type is unknown until then, so ResolveAll goes by the
        // contract it was registered under.
        public Func<IServiceLocator, object>? Factory;
    }

    private readonly Dictionary<Type, Entry> _byType = new();

    // The order registrations were made in.
    //
    // Dictionary enumeration order is insertion order in practice on .NET and
    // is NOT guaranteed to be, which is fine for Resolve (it asks for one key)
    // and not fine for ResolveAll -- a lifecycle whose participants run in an
    // order the runtime is free to change is a lifecycle that works until it
    // does not, on somebody else's machine. So the order is kept rather than
    // observed.
    private readonly List<Type> _registrationOrder = new();
    private readonly Dictionary<string, object> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, ServiceActivator> _activators = new();

    // The chain of types currently being built, so a cycle is reported as the
    // cycle it is rather than as a stack overflow with nothing in it worth
    // reading.
    private readonly List<Type> _building = new();

    /// <summary>
    /// A container that can already answer for itself.
    ///
    /// Registered here rather than left to each module to remember, because a
    /// class taking <see cref="IServiceLocator"/> to look something up later —
    /// which is what a registry that creates entities does — must not be the
    /// one case the container cannot satisfy.
    /// </summary>
    public ServiceContainer()
    {
        _byType[typeof(IServiceLocator)] = new Entry { Instance = this };
        _byType[typeof(ServiceContainer)] = new Entry { Instance = this };
    }

    //-------------------------------------------------------------------------
    // Registration.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Registers a ready-made instance under a contract.
    ///
    /// The common case in this mod: the module already holds the object,
    /// because something else needed it during init, and the container is how
    /// everything built afterwards reaches the same one.
    /// </summary>
    public ServiceContainer AddInstance<TService>(TService instance) where TService : class
        => AddInstance(typeof(TService), instance);

    /// <inheritdoc cref="AddInstance{TService}(TService)"/>
    public ServiceContainer AddInstance(Type serviceType, object instance)
    {
        if (!serviceType.IsInstanceOfType(instance))
            throw new ServiceResolutionException(
                $"{instance.GetType().Name} is not a {serviceType.Name} and cannot be registered as one");

        WarnIfReplacing(serviceType);
        Remember(serviceType);
        _byType[serviceType] = new Entry { Instance = instance };
        return this;
    }

    /// <summary>
    /// Registers a type to build on first ask, resolving its own constructor
    /// from this container.
    ///
    /// <para>
    /// Lazy rather than eager, and the reason is ordering: a system registered
    /// early may want one registered late, and building it at the moment it is
    /// declared would make the order lines appear in this file into a hidden
    /// dependency graph that a reader has to keep in their head. Deferring to
    /// the first ask means the only order that matters is "registered before
    /// anything resolves", which is a single line in the module.
    /// </para>
    /// </summary>
    public ServiceContainer Add<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
        => Add(typeof(TService), typeof(TImplementation));

    /// <summary>Registers a concrete type as its own contract.</summary>
    public ServiceContainer Add<TImplementation>() where TImplementation : class
        => Add(typeof(TImplementation), typeof(TImplementation));

    /// <inheritdoc cref="Add{TService, TImplementation}"/>
    public ServiceContainer Add(Type serviceType, Type implementationType)
    {
        if (!serviceType.IsAssignableFrom(implementationType))
            throw new ServiceResolutionException(
                $"{implementationType.Name} does not implement {serviceType.Name}");

        WarnIfReplacing(serviceType);
        Remember(serviceType);
        _byType[serviceType] = new Entry { ImplementationType = implementationType };
        return this;
    }

    /// <summary>
    /// Registers something under a console name — a convar or a console
    /// command.
    ///
    /// <para>
    /// One table for both kinds, because the console has one namespace: Source
    /// refuses a command whose name a convar already holds, so two tables here
    /// could only ever disagree with the engine about what a name means.
    /// </para>
    ///
    /// <para>
    /// A duplicate is reported rather than silently kept or silently replaced.
    /// It means two declarations claim one console name, which the engine will
    /// have refused one half of already — and a container quietly answering
    /// with the loser would hand a class the object whose writes go nowhere.
    /// </para>
    /// </summary>
    /// <summary>
    /// Registers a service built by <paramref name="factory"/> the first time
    /// somebody asks for it, and kept. For what a constructor cannot express: a
    /// value from configuration, an object a library hands out, one service
    /// under a second contract (resolve the first inside the factory).
    /// </summary>
    public ServiceContainer AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class
        => AddFactory(typeof(TService), factory);

    /// <inheritdoc cref="AddFactory{TService}(Func{IServiceLocator, TService})"/>
    public ServiceContainer AddFactory(Type serviceType, Func<IServiceLocator, object> factory)
    {
        WarnIfReplacing(serviceType);
        Remember(serviceType);
        _byType[serviceType] = new Entry { Factory = factory };
        return this;
    }

    /// <summary>
    /// The instance registered under <paramref name="serviceType"/> if it has
    /// been built, without building it: for shutdown, which must dispose what
    /// exists and never construct something only to tear it down.
    /// </summary>
    public bool TryGetBuilt(Type serviceType, out object? instance)
    {
        instance = _byType.TryGetValue(serviceType, out var entry) ? entry.Instance : null;
        return instance is not null;
    }

    public ServiceContainer AddNamed(string name, object instance)
    {
        if (name.Length == 0)
            throw new ServiceResolutionException("a service cannot be registered under an empty name");

        if (_byName.TryGetValue(name, out var existing) && !ReferenceEquals(existing, instance))
        {
            OwnLog().Warning($"[SourceSharp] two things claim the console name '{name}': " +
                           $"{existing.GetType().Name} and {instance.GetType().Name}; keeping the first\n");
            return this;
        }

        _byName[name] = instance;
        return this;
    }

    /// <summary>
    /// Whether a console name is taken. For a caller deciding whether to
    /// register, rather than one about to resolve.
    /// </summary>
    public bool IsNameRegistered(string name) => _byName.ContainsKey(name);

    /// <summary>
    /// Where this container says what it noticed: the <see cref="ILog"/> its
    /// composition registered in it, if one is there yet (statics to zero), or
    /// standard error before then.
    /// </summary>
    private ILog OwnLog()
        => _byType.TryGetValue(typeof(ILog), out var entry) && entry.Instance is ILog log ? log : new StandardErrorLog();

    private void WarnIfReplacing(Type serviceType)
    {
        if (!_byType.ContainsKey(serviceType))
            return;

        // Not an error: a test replacing a service with a stub is a legitimate
        // and useful thing to do, and so is a module registering a better
        // implementation over a default. Worth saying out loud all the same,
        // because in a module it is nearly always two lines that both meant to
        // be the only one.
        OwnLog().Warning($"[SourceSharp] {serviceType.Name} was already registered; replacing it\n");
    }

    //-------------------------------------------------------------------------
    // Resolution.
    //-------------------------------------------------------------------------

    /// <inheritdoc/>
    public object? Resolve(Type serviceType)
    {
        if (!_byType.TryGetValue(serviceType, out var entry))
            return null;

        if (entry.Instance is not null)
            return entry.Instance;

        if (entry.Factory is not null)
            return BuildFromFactory(serviceType, entry);

        // A lazy registration, being built now. The cycle check is around the
        // whole build rather than around the dictionary write, because the
        // cycle happens inside Create — a constructor asking for something that
        // asks for this one.
        var implementation = entry.ImplementationType!;

        if (_building.Contains(implementation))
            throw new ServiceResolutionException(
                "services depend on each other in a cycle: " +
                string.Join(" -> ", _building.Select(t => t.Name).Append(implementation.Name)));

        _building.Add(implementation);

        try
        {
            entry.Instance = ActivatorFor(implementation).Create(this);
        }
        finally
        {
            _building.RemoveAt(_building.Count - 1);
        }

        return entry.Instance;
    }

    /// <summary>
    /// Notes a service type the first time it is registered. A replacement
    /// keeps the original position, so re-registering something does not move
    /// it to the back of the lifecycle.
    /// </summary>
    private object BuildFromFactory(Type serviceType, Entry entry)
    {
        // Keyed by the contract: a factory has no implementation type to key on
        // until it has run, and running it is what a cycle prevents.
        if (_building.Contains(serviceType))
            throw new ServiceResolutionException(
                "services depend on each other in a cycle: " +
                string.Join(" -> ", _building.Select(t => t.Name).Append(serviceType.Name)));

        _building.Add(serviceType);

        try
        {
            var built = entry.Factory!(this)
                ?? throw new ServiceResolutionException($"the factory for {serviceType.Name} returned null");

            if (!serviceType.IsInstanceOfType(built))
                throw new ServiceResolutionException(
                    $"the factory for {serviceType.Name} built a {built.GetType().Name}, which is not one");

            entry.Instance = built;
            entry.Factory = null;
            return built;
        }
        finally
        {
            _building.RemoveAt(_building.Count - 1);
        }
    }

    private void Remember(Type serviceType)
    {
        if (!_byType.ContainsKey(serviceType))
            _registrationOrder.Add(serviceType);
    }

    /// <summary>
    /// Every registered service that implements <typeparamref name="T"/>, in
    /// registration order.
    ///
    /// <para>
    /// <b>It does not build the ones that do not match.</b> Registrations are
    /// lazy on purpose — see <see cref="Add{TService, TImplementation}"/> — so
    /// an implementation that resolved everything to find out what it was would
    /// construct the entire graph at whatever moment the first lifecycle event
    /// happened to arrive, and the careful ordering that laziness buys would be
    /// gone. Assignability is decided from the registration itself: the
    /// instance's own type where one was handed over, the declared
    /// implementation type where it was not. Only the matches are built.
    /// </para>
    ///
    /// <para>
    /// A service registered under two contracts appears once: the results are
    /// de-duplicated by instance, not by contract, because dispatching a
    /// lifecycle event to the same object twice is not a thing any participant
    /// is written to expect.
    /// </para>
    /// </summary>
    public IReadOnlyList<T> ResolveAll<T>() where T : class
    {
        var found = new List<T>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var serviceType in _registrationOrder)
        {
            if (!_byType.TryGetValue(serviceType, out var entry))
                continue;

            var concrete = entry.Instance?.GetType() ?? entry.ImplementationType
                           ?? (entry.Factory is not null ? serviceType : null);

            if (concrete is null || !typeof(T).IsAssignableFrom(concrete))
                continue;

            if (Resolve(serviceType) is T built && seen.Add(built))
                found.Add(built);
        }

        return found;
    }

    /// <summary>
    /// <see cref="ResolveAll{T}"/>, with the contract each was registered
    /// under: the first, in registration order, for a service registered under
    /// several. For a walk that names what it finds by contract (the client's
    /// reload participants, whose reload section is their contract's name).
    /// </summary>
    public IReadOnlyList<(Type Contract, T Service)> ResolveAllByContract<T>() where T : class
    {
        var found = new List<(Type, T)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach (var serviceType in _registrationOrder)
        {
            if (!_byType.TryGetValue(serviceType, out var entry))
                continue;

            var concrete = entry.Instance?.GetType() ?? entry.ImplementationType
                           ?? (entry.Factory is not null ? serviceType : null);

            if (concrete is null || !typeof(T).IsAssignableFrom(concrete))
                continue;

            if (Resolve(serviceType) is T built && seen.Add(built))
                found.Add((serviceType, built));
        }

        return found;
    }

    /// <inheritdoc/>
    public object? ResolveNamed(string name)
        => _byName.TryGetValue(name, out var instance) ? instance : null;

    /// <inheritdoc/>
    public bool IsRegistered(Type serviceType) => _byType.ContainsKey(serviceType);

    /// <inheritdoc/>
    public IReadOnlyCollection<Type> ServiceTypes => _byType.Keys;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    //-------------------------------------------------------------------------
    // Building things that are NOT services.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Builds an instance of a type the container does not hold, filling its
    /// constructor from the services it does.
    ///
    /// <para>
    /// This is the call the entity registries make, and it is the reason the
    /// container exists at all: a map entity is not a singleton — there are as
    /// many <c>trigger_hurt</c> instances as the map placed — but each of them
    /// wants the same systems that every singleton wants.
    /// </para>
    /// </summary>
    /// <exception cref="ServiceResolutionException">
    /// The type has no usable constructor, or one of its parameters cannot be
    /// filled and has no default.
    /// </exception>
    public object Create(Type type) => ActivatorFor(type).Create(this);

    /// <inheritdoc cref="Create(Type)"/>
    public T Create<T>() where T : class => (T)Create(typeof(T));

    /// <summary>
    /// The same, reporting a failure instead of throwing it.
    ///
    /// For the registries, which run over every declared class at load and must
    /// not let one bad declaration take the rest of them down with it: an
    /// entity class the container cannot build is a warning naming that class,
    /// and every other class in the map still works.
    /// </summary>
    public bool TryCreate(Type type, out object? instance, out string error)
    {
        try
        {
            instance = Create(type);
            error = string.Empty;
            return true;
        }
        catch (ServiceResolutionException failure)
        {
            instance = null;
            error = failure.Message;
            return false;
        }
        catch (System.Reflection.TargetInvocationException failure)
        {
            // The constructor itself threw. Reported the same way rather than
            // let through, because to the registry running the loop the two are
            // the same event — this class did not produce an instance — and the
            // inner exception is the part worth printing either way.
            instance = null;
            error = failure.InnerException?.Message ?? failure.Message;
            return false;
        }
    }

    /// <summary>
    /// Everything in the container, both tables, in a stable order.
    ///
    /// <para>
    /// For the development API's services view, which answers the one question
    /// this container otherwise makes harder rather than easier: before it,
    /// what an entity could reach was whatever statics happened to be in scope,
    /// which at least a compiler would list. Now it is whatever the module
    /// registered, and without a view of that the only answer is to read
    /// <c>ServerModule.Initialize</c>.
    /// </para>
    ///
    /// <para>
    /// Sorted rather than left in dictionary order, so two readings of an
    /// unchanged container are the same reading and a diff between two builds
    /// means something.
    /// </para>
    ///
    /// <para>
    /// Safe off the game thread, and deliberately so: it walks managed
    /// dictionaries that stop changing when init ends and touches no engine
    /// state. A debugging tool polling this must not be able to stall a frame.
    /// A LAZY service is described rather than built — asking what exists must
    /// not be what causes it to exist.
    /// </para>
    /// </summary>
    public IReadOnlyList<ServiceInfo> Describe()
    {
        var rows = new List<ServiceInfo>(_byType.Count + _byName.Count);

        foreach (var (contract, entry) in _byType)
        {
            // Self-registration is worth marking rather than hiding. A class
            // taking IServiceLocator to look something up later is doing a real
            // thing, and a reader scanning for what is available should see
            // that it is available.
            bool self = ReferenceEquals(entry.Instance, this);

            var implementation = entry.Instance?.GetType() ?? entry.ImplementationType ?? contract;

            string kind =
                self                                            ? "self" :
                typeof(ConVarBase).IsAssignableFrom(contract)   ? "convar" :
                entry.Instance is not null                      ? "instance" :
                entry.Factory is not null                       ? "factory" :
                                                                  "lazy";

            rows.Add(new ServiceInfo(
                contract.Name, "type", kind, implementation.Name, entry.Instance is not null));
        }

        foreach (var (name, instance) in _byName)
        {
            string kind =
                instance is ConVarBase  ? "convar" :
                instance is IConCommand ? "command" :
                                          "instance";

            // Always built: the name table only ever holds things that already
            // exist, because a convar and a command are both registered with
            // the engine before either reaches this.
            rows.Add(new ServiceInfo(name, "name", kind, instance.GetType().Name, true));
        }

        rows.Sort(static (a, b) =>
        {
            int by = string.CompareOrdinal(a.LookedUpBy, b.LookedUpBy);
            return by != 0 ? by : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
        });

        return rows;
    }

    /// <summary>
    /// The cached constructor plan for a type, worked out on first use.
    ///
    /// Cached HERE rather than in the activator, because the cost being avoided
    /// is per-entity: the plan is the same for every <c>trigger_hurt</c> a map
    /// places, and reflection is the expensive half of creating one.
    /// </summary>
    /// <remarks>
    /// LOCKED, although the game builds entities on one thread. Three
    /// containers are static and shared by every registry that was handed none
    /// (<c>ConCommandRegistry.Empty</c> and the two <c>NoServices</c>), so two
    /// threads creating entities through them fill this one cache at once, and
    /// a Dictionary written from two threads corrupts itself. Uncontended, the
    /// lock is the only cost.
    /// </remarks>
    private ServiceActivator ActivatorFor(Type type)
    {
        lock (_activators)
        {
            if (!_activators.TryGetValue(type, out var activator))
                _activators[type] = activator = ServiceActivator.For(type);

            return activator;
        }
    }
}
