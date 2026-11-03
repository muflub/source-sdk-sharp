using SourceSharp.Abi;

namespace TF2.Server.Player;

/// <summary>The TF player's chain (<see cref="ITFConditionHook"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFPlayerRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFConditionHook>();
}
