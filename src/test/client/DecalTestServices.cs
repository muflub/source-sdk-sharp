using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Tests;

/// <summary>
/// A test container the client's decal players can be built from: the decal
/// gates they take (<see cref="IDecalSettings"/>, the engine's r_decals and
/// r_drawmodeldecals) both on, as the engine's defaults are, and player sprays
/// that draw nothing (<see cref="PlayerSpraysTests"/> is where they are asked).
/// </summary>
internal static class DecalTestServices
{
    public static ServiceContainer WithDecalsOn(this ServiceContainer services)
    {
        services.AddInstance<IDecalSettings>(DecalSettings.On());
        services.AddInstance<IPlayerSprays>(new NoSprays());
        return services;
    }

    private sealed class NoSprays : IPlayerSprays
    {
        public SprayOutcome Spray(Vector position, int player, int entity) => SprayOutcome.Refused;
    }
}
