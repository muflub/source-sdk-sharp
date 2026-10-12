extern alias client;

using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Descent.Server;
using Xunit;
using ClientRegistrar = client::TF2.Descent.Client.TF2DescentClientRegistrar;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;

namespace TF2.Descent.Tests;

/// <summary>
/// Every hook a TF2Descent side dispatches has implementations with distinct
/// <see cref="IOrderedHook.HookOrder"/>s. <c>Hooks&lt;T&gt;</c> refuses a tie
/// the first time it collects, so a tie makes every dispatch of that hook
/// throw. LevelPrefetch joined LevelHop at order 0 on IHostCommandHook, and
/// every host command, PrepareHop and Exec included, threw in the hub's pod.
/// Each service's own tests built it alone, so none of them could see it.
/// </summary>
public sealed partial class HookOrderTests
{
    /// <summary>The hook types a registrar registers; everything else is refused.</summary>
    private sealed class RecordingRegistry : ISideServiceRegistry
    {
        public List<Type> Hooks { get; } = new();
        public void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService { }
        public void AddInstance<TService>(TService instance) where TService : class { }
        public void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class { }
        public void AddHooks<THook>() where THook : class => Hooks.Add(typeof(THook));
    }

    private static List<Type> HooksOf(ISideServiceRegistrar registrar)
    {
        var registry = new RecordingRegistry();
        registrar.Register(registry);
        return registry.Hooks;
    }

    /// <summary>Each [SideService(side)] class in <paramref name="assembly"/> implementing <paramref name="hook"/>, with its order.</summary>
    private static (string Name, int Order)[] Implementations(Assembly assembly, Side side, Type hook) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && hook.IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == side))
            .Select(t => (t.Name, OrderOf(RuntimeHelpers.GetUninitializedObject(t), hook)))
            .ToArray();

    /// <summary>The order the hook's dispatcher sorts by: the lifecycle drivers have their own.</summary>
    private static int OrderOf(object service, Type hook) =>
        hook == typeof(IClientLifecycle) ? ((IClientLifecycle)service).ClientLifecycleOrder
        : hook == typeof(IGameLifecycle) ? ((IGameLifecycle)service).LifecycleOrder
        : (service as IOrderedHook)?.HookOrder ?? 0;

    private static void AssertDistinct(Assembly assembly, Side side, IEnumerable<Type> hooks)
    {
        foreach (var hook in hooks)
        {
            var found = Implementations(assembly, side, hook);
            var ties = found.GroupBy(f => f.Order).Where(g => g.Count() > 1)
                .Select(g => $"{hook.Name} order {g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
                .ToArray();
            Assert.Empty(ties);
        }
    }

    [Fact]
    public void Server_hook_implementations_have_distinct_orders()
    {
        // The registrar's hooks, and the core's lifecycle hooks the mod implements.
        var hooks = HooksOf(new TF2DescentServerRegistrar())
            .Append(typeof(IClientLifecycle)).Append(typeof(IGameLifecycle)).ToList();
        Assert.Contains(typeof(IHostCommandHook), hooks);

        // The stimulus: the hook that tied has several implementations to compare.
        Assert.True(Implementations(typeof(LevelHop).Assembly, Side.Server, typeof(IHostCommandHook)).Length >= 3);

        Assert.True(Implementations(typeof(LevelHop).Assembly, Side.Server, typeof(IClientLifecycle)).Length >= 2);
        AssertDistinct(typeof(LevelHop).Assembly, Side.Server, hooks);
    }

    [Fact]
    public void Client_hook_implementations_have_distinct_orders()
    {
        // The registrar's hooks, and the core's client hook the mod implements.
        var hooks = ClientHooksOf(new ClientRegistrar()).Append(typeof(IClientLevelHook)).ToList();
        Assert.True(hooks.Count >= 4, $"only {hooks.Count} client hook type(s) found");

        ClientAssertDistinct(typeof(ClientRegistrar).Assembly, Side.Client, hooks);
    }

    /// <summary>
    /// The client's level hooks are dispatched over the whole container, so the
    /// core client's own implementations take part beside the mod's. The core's
    /// ClientRopeSystem sat at order 0, LevelDownloads joined it there, and every
    /// tf2_descent client level init and shutdown threw (k3s run, 2026-10-11):
    /// a check over the mod's assembly alone could not see the tie.
    /// </summary>
    [Fact]
    public void Client_level_hooks_have_distinct_orders_beside_the_cores()
    {
        var mod = ClientLevelHookOrders(typeof(ClientRegistrar).Assembly, requireSideService: true);
        var core = ClientLevelHookOrders(typeof(IClientLevelHook).Assembly, requireSideService: false);

        // The stimulus: both sides bring at least one implementation.
        Assert.Contains(mod, f => f.Name == "LevelDownloads");
        Assert.Contains(core, f => f.Name == "ClientRopeSystem");

        var ties = mod.Concat(core).GroupBy(f => f.Order).Where(g => g.Count() > 1)
            .Select(g => $"IClientLevelHook order {g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
            .ToArray();
        Assert.Empty(ties);
    }
}
