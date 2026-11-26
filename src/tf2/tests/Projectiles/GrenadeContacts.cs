using SourceSharp.Abi;
using TF2.Shared;
using TF2.Tests.Buildings;

namespace TF2.Tests.Projectiles;

/// <summary>
/// What a grenade's contact reads, answered as the server answers it: <c>Players.GetTeam</c> is a player's team and -1 for
/// anything else (EngineBinding.Server's GetTeam), and a building's team is its own.
/// </summary>
internal static class GrenadeContacts
{
    /// <summary>The named players are players on their teams; every other handle is no player and reads team -1.</summary>
    public static void ServerTeams(RecordingEngine engine, params (EntityHandle Player, TFTeam Team)[] players)
    {
        engine.ReturnsEachCall("Entities.IsPlayer", () => TeamOf(engine.Calls[^1].Args[0], players) is not null);
        engine.ReturnsEachCall("Players.GetTeam", () => TeamOf(engine.Calls[^1].Args[0], players) ?? -1);
    }

    private static int? TeamOf(object? arg, (EntityHandle Player, TFTeam Team)[] players)
    {
        foreach ((EntityHandle player, TFTeam team) in players)
        {
            if (arg is EntityHandle h && h == player)
                return (int)team;
        }

        return null;
    }

    /// <summary>A building placed by a builder of <paramref name="team"/>, on a rig of its own.</summary>
    public static TestBuilding PlacedBuilding(TFTeam team)
    {
        var rig = new BuildingsRig();
        EntityHandle builder = rig.AddBuilder(team);
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        return building;
    }
}
