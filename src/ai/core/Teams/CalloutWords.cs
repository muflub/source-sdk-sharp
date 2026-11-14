using System.Text;

namespace SourceSharp.Ai;

/// <summary>
/// A callout as a line of team chat (plan_bots_teams_tf2 B.4): "Enemy at the east
/// end", "2 enemies by the health kit", "Regroup in the middle", "Push now!". The
/// place in words a player can use (<see cref="ChatPlaces"/>), never a position.
/// Templated, so it needs no model: a human on the team hears it with chat off.
/// </summary>
public static class CalloutWords
{
    /// <summary>The line for <paramref name="say"/>, or null for a callout with nothing to say in words.</summary>
    public static string? Line(in SayIntent say, ChatPlaces places, MapKnowledge map)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(map);
        return say.Kind switch
        {
            CalloutKind.Enemy => (say.Count > 1 ? $"{say.Count} enemies " : "Enemy ") + Where(say.Place, places, map),
            CalloutKind.Rally => "Regroup " + Where(say.Place, places, map),
            CalloutKind.Push => "Push now!",
            CalloutKind.GoingAround => "Going around",
            _ => null,
        };
    }

    /// <summary>A pickup a player can be pointed to by name; anything else is no landmark.</summary>
    public static string? PickupName(ItemSpawn item) => item.ClassName switch
    {
        "item_healthkit" => "the health kit",
        "item_battery" => "the battery",
        "item_healthcharger" => "the health charger",
        "item_suitcharger" => "the suit charger",
        _ when item.ClassName.StartsWith("weapon_", StringComparison.Ordinal) => "the " + item.ClassName["weapon_".Length..],
        _ => null,
    };

    private static string Where(AiVector at, ChatPlaces places, MapKnowledge map)
    {
        // A callout names the pickup itself as its place (Hl2mpDecider.CalloutPlace).
        foreach (ItemSpawn item in map.Items)
        {
            if (item.Position.DistanceTo(at) < 1f && PickupName(item) is { } name)
                return "by " + name;
        }

        ChatPlace p = places.Describe(at, PickupName);
        if (p.Near.Length > 0)
            return "by " + p.Near;
        var b = new StringBuilder(p.End == "middle" ? "in the middle" : "at the " + p.End);
        if (p.Side.Length > 0)
            b.Append(" on the ").Append(p.Side).Append(" side");
        if (p.Floor.Length > 0)
            b.Append(", ").Append(p.Floor);
        return b.ToString();
    }
}
