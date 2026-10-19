using Debug.Api.Client.Interfaces;
using SourceSharp.Client.MaterialProxies;

namespace SourceSharp.Client;

/// <summary><see cref="IDebugClientProxies"/> over the material proxy host (lane c5-proxies).</summary>
public sealed class ClientDebugProxies(IMaterialProxyReadings host) : IDebugClientProxies
{
    public DebugClientProxies Read(string filter)
    {
        bool Matches(string name, string material)
            => filter.Length == 0
               || name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || material.Contains(filter, StringComparison.OrdinalIgnoreCase);

        var names = host.ReadNames();
        var proxies = host.Read();
        return new DebugClientProxies(
            names.Count, host.UnknownBinds, host.DroppedOps,
            names.Where(n => Matches(n.Name, ""))
                 .Select(n => new DebugClientProxyName(n.Name, n.Live, n.Binds, n.BindsLastFrame, n.Unanswered))
                 .ToArray(),
            proxies.Where(p => Matches(p.Name, p.Material))
                   .Select(p => new DebugClientProxy(p.Handle, p.Name, p.Material, p.Initialised, p.Binds, p.LastOutput))
                   .ToArray());
    }
}
