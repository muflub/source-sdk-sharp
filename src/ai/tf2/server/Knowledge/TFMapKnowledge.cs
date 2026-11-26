using System.Collections.Immutable;
using SourceSharp.Ai.Server.Nav;
using TF2.Server.Entities;
using TF2.Server.Entities.Items;
using TF2.Server.Entities.Rooms;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// What a TF2 map is to a player who knows the game, before playing (plan_bots_teams_tf2 C.7, P6
/// step 5), from the entity lump the navigation read: the health and ammo packs by size (each back
/// <see cref="TFPowerup.ItemRespawnTime"/> after it is taken), each team's spawns
/// (<c>info_player_teamspawn</c> by its team), and each team's respawn rooms and resupply cabinets
/// as their volumes; the death pits (a <c>trigger_hurt</c> that kills, <see cref="Kills"/>) as hurt zones; and the classes' bodies it is given (<see cref="TFKnowledge.Bodies"/>). The objectives move, so they ride every view (<see cref="TFObjectiveFeed"/>),
/// not this.
/// </summary>
public static class TFMapKnowledge
{
    /// <summary>A pack's size, as its classname says; <see cref="ItemKind.Other"/> for anything else.</summary>
    public static ItemKind KindOf(string className) => className switch
    {
        HealthKitSmall.SmallClass => ItemKind.HealthSmall,
        HealthKitMedium.MediumClass => ItemKind.HealthMedium,
        HealthKit.MapClass => ItemKind.HealthLarge,
        AmmoPackSmall.SmallClass => ItemKind.AmmoSmall,
        AmmoPackMedium.MediumClass => ItemKind.AmmoMedium,
        AmmoPack.MapClass => ItemKind.AmmoLarge,
        _ => ItemKind.Other,
    };

    public static MapKnowledge Build(string map, MapEntities lump, WeaponKnowledge weapons, AbilityTable abilities, BodyKnowledge bodies)
    {
        var items = ImmutableArray.CreateBuilder<ItemSpawn>();
        var spawns = ImmutableArray.CreateBuilder<TeamSpawn>();
        var zones = ImmutableArray.CreateBuilder<TeamZone>();
        foreach (MapEntity e in lump.All)
        {
            if (KindOf(e.ClassName) is var kind and not ItemKind.Other)
                items.Add(new ItemSpawn(items.Count, e.ClassName, kind, e.Origin, TFPowerup.ItemRespawnTime));
            else if (e.ClassName == InfoPlayerTeamSpawn.MapClass)
                spawns.Add(new TeamSpawn(e.Team, e.Origin));
            else if (ZoneOf(e.ClassName) is { } zone && e.Mins is { } mins && e.Maxs is { } maxs)
                zones.Add(new TeamZone(e.Team, zone, mins, maxs));
            else if (Kills(in e) && e.Mins is { } hurtMins && e.Maxs is { } hurtMaxs)
                zones.Add(new TeamZone(0, ZoneKind.Hurt, hurtMins, hurtMaxs));
        }

        return new MapKnowledge(map, items.ToImmutable(), [], weapons, spawns.ToImmutable(), abilities, teamZones: zones.ToImmutable(), bodies: bodies);
    }

    /// <summary>
    /// A <c>trigger_hurt</c> hurts this much a second or more: a full-health Heavy's 300 within the second its two half-second
    /// hits take (SourceSharp's TriggerHurt, stock's HurtThink: half a second's damage every half second). Death pits ask far more
    /// (cp_foundry's are 20,000).
    /// </summary>
    public const float LethalHurtPerSecond = 300f;

    /// <summary>The trigger's classname and keys as the map's lump holds them (TriggerHurtVars: "damage", 20 by default).</summary>
    public const string TriggerHurtClass = "trigger_hurt", DamageKey = "damage";

    /// <summary>Whether <paramref name="e"/> is a <c>trigger_hurt</c> that kills (<see cref="LethalHurtPerSecond"/>) and starts enabled.</summary>
    public static bool Kills(in MapEntity e)
    {
        if (e.ClassName != TriggerHurtClass || e.Int("StartDisabled") != 0 || e.Int("startdisabled") != 0)
            return false;
        float damage = float.TryParse(e.Get(DamageKey), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 20f;
        return damage >= LethalHurtPerSecond;
    }

    private static ZoneKind? ZoneOf(string className) => className switch
    {
        FuncRespawnRoom.MapClass => ZoneKind.RespawnRoom,
        FuncRegenerate.MapClass => ZoneKind.Resupply,
        _ => null,
    };
}
