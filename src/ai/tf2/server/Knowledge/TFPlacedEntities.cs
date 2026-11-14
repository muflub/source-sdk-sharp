using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Server;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// The placements a TF2 map makes that its bots know before playing (P6 step 5), read from the
/// level's own entities as it comes up: the team spawns, the packs, the respawn rooms and the
/// resupply cabinets, each with its team and, for a brush, its volume. A shipped TF2 map stores its
/// entity lump LZMA-compressed, which the lump reader (<see cref="MapEntities.Read"/>) cannot
/// read; the level's entities are the same placements, spawned from it.
/// </summary>
[SideService(Side.Server)]
public sealed class TFPlacedEntities(IEntityDirectory directory, IEngineEntities entities)
{
    /// <summary>The pack classes, by size: what <see cref="TFMapKnowledge.KindOf"/> names.</summary>
    private static string[] Packs() =>
    [
        HealthKitSmall.SmallClass, HealthKitMedium.MediumClass, HealthKit.MapClass,
        AmmoPackSmall.SmallClass, AmmoPackMedium.MediumClass, AmmoPack.MapClass,
    ];

    public MapEntities Read()
    {
        var all = new List<MapEntity>();
        foreach (InfoPlayerTeamSpawn spawn in directory.OfClass(InfoPlayerTeamSpawn.MapClass).OfType<InfoPlayerTeamSpawn>())
            all.Add(Point(InfoPlayerTeamSpawn.MapClass, spawn.Handle, spawn.Team));
        foreach (string pack in Packs())
        {
            foreach (ScriptedEntity e in directory.OfClass(pack))
                all.Add(Point(pack, e.Handle, 0));
        }

        foreach (FuncRespawnRoom room in directory.OfClass(FuncRespawnRoom.MapClass).OfType<FuncRespawnRoom>())
            all.Add(Brush(FuncRespawnRoom.MapClass, room.Handle, room.Team));
        foreach (FuncRegenerate cabinet in directory.OfClass(FuncRegenerate.MapClass).OfType<FuncRegenerate>())
            all.Add(Brush(FuncRegenerate.MapClass, cabinet.Handle, cabinet.RegenerateVars.TeamNum));
        return new MapEntities(all, null);
    }

    private MapEntity Point(string className, EntityHandle e, int team)
    {
        Vector o = entities.GetOrigin(e);
        return new MapEntity(className, null, new AiVector(o.X, o.Y, o.Z), null, null, Team: team);
    }

    /// <summary>A brush's volume in the world: its origin and its bounds (a brush entity is not turned).</summary>
    private MapEntity Brush(string className, EntityHandle e, int team)
    {
        Vector o = entities.GetOrigin(e);
        (Vector mins, Vector maxs) = entities.GetBounds(e);
        var origin = new AiVector(o.X, o.Y, o.Z);
        return new MapEntity(className, null, origin, origin + new AiVector(mins.X, mins.Y, mins.Z), origin + new AiVector(maxs.X, maxs.Y, maxs.Z), Team: team);
    }
}
