using System.Collections.Immutable;
using System.Text.Json;

namespace SourceSharp.Ai;

/// <summary>One skill's two tones: for a bot well above the even spread, and well below it.</summary>
public sealed record ChatTones(string High, string Low);

/// <summary>
/// The phrases <see cref="ChatLineRules"/> reads: what a winner and a loser do not say, what
/// makes a player's line friendly, and what an answer to one may not hold.
/// </summary>
public sealed record ChatLineWords(ImmutableArray<string> WonSaysNot, ImmutableArray<string> LostSaysNot, ImmutableArray<string> Friendly, ImmutableArray<string> Insults);

/// <summary>
/// What bot chat reads from its data file (plan_hl2mp_ai §3.6): the prompts'
/// wording, each trait's tone, the weapons' spoken names, and the output
/// filter's word lists. Read by the binding through the file seam; the core
/// names no game.
/// </summary>
public sealed record ChatWords(ImmutableDictionary<string, string> Prompts, ImmutableDictionary<string, ChatTones> Tones,
                               ImmutableDictionary<string, string> Weapons, ImmutableHashSet<string> Braggable,
                               ImmutableHashSet<string> Banned, ImmutableArray<string> BannedAnywhere, ImmutableArray<string> Chatter,
                               ImmutableHashSet<string> Commands, ImmutableHashSet<string> ConsoleWords, ImmutableHashSet<string> LoneCommands, ImmutableHashSet<string> NotNames,
                               ImmutableHashSet<string> ShortWords, ChatLineWords Line)
{
    /// <summary>The points at or under which a bot talks like a beginner (Easy spends 20), and at or over which like a regular (Hard 50, Expert 60).</summary>
    public const int NoviceSpent = 25, VeteranSpent = 50;

    /// <summary>The words a <c>format: 1</c> JSON file holds, or a throw naming what is wrong.</summary>
    public static ChatWords Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("format", out JsonElement format) || format.GetInt32() != 1)
            throw new InvalidDataException("the chat file is not format 1");

        ImmutableDictionary<string, string> Map(string key)
        {
            var map = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty p in root.GetProperty(key).EnumerateObject())
                map[p.Name] = p.Value.GetString() ?? "";
            return map.ToImmutable();
        }

        string[] Strings(string key) => [.. root.GetProperty(key).EnumerateArray().Select(x => x.GetString() ?? "")];
        ImmutableHashSet<string> Set(string key) => [.. Strings(key)];

        var tones = ImmutableDictionary.CreateBuilder<string, ChatTones>(StringComparer.Ordinal);
        foreach (JsonProperty skill in root.GetProperty("tones").EnumerateObject())
            tones[skill.Name] = new ChatTones(skill.Value.GetProperty("high").GetString() ?? "", skill.Value.GetProperty("low").GetString() ?? "");

        var words = new ChatWords(Map("prompts"), tones.ToImmutable(), Map("weapons"), Set("braggable"), Set("banned"), [.. Strings("bannedAnywhere")],
                                  [.. Strings("chatter")], Set("commands"), Set("consoleWords"), Set("loneCommands"), Set("notNames"), Set("shortWords"),
                                  new ChatLineWords([.. Strings("wonSaysNot")], [.. Strings("lostSaysNot")], [.. Strings("friendly")], [.. Strings("insults")]));
        if (words.Banned.IsEmpty || words.Chatter.IsEmpty || words.Commands.IsEmpty)
            throw new InvalidDataException("the chat file has an empty word list");
        foreach (string key in (string[])["system", "score", "recent", "earlier", "you", "tail", "kill", "death", "suicide", "revenge", "streak", "leader", "pickup", "reply", "replyFriendly", "spot", "hot", "cold", "idle", "avoid", "answerSystem", "answer", "toneNovice", "toneVeteran",
                                          "recentKill", "recentDeath", "recentSuicide"])
        {
            if (!words.Prompts.ContainsKey(key))
                throw new InvalidDataException($"the chat file has no prompt '{key}'");
        }

        return words;
    }

    /// <summary>The spoken name of a weapon as the kill feed names it.</summary>
    public string WeaponName(string? weapon)
    {
        if (string.IsNullOrEmpty(weapon))
            return "gun";
        string bare = weapon.StartsWith("weapon_", StringComparison.Ordinal) ? weapon["weapon_".Length..] : weapon;
        return Weapons.TryGetValue(bare, out string? spoken) ? spoken : bare.Replace('_', ' ');
    }

    /// <summary>The tone of <paramref name="trait"/> in <paramref name="profile"/>, or a plain one when the file has none for that skill.</summary>
    public string ToneOf(SkillProfile profile, BotTrait trait)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string tone = Tones.TryGetValue(profile.Skills[trait.Skill].Key, out ChatTones? t) ? trait.High ? t.High : t.Low : "easygoing";
        string rank = profile.Spent <= NoviceSpent ? Prompts["toneNovice"] : profile.Spent >= VeteranSpent ? Prompts["toneVeteran"] : "";
        return rank.Length > 0 ? tone + ", " + rank : tone;
    }
}
