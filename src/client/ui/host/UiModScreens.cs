using System.Reflection;
using System.Runtime.Loader;

using SourceSharp.Abi;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Vgui;

/// <summary>
/// A mod's own screens (stage 0.9): every <see cref="UiScreen"/> in the UI
/// assemblies mods.yaml lists (<c>assemblies.client_ui</c>), built through the
/// side's container and placed on the layer each names. The core never names
/// a mod screen type: the mod ships an assembly, the UI finds its screens.
/// </summary>
public static class UiModScreens
{
    /// <summary>
    /// The mod's UI assemblies by name, from the context this host was loaded
    /// in: in game the UI's own collectible one, which UiLoader has already
    /// loaded them into, so the copy found is the copy a reload swaps.
    /// </summary>
    public static IReadOnlyList<Assembly> Assemblies(IEnumerable<string> names)
    {
        var context = AssemblyLoadContext.GetLoadContext(typeof(UiModScreens).Assembly) ?? AssemblyLoadContext.Default;
        return [.. names.Select(n => context.LoadFromAssemblyName(new AssemblyName(n)))];
    }

    /// <summary>
    /// Every screen in <paramref name="assemblies"/>, built, in discovery
    /// order (<see cref="UiScreens.Discover"/>). A windowed screen gets the
    /// menu layer's <paramref name="windows"/>, as the core's do, so its
    /// z-order and its carried geometry are the layer's. A screen whose
    /// constructor cannot be filled throws, naming the screen and the
    /// parameter: a mod screen the UI quietly left out would be a screen
    /// nobody can tell is missing.
    /// </summary>
    public static IReadOnlyList<UiScreen> Build(IServiceLocator services, IReadOnlyList<Assembly> assemblies, UiWindows windows)
        => [.. UiScreens.Discover([.. assemblies]).Select(t => (UiScreen)new PartLocator(services, assemblies, windows).Build(t))];

    /// <summary>
    /// The core's screens, then the mod's: HUD-layer screens to
    /// <paramref name="hud"/>, every other to <paramref name="menu"/>. The
    /// core's order is kept; the mod's follow it by <see cref="UiScreen.Order"/>
    /// (stable: discovery order breaks a tie).
    /// </summary>
    public static (UiScreen[] Hud, UiScreen[] Menu) Place(IEnumerable<UiScreen> hud, IEnumerable<UiScreen> menu,
                                                          IReadOnlyList<UiScreen> mod)
    {
        var byOrder = mod.OrderBy(s => s.Order).ToArray();
        return ([.. hud, .. byOrder.Where(s => s.Layer == UiLayerKind.Hud)],
                [.. menu, .. byOrder.Where(s => s.Layer != UiLayerKind.Hud)]);
    }

    /// <summary>
    /// The container, plus the screen's own parts: data interfaces resolve
    /// from the side's services (the mod's adapters are
    /// <c>[SideService]</c>s of its client assembly); a concrete type of a UI
    /// assembly (a sub-panel) the services do not hold is built here, by the
    /// container's own constructor rules, once per screen. One per screen and
    /// never the side's container itself: that one caches activators by type
    /// for the life of the side, which would pin the UI's collectible context
    /// across every reload.
    /// </summary>
    private sealed class PartLocator(IServiceLocator services, IReadOnlyList<Assembly> assemblies, UiWindows windows) : IServiceLocator
    {
        private readonly Dictionary<Type, object> _parts = new() { [typeof(UiWindows)] = windows };

        public object Build(Type type) => ServiceActivator.For(type).Create(this);

        public object? Resolve(Type serviceType)
        {
            if (services.Resolve(serviceType) is { } service)
                return service;
            if (_parts.TryGetValue(serviceType, out var part))
                return part;
            if (serviceType.IsInterface || serviceType.IsAbstract || serviceType.ContainsGenericParameters
                || !(assemblies.Contains(serviceType.Assembly) || serviceType.Assembly == typeof(UiScreen).Assembly))
                return null;
            return _parts[serviceType] = Build(serviceType);
        }

        public object? ResolveNamed(string name) => services.ResolveNamed(name);

        public bool IsRegistered(Type serviceType) => services.IsRegistered(serviceType);

        public IReadOnlyCollection<Type> ServiceTypes => services.ServiceTypes;

        public IReadOnlyCollection<string> Names => services.Names;
    }
}
