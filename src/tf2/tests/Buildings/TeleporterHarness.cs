using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;

namespace TF2.Tests.Buildings;

/// <summary>
/// The level 1 pair as the teleporter facts meet it: one engineer's entrance
/// and exit, both built and thinking, on a stubbed world.
/// </summary>
internal static class TeleporterHarness
{
    /// <summary>One engineer's entrance and exit, both built and thinking at the built hour.</summary>
    public static (BuildingsRig Rig, TFTeleporter Entrance, TFTeleporter Exit, EntityHandle Builder) Pair(TFTeam team = TFTeam.Red)
    {
        var rig = new BuildingsRig();
        rig.StubWorld();
        EntityHandle builder = rig.AddBuilder(team);
        var entrance = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);
        var exit = rig.Spawn<TFTeleporter>(TFTeleporter.MapClass);

        foreach (var (end, mode) in new[] { (entrance, 0), (exit, 1) })
        {
            end.StartPlacement(builder);
            end.SetObjectMode(mode);
            end.StartBuilding();
        }

        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Teleporter)!.BuildTime;
        entrance.OnThink();   // the base think's last tick for each end
        exit.OnThink();

        return (rig, entrance, exit, builder);
    }

    /// <summary>The pair thinks together at its own fast beat, the clock landed exactly at the end.</summary>
    public static void Tick(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit, float seconds)
    {
        int steps = (int)(seconds / TFTeleporter.TeleporterThinkDelay + 0.5f);
        float start = rig.Clock.Now, end = start + seconds;
        for (int step = 1; step <= steps; step++)
        {
            rig.Clock.Now = step == steps ? end : start + step * TFTeleporter.TeleporterThinkDelay;
            entrance.OnThink("TeleporterThink");
            exit.OnThink("TeleporterThink");
        }
    }

    /// <summary>A friendly of the team asked who is standing, and so may be booked and sent.</summary>
    public static EntityHandle AStandingRider(BuildingsRig rig, TFTeam team = TFTeam.Red)
    {
        (EntityHandle handle, _) = rig.Players.Add(team);
        rig.Engine.Returns("Entities.GetVelocity", Vector.Zero);
        return handle;
    }
}
