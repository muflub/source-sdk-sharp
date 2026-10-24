namespace SourceSharp.Abi;

/// <summary>
/// Whether this composition's engine serves <c>IVEngineClient</c>, with the
/// reason when it refuses: the seam for code that reports the refusal itself
/// (<c>cs_vtable_client</c>). It asks the engine every time, and answers;
/// the calls themselves go through the registered <see cref="IFwdEngineClient"/>,
/// never null, like every other service's. The asking goes through the
/// composition's own engine (plan_abi_hotreload D8), and the logic never
/// names <c>ModuleEngine</c> (plan_shim_areas_split phase 8).
///
/// <para>
/// Not named <c>IEngine...</c>: <c>EngineServices</c> enrols every
/// <c>IEngine*</c> interface beside a same-named class as an engine area,
/// built over the side, and this one is built over the composition's engine
/// by its root.
/// </para>
/// </summary>
public interface IFwdEngineClientProbe
{
    /// <summary>Whether the engine serves the engine client; false with <paramref name="why"/> the engine's reason.</summary>
    bool Served(out string why);
}
