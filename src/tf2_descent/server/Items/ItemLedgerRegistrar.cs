using SourceSharp.Abi;

namespace TF2.Descent.Server.Items;

/// <summary>
/// The item ledger's registrations an attribute cannot say: the dispatch for
/// <see cref="IItemLedgerHook"/>, and <see cref="IHostItems"/> as the one host link
/// (<see cref="HostLink"/> answers it beside <see cref="IHostLink"/>, the same instance).
/// </summary>
[SideService(Side.Server)]
public sealed class ItemLedgerRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<IItemLedgerHook>();
        registry.AddFactory<IHostItems>(services => services.Require<HostLink>());
    }
}
