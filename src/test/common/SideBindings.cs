using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// An engine area's implementation as the side registers it
/// (plan_shim_areas_split phase 6): <c>EngineServices</c> builds it over the
/// side and a composition holding what it takes by constructor, and the fact
/// holds it as its interface. A test of an implementation never names its
/// class, so the fact covers the registration too, and survives a change to
/// what the implementation takes.
/// </summary>
internal static class SideBindings
{
    /// <summary>
    /// The binding registered for <typeparamref name="T"/> over
    /// <paramref name="side"/>, built over a composition holding the side's
    /// engine interfaces and model caches and <paramref name="composition"/>, each under its own type.
    /// </summary>
    public static T Of<T>(EngineSide side, params object[] composition) where T : class
    {
        var services = new ServiceContainer();
        // What the side's bindings are built over, as AddTo registers it first.
        EngineServices.AddBindingServices(services, side);
        foreach (object part in composition)
            services.AddInstance(part.GetType(), part);
        return (T)EngineServices.BindingFor(typeof(T), side, services);
    }
}
