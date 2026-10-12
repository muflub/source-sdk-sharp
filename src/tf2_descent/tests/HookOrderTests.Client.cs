extern alias client;

using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using Xunit;
// The client's copy of the shared registration vocabulary (plan_side_isolation
// P7: the shared source compiles into each side), which the client registrar
// and the client's [SideService] classes are written against.
using ClientSideServiceRegistry = client::SourceSharp.Abi.ISideServiceRegistry;
using ClientSideServiceRegistrar = client::SourceSharp.Abi.ISideServiceRegistrar;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;

namespace TF2.Descent.Tests;

/// <summary>The client half of <see cref="HookOrderTests"/>: the same three steps over the client's types.</summary>
public sealed partial class HookOrderTests
{
    private sealed class ClientRecordingRegistry : ClientSideServiceRegistry
    {
        public List<Type> Hooks { get; } = new();
        public void Add<TService, TImplementation>() where TService : class where TImplementation : class, TService { }
        public void AddInstance<TService>(TService instance) where TService : class { }
        public void AddFactory<TService>(Func<IServiceLocator, TService> factory) where TService : class { }
        public void AddHooks<THook>() where THook : class => Hooks.Add(typeof(THook));
    }

    private static List<Type> ClientHooksOf(ClientSideServiceRegistrar registrar)
    {
        var registry = new ClientRecordingRegistry();
        registrar.Register(registry);
        return registry.Hooks;
    }

    private static (string Name, int Order)[] ClientImplementations(Assembly assembly, Side side, Type hook) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && hook.IsAssignableFrom(t)
                        && t.GetCustomAttributes<ClientSideServiceAttribute>().Any(a => a.Side == side))
            .Select(t => (t.Name, OrderOf(RuntimeHelpers.GetUninitializedObject(t), hook)))
            .ToArray();

    /// <summary>Each class in <paramref name="assembly"/> implementing IClientLevelHook, with the order the client's Hooks sorts by.</summary>
    private static (string Name, int Order)[] ClientLevelHookOrders(Assembly assembly, bool requireSideService) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IClientLevelHook).IsAssignableFrom(t)
                        && (!requireSideService || t.GetCustomAttributes<ClientSideServiceAttribute>().Any(a => a.Side == Side.Client)))
            .Select(t => (t.Name, (RuntimeHelpers.GetUninitializedObject(t) as IOrderedHook)?.HookOrder ?? 0))
            .ToArray();

    private static void ClientAssertDistinct(Assembly assembly, Side side, IEnumerable<Type> hooks)
    {
        foreach (var hook in hooks)
        {
            var found = ClientImplementations(assembly, side, hook);
            var ties = found.GroupBy(f => f.Order).Where(g => g.Count() > 1)
                .Select(g => $"{hook.Name} order {g.Key}: {string.Join(", ", g.Select(f => f.Name))}")
                .ToArray();
            Assert.Empty(ties);
        }
    }
}
