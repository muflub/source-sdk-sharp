using SourceSharp.Tests;

namespace SourceSharp.Ai.Tests.Chat;

/// <summary>A small map with known places, the committed help file, and a director that answers from them.</summary>
internal static class ChatAnswerRig
{
    public static readonly AiVector KitAt = new(-500f, 1800f, 10f);
    public static readonly AiVector RpgAt = new(800f, 1900f, 10f);
    public static readonly AiVector BatteryAt = new(-800f, -1800f, 10f);
    public static readonly IReadOnlySet<string> Good = new HashSet<string>(StringComparer.Ordinal) { "weapon_rpg", "weapon_crossbow", "weapon_ar2", "weapon_357", "weapon_shotgun" };

    public static ChatHelp Help() => ChatHelp.Parse(File.ReadAllText(RepoLayout.Repo("game", "mod_test", "scripts", "ai", "hl2mp_help.json")));

    public const ulong Seed = 0xC4A7;

    /// <summary>Two kits, one RPG, one battery, a grenade by the RPG and two spawns: a map 4,000 units long running north, all one floor.</summary>
    public static MapKnowledge Map()
    {
        var weapons = new WeaponKnowledge([new WeaponFacts(0, "weapon_rpg", 0, 1f, 1f, false, false, 0f)]);
        return new MapKnowledge("dm_testing",
        [
            new ItemSpawn(0, "item_healthkit", ItemKind.Health, KitAt, 30f),
            new ItemSpawn(1, "item_healthkit", ItemKind.Health, new AiVector(500f, -1500f, 10f), 30f),
            new ItemSpawn(2, "weapon_rpg", ItemKind.Weapon, RpgAt, 20f, 0),
            new ItemSpawn(3, "item_battery", ItemKind.Armour, BatteryAt, 30f),
            new ItemSpawn(4, "weapon_frag", ItemKind.Weapon, new AiVector(860f, 1900f, 10f), 20f),
        ],
        [new AiVector(-1000f, -2000f, 0f), new AiVector(1000f, 2000f, 0f)], weapons);
    }

    public static ChatAnswerBook Book(ulong seed = Seed)
    {
        MapKnowledge map = Map();
        return new ChatAnswerBook(Help(), map, new ChatPlaces(map, null), Good, seed);
    }

    public static readonly IReadOnlySet<string> People = new HashSet<string>(StringComparer.Ordinal) { "lodle", "ravi", "pia", "tommy" };

    public static ChatAsked Asked(string text = "", AiVector? seen = null, AiVector? at = null, string? lastKiller = null, int draw = 0)
        => new(text, new ChatScore(3, 2, 2, 4, "Kofi", 5), lastKiller, seen, at, draw);

    /// <summary>What the book answers a line, end to end, or null when it is no question.</summary>
    public static ChatAnswer? Ask(string text, AiVector? seen = null, AiVector? at = null, bool namesABot = false, int draw = 0)
    {
        ChatAnswerBook book = Book();
        return book.Read(text, namesABot, People) is { } topic ? book.Answer(topic, Asked(text, seen, at, draw: draw)) : null;
    }
}
