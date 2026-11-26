using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Server;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>The level's own entities, as a bot knows its map's placements: <see cref="TFPlacedEntities"/>.</summary>
public interface ITFLevelEntities
{
    /// <summary>The placements the level's entities make, read now.</summary>
    MapEntities Read();
}

/// <summary>
/// The placements a TF2 map makes that its bots know before playing (P6 step 5), read from the
/// level's own entities as it comes up: the team spawns, the packs, the respawn rooms and the
/// resupply cabinets, the no-build areas and the hurt triggers, each with its team and, for a brush, its volume; and the capture areas, each
/// with the point it names, and the control points by name (lane Ge). A shipped TF2 map stores its
/// entity lump LZMA-compressed, which the lump reader (<see cref="MapEntities.Read"/>) cannot
/// read; the level's entities are the same placements, spawned from it.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFLevelEntities)])]
public sealed class TFPlacedEntities(IEntityDirectory directory, IEngineEntities entities) : ITFLevelEntities
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
        {
            all.Add(Point(InfoPlayerTeamSpawn.MapClass, spawn.Handle, spawn.Team) with
            {
                Keys = [Key("TeamNum", spawn.Team), Key("StartDisabled", spawn.Disabled ? 1 : 0)],
            });
        }

        foreach (string pack in Packs())
        {
            foreach (ScriptedEntity e in directory.OfClass(pack))
                all.Add(Point(pack, e.Handle, 0));
        }

        foreach (FuncRespawnRoom room in directory.OfClass(FuncRespawnRoom.MapClass).OfType<FuncRespawnRoom>())
        {
            all.Add(Brush(FuncRespawnRoom.MapClass, room.Handle, room.Team) with
            {
                Name = entities.GetName(room.Handle),
                Keys = [Key("TeamNum", room.Team)],
            });
        }

        // The visualizers, with the keys TFTeamSolids reads: the room each names and whether it
        // is solid to the room's enemies.
        foreach (FuncRespawnRoomVisualizer v in directory.OfClass(FuncRespawnRoomVisualizer.MapClass).OfType<FuncRespawnRoomVisualizer>())
        {
            all.Add(Brush(FuncRespawnRoomVisualizer.MapClass, v.Handle, v.Team) with
            {
                Keys = [new("respawnroomname", v.VisualizerVars.RespawnRoomName), Key("solid_to_enemies", v.SolidToEnemies ? 1 : 0)],
            });
        }

        foreach (FuncRegenerate cabinet in directory.OfClass(FuncRegenerate.MapClass).OfType<FuncRegenerate>())
            all.Add(Brush(FuncRegenerate.MapClass, cabinet.Handle, cabinet.RegenerateVars.TeamNum));

        // The no-build areas, with the team each denies and whether it is on now and which buildings it lets in
        // (TFMapKnowledge's NoBuild zones).
        foreach (FuncNoBuild area in directory.OfClass(FuncNoBuild.MapClass).OfType<FuncNoBuild>())
        {
            all.Add(Brush(FuncNoBuild.MapClass, area.Handle, area.Team) with
            {
                Keys =
                [
                    Key("TeamNum", area.Team), Key("StartDisabled", area.Active ? 0 : 1), Key(TFMapKnowledge.AllowSentryKey, area.Vars.AllowSentry ? 1 : 0),
                    Key(TFMapKnowledge.AllowDispenserKey, area.Vars.AllowDispenser ? 1 : 0), Key(TFMapKnowledge.AllowTeleportersKey, area.Vars.AllowTeleporters ? 1 : 0),
                ],
            });
        }

        // The hurt triggers, with the keys TFMapKnowledge.Kills reads (their damage a second and whether they start off): a lethal
        // one is a death pit (S12's hurt zones). Read once at the level's start, as the no-build areas are.
        foreach (TriggerHurt hurt in directory.OfClass(TFMapKnowledge.TriggerHurtClass).OfType<TriggerHurt>())
        {
            all.Add(Brush(TFMapKnowledge.TriggerHurtClass, hurt.Handle, 0) with
            {
                Keys =
                [
                    new(TFMapKnowledge.DamageKey, hurt.TriggerHurtVars.Damage.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    Key("StartDisabled", hurt.Vars.StartDisabled ? 1 : 0),
                ],
            });
        }

        // The capture areas with the point each names, and the points by name: an area whose point the level does not
        // place is a course's end (TFCourseEnds, lane Ge).
        foreach (TriggerAreaCapture area in directory.OfClass(TriggerAreaCapture.MapClass).OfType<TriggerAreaCapture>())
            all.Add(Brush(TriggerAreaCapture.MapClass, area.Handle, 0) with { Keys = [new(TFCourseEnds.PointKey, area.AreaVars.CapPointName)] });
        foreach (TeamControlPoint point in directory.OfClass(TeamControlPoint.MapClass).OfType<TeamControlPoint>())
            all.Add(Point(TeamControlPoint.MapClass, point.Handle, 0) with { Name = entities.GetName(point.Handle) });
        return new MapEntities(all, null);
    }

    private static KeyValuePair<string, string> Key(string key, int value) => new(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
