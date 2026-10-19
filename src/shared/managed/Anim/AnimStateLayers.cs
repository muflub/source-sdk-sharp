using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceSharp.Shared.Anim;

/// <summary>
/// The side's <see cref="IAnimStateLayer"/> composition: every layer the
/// side's container holds, plus the core's <see cref="Hl2PostureLayer"/> over
/// the player being posed, in one <see cref="AnimStateLayerChain"/> (ordered by
/// <see cref="IAnimStateLayer.LayerOrder"/>, ties refused).
///
/// <para>
/// Both sides register one, built the same way from the same assembly set, so
/// the client's copy of a player asks exactly the layers the server's copy
/// asked: an animation layer present on one side only is a pose the client
/// predicts and the server never chose.
/// </para>
///
/// <para>
/// A mod's layer is a container service with the PLAIN
/// <c>[SideService(Side.X)]</c> mark, found by type: a class that claims the
/// contract with <c>As = [typeof(IAnimStateLayer)]</c> owns it for the side,
/// and a second mod doing the same is a startup error. A layer is one object
/// per side shared by every player, so it reads the player from
/// <see cref="AnimStateContext.Target"/>, never from a field.
/// </para>
///
/// <para>
/// The container is asked once, lazily, on the first chain: after the mod
/// assemblies have registered, which is before any player animates.
/// </para>
/// </summary>
public sealed class AnimStateLayers
{
    private readonly Func<IEnumerable<IAnimStateLayer>> _collect;
    private IAnimStateLayer[]? _registered;

    /// <param name="collect">The side's registered layers (the modules pass the container's <c>ResolveAll</c>).</param>
    public AnimStateLayers(Func<IEnumerable<IAnimStateLayer>> collect)
    {
        _collect = collect ?? throw new ArgumentNullException(nameof(collect));
    }

    /// <summary>The container's layers, as first collected (registration order; the chain sorts).</summary>
    public IReadOnlyList<IAnimStateLayer> Registered => _registered ??= _collect().ToArray();

    /// <summary>
    /// The chain for one player: the registered layers and the core's HL2
    /// layer over <paramref name="target"/>. With nothing registered it is
    /// the HL2 layer alone, the chain every player had before mods could add
    /// one.
    /// </summary>
    public AnimStateLayerChain ChainFor(IAnimTarget target)
        => new(Registered.Append(new Hl2PostureLayer(target)));

    /// <summary>
    /// The chain for a player built without the composition (hand-built
    /// tests): the HL2 layer alone.
    /// </summary>
    public static AnimStateLayerChain CoreOnly(IAnimTarget target)
        => new(new IAnimStateLayer[] { new Hl2PostureLayer(target) });
}
