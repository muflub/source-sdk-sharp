using System;
using System.Collections.Generic;

namespace SourceSharp.Client.MaterialProxies;

/// <summary>
/// Named proxies a mod adds to this client, asked by <see cref="MaterialProxyFactory"/>
/// after its own table: the way TF2's client adds <c>ModelGlowColor</c> and its
/// fellows beside the base proxies its client.dll compiled in.
///
/// <para>
/// A provider answers <see cref="Create"/> for the names it declares in
/// <see cref="Names"/> and null for everything else. A name the core's table also
/// defines, or that another provider defines, is refused the first time the
/// providers are asked, naming both definers — a silently shadowed name would make
/// a material's answer depend on assembly load order, the same order-dependence a
/// mod contract the core already registered refuses.
/// </para>
/// </summary>
public interface IMaterialProxyProvider
{
    /// <summary>The names this provider answers.</summary>
    IReadOnlyCollection<string> Names { get; }

    /// <summary>A fresh proxy for <paramref name="name"/>, or null for a name not this provider's.</summary>
    MaterialProxy? Create(string name);
}
