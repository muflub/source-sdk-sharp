namespace SourceSharp.Client.Interface;

/// <summary>One proxy name the client registers: how many the material system holds, and their binds.</summary>
/// <param name="BindsLastFrame">Binds in the last whole frame the client bound any proxy in.</param>
/// <param name="Unanswered">Binds that reached an entity fact this build cannot read (ToggleTexture's frame index, Pupil's eye lighting).</param>
public sealed record DebugClientProxyName(string Name, int Live, long Binds, int BindsLastFrame, int Unanswered);

/// <summary>One proxy the material system holds: its material, whether its Init accepted it, and what its last bind set.</summary>
/// <param name="LastOutput">The last bind's IMaterialVar setter calls, in order ("Float 0 0.5", "Matrix 2 ...").</param>
public sealed record DebugClientProxy(int Handle, string Name, string Material, bool Initialised, long Binds,
                                      IReadOnlyList<string> LastOutput);

/// <summary>
/// The material proxies, as <c>/client/{slot}/proxies</c> reports them
/// (plan_client_novalve §4.4): the registered names with their binds per
/// frame, and each live proxy's last output by material.
/// </summary>
public sealed record DebugClientProxies(int Registered, long UnknownBinds, long DroppedOps,
                                        IReadOnlyList<DebugClientProxyName> Names,
                                        IReadOnlyList<DebugClientProxy> Proxies);

/// <summary>The client's material proxies (lane c5-proxies). Game thread.</summary>
public interface IDebugClientProxies
{
    /// <summary>All of them; with a filter, only the proxies whose name or material contains it.</summary>
    DebugClientProxies Read(string filter);
}
