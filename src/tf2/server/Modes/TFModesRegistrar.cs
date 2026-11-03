using SourceSharp.Abi;

namespace TF2.Server.Modes;

/// <summary>The mode hooks' chain (<see cref="ITFModeHook"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TFModesRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ITFModeHook>();
}
