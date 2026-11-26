using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;

namespace SourceSharp.Ai.TF2;

/// <summary>
/// The TF2 AI binding's presence in a level (plan_bots_teams_tf2 P6): it says it composed, and
/// what the bots know of the level from the mod's tables and the map (the classes, the weapons,
/// the abilities, the team spawns, the packs and the teams' rooms), once per level; and, the first
/// tick the HUD shows any, the objectives every bot's view carries. A live boot reads these lines.
/// </summary>
[SideService(Side.Server)]
public sealed class TFAiService(ILog log, TFBotGame game, ServerAiSession ai, TFObjectiveFeed objectives) : IGameLifecycle, IFrameLifecycle
{
    /// <summary>After the session built the level's knowledge (ServerAiSession, First + 450), beside the HL2MP binding's slot (never composed with it).</summary>
    public int LifecycleOrder => ModStages.First + 510;

    /// <summary>After the senses (First + 600) and the AI's other frame members, so the feed has refreshed.</summary>
    public int FrameOrder => ModStages.First + 650;

    /// <summary>How many levels this service has seen come up.</summary>
    public int Levels { get; private set; }

    private bool _toldObjectives;

    public void OnLevelInitPostEntity(string mapName)
    {
        Levels++;
        _toldObjectives = false;
        var line = new System.Text.StringBuilder($"[SourceSharp/Ai] TF2 AI composed for {mapName}");
        if (ai.Session.World?.Map is { } map)
        {
            line.Append(CultureInfo.InvariantCulture,
                $": {map.TeamSpawns.Count(s => s.Team == 2)} red and {map.TeamSpawns.Count(s => s.Team == 3)} blue spawns, {map.Items.Length} packs, "
                + $"{map.TeamZones.Count(z => z.Kind == ZoneKind.RespawnRoom)} respawn rooms, {map.TeamZones.Count(z => z.Kind == ZoneKind.Resupply)} resupply cabinets, "
                + $"{map.TeamZones.Count(z => z.Kind == ZoneKind.Hurt)} death pits, {map.TeamZones.Count(z => z.Kind == ZoneKind.NoBuild)} no-build areas, "
                + $"{map.Weapons.All.Length} weapons ({map.Weapons.All.Count(w => w.ProjectileGravity > 0f)} fall, {map.Weapons.All.Count(w => w.ChargeSeconds > 0f)} charge), "
                + $"{map.Abilities.Count} abilities");
        }

        line.Append("; classes ").AppendJoin(", ", game.Classes().Select(c => string.Create(CultureInfo.InvariantCulture, $"{c.Class} {c.MaxSpeed:F0}/{c.MaxHealth}")));
        log.Msg(line.Append('\n').ToString());
    }

    public void OnPostThink(float frameTime)
    {
        if (_toldObjectives || objectives.Current.All.IsEmpty)
            return;
        _toldObjectives = true;
        log.Msg("[SourceSharp/Ai] TF2 AI: the HUD shows " + string.Join("; ", objectives.Current.All.Select(o => string.Create(CultureInfo.InvariantCulture,
            $"{o.Kind} {o.Id} team {o.OwnerTeam} {o.State} at ({o.Position.X:F0} {o.Position.Y:F0} {o.Position.Z:F0})"))) + "\n");
    }
}
