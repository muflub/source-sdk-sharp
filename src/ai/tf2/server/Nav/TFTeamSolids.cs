using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// TF's respawn room visualizers as <see cref="TeamSolid"/>s, from the map's entities
/// (plan_bots_teams_tf2 C.7), which <see cref="TFNavProfile"/> publishes as its
/// <see cref="INavProfile.TeamSolids"/>. A visualizer is solid to the enemy of its room's team
/// (stock's <c>CFuncRespawnRoomVisualizer::ShouldCollide</c>, the mod's
/// <c>FuncRespawnRoomVisualizer</c>) unless its <c>solid_to_enemies</c> is 0; its room is the
/// <c>func_respawnroom</c> its <c>respawnroomname</c> names, whose team is its <c>TeamNum</c>
/// or, with none, that of an enabled team spawn inside it (stock's <c>RoundActivate</c>). The
/// enemy sees it drawn in the room's colour, so a bot knows it as a player does. A visualizer
/// with no room, or a room with no team, is solid to nobody.
/// </summary>
public static class TFTeamSolids
{
    public const string Visualizer = "func_respawnroomvisualizer", Room = "func_respawnroom", TeamSpawn = "info_player_teamspawn";

    public static IReadOnlyList<TeamSolid> Read(MapEntities entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var rooms = new Dictionary<string, MapEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (MapEntity e in entities.All)
        {
            if (e.ClassName == Room && e.Name is { Length: > 0 } name && e.Mins is not null)
                rooms.TryAdd(name, e);
        }

        var solids = new List<TeamSolid>();
        foreach (MapEntity v in entities.All)
        {
            if (v.ClassName != Visualizer || v.Mins is not { } mins || v.Maxs is not { } maxs || v.Int("solid_to_enemies", 1) == 0)
                continue;
            if (v.Get("respawnroomname") is not { Length: > 0 } roomName || !rooms.TryGetValue(roomName, out MapEntity room))
                continue;
            int team = TeamOf(room, entities);
            if (team >= TeamRule.FirstGameTeam)
                solids.Add(new TeamSolid(mins, maxs, team));
        }

        return solids;
    }

    private static int TeamOf(MapEntity room, MapEntities entities)
    {
        int team = room.Int("TeamNum");
        if (team != 0)
            return team;
        AiVector lo = room.Mins!.Value, hi = room.Maxs!.Value;
        foreach (MapEntity s in entities.All)
        {
            if (s.ClassName == TeamSpawn && s.Int("StartDisabled") == 0 && s.Int("TeamNum") >= TeamRule.FirstGameTeam
                && s.Origin.X >= lo.X && s.Origin.X <= hi.X && s.Origin.Y >= lo.Y && s.Origin.Y <= hi.Y && s.Origin.Z >= lo.Z && s.Origin.Z <= hi.Z)
                return s.Int("TeamNum");
        }

        return 0;
    }
}
