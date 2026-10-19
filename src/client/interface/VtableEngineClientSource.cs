namespace SourceSharp.Abi;

/// <summary>
/// This composition's engine client, acquired when asked, with the reason when
/// the engine refuses it.
///
/// <para>
/// The seam for code that reports the refusal itself (<c>cs_vtable_client</c>):
/// the services that only need the client take a <c>Func&lt;IVtableEngineClient?&gt;</c>
/// from the root instead. Either way the acquire goes through the
/// composition's own engine (plan_abi_hotreload D8), and the logic never names
/// <c>ModuleEngine</c> (plan_shim_areas_split phase 8).
/// </para>
///
/// <para>
/// Not named <c>IEngine...</c>: <c>EngineServices</c> enrols every
/// <c>IEngine*</c> interface beside a same-named class as an engine area,
/// built over the side, and this one is built over the composition's engine
/// by its root.
/// </para>
/// </summary>
public interface IVtableEngineClientSource
{
    /// <summary>The engine client, or null with <paramref name="why"/> the engine's reason.</summary>
    IVtableEngineClient? Acquire(out string why);
}
