using SourceSharp.Abi;

namespace TF2.Server.Player;

/// <summary>The TF player's chains (<see cref="ITFConditionHook"/>, the self-blast jump's
/// <see cref="TF2.Server.Projectiles.ITFSelfBlastHook"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFPlayerRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<ITFConditionHook>();
        registry.AddHooks<ITFWeaponEquipHook>();
        registry.AddHooks<ITFPlayerAttributeSource>();
        registry.AddHooks<TF2.Server.Projectiles.ITFSelfBlastHook>();
    }
}
