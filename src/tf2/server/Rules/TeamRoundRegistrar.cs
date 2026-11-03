using SourceSharp.Abi;

namespace TF2.Server.Rules;

/// <summary>The round machine's two chains (<see cref="ITeamRoundListener"/>, <see cref="ITimerExpiryVeto"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class TeamRoundRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry)
    {
        registry.AddHooks<ITeamRoundListener>();
        registry.AddHooks<ITimerExpiryVeto>();
    }
}
