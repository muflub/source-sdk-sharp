using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// Every networked type this side declares, described once and kept: the
/// client's counterpart of the server's NetSchemaTypes, for the dev API's
/// nettables and entity routes. One per client composition, from the container.
/// </summary>
public sealed class ClientNetSchemaTypes(INetTiers tiers, Func<IEnumerable<StandaloneNetState>> declared)
{
    private IReadOnlyList<NetSchemaType>? _all;

    /// <summary>The described types, by class name.</summary>
    public IReadOnlyList<NetSchemaType> All
        => _all ??= NetSchema.Describe(tiers, Array.Empty<EntityLayout>(), declared());
}
