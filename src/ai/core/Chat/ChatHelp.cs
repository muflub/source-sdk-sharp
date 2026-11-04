using System.Collections.Immutable;
using System.Text.Json;

namespace SourceSharp.Ai;

/// <summary>A thing on a map a player may ask about: the classes it is, what chat calls it, and the words that ask for it.</summary>
public sealed record ChatItem(ImmutableArray<string> Classes, string Say, string Many, string Label, ImmutableArray<string> Words);

/// <summary>One how-to-play answer: the words that ask for it, the answer, and the words a rephrasing of it must keep.</summary>
public sealed record ChatHowTo(string Id, ImmutableArray<string> Words, string Answer, ImmutableArray<string> Must);

/// <summary>
/// What bot chat answers questions from, besides the map itself (plan_hl2mp_ai §3.6, user
/// request 2026-11-03): the words that make a line a question and name its topic, the
/// things on a map, the hand-written how-to-play answers, the shrugs and every template.
/// Read by the binding through the file seam; the core names no game. Every claim in the
/// file is held to the repo by <c>ChatHelpTests</c>.
/// </summary>
public sealed record ChatHelp(
    ImmutableHashSet<string> QuestionWords, ImmutableArray<string> SmallTalk, ImmutableArray<string> WhereWords, ImmutableArray<string> ExistsWords,
    ImmutableArray<string> CountWords, ImmutableArray<string> RespawnWords, ImmutableHashSet<string> PeopleWords, ImmutableArray<string> ScoreWords,
    ImmutableArray<string> OwnScoreWords, ImmutableArray<string> LastKillerWords, ImmutableArray<string> MapWords, ImmutableArray<string> SpawnWords,
    ImmutableArray<string> WeaponListWords, ImmutableArray<string> ItemListWords, ImmutableArray<ChatItem> Items, ImmutableArray<string> AmmoGeneric,
    ImmutableHashSet<string> Loadout, ImmutableArray<string> NotInGame, ImmutableArray<ChatHowTo> HowTo, ImmutableArray<string> Shrugs,
    ImmutableArray<string> PeopleShrugs, ImmutableArray<string> LeadIns, ImmutableHashSet<string> Negations, ImmutableHashSet<string> Agreements, ImmutableHashSet<string> Filler,
    ImmutableDictionary<string, string> Templates, ImmutableDictionary<string, string> Vague)
{
    /// <summary>The help a <c>format: 1</c> JSON file holds, or a throw naming what is wrong.</summary>
    public static ChatHelp Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != 1)
            throw new InvalidDataException("the help file is not format 1");

        static ImmutableArray<string> Array(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetString() ?? "")];
        ImmutableArray<string> Strings(string key) => Array(root.GetProperty(key));
        ImmutableHashSet<string> Set(string key) => [.. Strings(key)];
        ImmutableDictionary<string, string> Map(string key)
        {
            var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty p in root.GetProperty(key).EnumerateObject())
                map[p.Name] = p.Value.GetString() ?? "";
            return map.ToImmutable();
        }

        ImmutableArray<ChatItem> items = [.. root.GetProperty("items").EnumerateArray().Select(i => new ChatItem(
            Array(i.GetProperty("classes")), i.GetProperty("say").GetString() ?? "", i.GetProperty("many").GetString() ?? "", i.GetProperty("label").GetString() ?? "", Array(i.GetProperty("words"))))];
        ImmutableArray<ChatHowTo> howTo = [.. root.GetProperty("help").EnumerateArray().Select(h => new ChatHowTo(
            h.GetProperty("id").GetString() ?? "", Array(h.GetProperty("words")), h.GetProperty("answer").GetString() ?? "", Array(h.GetProperty("must"))))];
        var help = new ChatHelp(Set("questionWords"), Strings("smallTalk"), Strings("whereWords"), Strings("existsWords"), Strings("countWords"), Strings("respawnWords"),
                                Set("peopleWords"), Strings("scoreWords"), Strings("ownScoreWords"), Strings("lastKillerWords"), Strings("mapWords"), Strings("spawnWords"),
                                Strings("weaponListWords"), Strings("itemListWords"), items, Strings("ammoGeneric"), Set("loadout"), Strings("notInGame"), howTo, Strings("shrugs"),
                                Strings("peopleShrugs"), Strings("leadIns"), Set("negations"), Set("agreements"), Set("filler"), Map("templates"), Map("vague"));
        if (help.Shrugs.IsEmpty || help.PeopleShrugs.IsEmpty || help.Items.IsEmpty || help.HowTo.IsEmpty)
            throw new InvalidDataException("the help file has an empty list");
        foreach (string key in (string[])["whereOneNear", "whereOne", "whereManyNear", "whereMany", "vagueOne", "vagueMany", "yesOne", "yesOneExact", "yesMany", "absent", "loadout", "notInGame",
                                          "count", "countOne", "countNone", "respawn", "respawnNone", "ammo", "mapName", "spawns", "weaponList", "itemList", "leaderOther", "leaderSelf",
                                          "ownScore", "lastKiller", "lastKillerNone", "whereLadder"])
        {
            if (!help.Templates.ContainsKey(key))
                throw new InvalidDataException($"the help file has no template '{key}'");
        }

        return help;
    }
}
