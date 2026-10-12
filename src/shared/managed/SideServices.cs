using System.Reflection;

namespace SourceSharp.Abi;

/// <summary>
/// Marks a class as a service of one side, found by the scan that finds
/// <c>[ConCommand]</c> and registered in that side's container as a lazy
/// singleton, built by constructor injection.
///
/// <para>
/// This is how a mod's assemblies join the game: listing one in mods.yaml
/// loads it, and loading it is all the registration a marked class needs. A
/// marked class that implements a hook interface (<see cref="IGameLifecycle"/>,
/// <see cref="IFrameLifecycle"/>, ...) is then called by that hook's driver,
/// because drivers collect every registered implementation.
/// </para>
///
/// <para>
/// Repeat the attribute, once per side, for a service both ends use. It is
/// always registered under its own type; <see cref="As"/> adds contracts, each
/// resolving to the same instance.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SideServiceAttribute(Side side) : Attribute
{
    public Side Side { get; } = side;

    /// <summary>The contracts the service is also registered under.</summary>
    public Type[] As { get; init; } = [];
}

/// <summary>
/// What a mod registers when an attribute cannot say it: a type from an
/// assembly that cannot carry <see cref="SideServiceAttribute"/>, an object a
/// factory makes, a third-party type. Marked <c>[SideService(side)]</c> like a
/// service, which here names the side it registers for (the registrar itself is
/// not registered); any number per assembly. Built by the container, so its
/// constructor can take the core's services.
/// </summary>
public interface ISideServiceRegistrar
{
    void Register(ISideServiceRegistry registry);
}

/// <summary>
/// The part of a side's container a mod may write to. Every registration is
/// refused if the core, or another mod assembly, already registered the
/// contract: a mod adds services, it never replaces one.
/// </summary>
public interface ISideServiceRegistry
{
    void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService;

    void AddInstance<TService>(TService instance) where TService : class;

    /// <summary>Built on first resolve, once.</summary>
    void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class;

    /// <summary>
    /// Registers <see cref="IHooks{T}"/> for a hook interface the mod defines,
    /// so its services can dispatch to every implementation. The container
    /// cannot resolve open generics, so each hook interface is named once.
    /// </summary>
    void AddHooks<THook>() where THook : class;
}

/// <summary>
/// Registers every <see cref="SideServiceAttribute"/> class and runs every
/// <see cref="ISideServiceRegistrar"/> of one side, after the module's own
/// registrations and before its drivers collect their participants.
///
/// <para>
/// Stateless: the state is the container it is given, and the registry it
/// hands out lives for one call.
/// </para>
/// </summary>
public static class SideServices
{
    /// <summary>
    /// Registers the side's mod services into <paramref name="services"/>.
    /// <paramref name="types"/> is what the scan found
    /// (<see cref="LoadedAssemblies.Types"/>); a test passes its own.
    /// </summary>
    /// <returns>The contracts the mods registered, in order: what <see cref="DisposeBuilt"/> tears down.</returns>
    public static IReadOnlyList<Type> Register(ServiceContainer services, Side side, IEnumerable<Type> types)
    {
        var registry = new Registry(services);
        var candidates = types.Where(t => t is { IsClass: true, IsAbstract: false }).ToArray();

        foreach (var type in candidates)
        {
            var marks = type.GetCustomAttributes<SideServiceAttribute>().Where(a => a.Side == side).ToArray();
            if (marks.Length == 0)
                continue;

            if (marks.Length > 1)
                throw new ServiceResolutionException(
                    $"{type.FullName} is marked [SideService({side})] more than once");

            if (typeof(ISideServiceRegistrar).IsAssignableFrom(type))
                continue;

            registry.Claim(type, type);
            services.Add(type, type);

            foreach (var contract in marks[0].As)
            {
                if (!contract.IsAssignableFrom(type))
                    throw new ServiceResolutionException(
                        $"{type.FullName} is registered As {contract.Name}, which it does not implement");

                registry.Claim(contract, type);
                var concrete = type;
                services.AddFactory(contract, locator => locator.Resolve(concrete)!);
            }
        }

        foreach (var type in candidates.Where(t => typeof(ISideServiceRegistrar).IsAssignableFrom(t)
                                                   && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == side)))
        {
            var registrar = (ISideServiceRegistrar)services.Create(type);
            registry.Owner = type;
            registrar.Register(registry);
        }

        return registry.Registered;
    }

    /// <summary>
    /// Disposes, newest first, every mod service in <paramref name="registered"/>
    /// that was built: at module shutdown and before a hot reload unloads the
    /// mod's assemblies. Never builds one. An object registered under several
    /// contracts is disposed once. A failure is logged and the rest still run.
    /// </summary>
    /// <remarks>
    /// <see cref="IDisposable"/> is preferred. An <see cref="IAsyncDisposable"/>-only
    /// service is waited on here, on the game thread, so its disposal must not
    /// need the game thread to make progress.
    /// </remarks>
    public static int DisposeBuilt(ServiceContainer services, IReadOnlyList<Type> registered, ILog log)
    {
        var done = new HashSet<object>(ReferenceEqualityComparer.Instance);
        int disposed = 0;

        for (int i = registered.Count - 1; i >= 0; i--)
        {
            if (!services.TryGetBuilt(registered[i], out var instance) || instance is null || !done.Add(instance))
                continue;

            try
            {
                if (instance is IDisposable sync)
                    sync.Dispose();
                else if (instance is IAsyncDisposable async)
                    async.DisposeAsync().AsTask().GetAwaiter().GetResult();
                else
                    continue;

                disposed++;
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp] disposing mod service {instance.GetType().FullName} failed: {e.Message}\n");
            }
        }

        return disposed;
    }

    private sealed class Registry(ServiceContainer services) : ISideServiceRegistry
    {
        // Who registered each contract a mod added, to name both sides of a clash.
        private readonly Dictionary<Type, Type> _claims = new();

        // The core's contracts: everything registered before the first claim.
        private readonly HashSet<Type> _core = services.ServiceTypes.ToHashSet();

        public Type? Owner { get; set; }

        public List<Type> Registered { get; } = new();

        public void Claim(Type contract, Type owner)
        {
            if (_core.Contains(contract))
                throw new ServiceResolutionException(
                    $"{owner.FullName} ({owner.Assembly.GetName().Name}) registers {contract.Name}, " +
                    "which the core already registered: a mod adds services, it never replaces one");

            if (_claims.TryGetValue(contract, out var first))
                throw new ServiceResolutionException(
                    $"{contract.Name} is registered twice: by {first.FullName} ({first.Assembly.GetName().Name}) " +
                    $"and by {owner.FullName} ({owner.Assembly.GetName().Name})");

            _claims[contract] = owner;
            Registered.Add(contract);
        }

        public void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService
        {
            Claim(typeof(TService), Owner!);
            services.Add<TService, TImplementation>();
        }

        public void AddInstance<TService>(TService instance) where TService : class
        {
            Claim(typeof(TService), Owner!);
            services.AddInstance(instance);
        }

        public void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class
        {
            Claim(typeof(TService), Owner!);
            services.AddFactory(factory);
        }

        public void AddHooks<THook>() where THook : class =>
            AddFactory<IHooks<THook>>(locator => new Hooks<THook>(locator, locator.Resolve<ILog>()
                ?? throw new ServiceResolutionException("IHooks needs ILog in the side's container")));
    }
}
