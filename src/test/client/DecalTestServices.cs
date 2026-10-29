using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Tests;

/// <summary>
/// A test container the client's decal players can be built from: the decal
/// gates they take (<see cref="IDecalSettings"/>, the engine's r_decals and
/// r_drawmodeldecals) both on, as the engine's defaults are, normal violence,
/// and player sprays that draw nothing (<see cref="PlayerSpraysTests"/> is
/// where they are asked).
/// </summary>
internal static class DecalTestServices
{
    public static ServiceContainer WithDecalsOn(this ServiceContainer services)
    {
        services.AddInstance<IDecalSettings>(DecalSettings.On());
        services.AddInstance<IPlayerSprays>(new NoSprays());
        services.AddInstance<Client.Effects.IClientLowViolence>(new NormalViolence());
        return services;
    }

    private sealed class NormalViolence : Client.Effects.IClientLowViolence
    {
        public bool IsLowViolence => false;
    }

    private sealed class NoSprays : IPlayerSprays
    {
        public SprayOutcome Spray(Vector position, int player, int entity) => SprayOutcome.Refused;
    }
}
