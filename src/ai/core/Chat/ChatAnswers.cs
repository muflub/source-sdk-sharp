using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace SourceSharp.Ai;

/// <summary>What a question is about.</summary>
public enum ChatTopicKind
{
    /// <summary>Nothing anyone here can answer, hostile questions among them.</summary>
    Unknown,

    /// <summary>Where another player is: a bot does not say (§3.6).</summary>
    People,

    /// <summary>Where a thing on the map is.</summary>
    Where,

    /// <summary>Whether the map has a thing.</summary>
    Exists,

    /// <summary>How many of a thing the map has.</summary>
    Count,

    /// <summary>How long a thing takes to come back.</summary>
    Respawn,

    /// <summary>Where a ladder is: from the nav, the lump's item list has none.</summary>
    WhereLadder,

    /// <summary>A thing this game does not have ("is there a bfg").</summary>
    NotInGame,

    /// <summary>How to play: a line of the help file.</summary>
    HowTo,

    MapName,
    Spawns,
    WeaponList,
    ItemList,
    AmmoAny,
    Score,
    OwnScore,
    LastKiller,
}

/// <summary>A question read from a line: its topic, the thing or the how-to it names, and the word it used for one the game lacks.</summary>
public sealed record ChatTopic(ChatTopicKind Kind, ChatItem? Item = null, ChatHowTo? HowTo = null, string Word = "");

/// <summary>What the bot answering may use: the board any player sees, its own last killer, and the asker, only as far as it knows them.</summary>
/// <param name="Text">The line, as said: data, never put in an answer.</param>
/// <param name="SeenAt">Where the asker stands, only when this bot can see them: the one thing that lets an answer say "of you".</param>
/// <param name="AskerAt">Where the asker stands, for the choice of which item to mark on their own screen; never in a line.</param>
public readonly record struct ChatAsked(string Text, ChatScore Score, string? LastKiller, AiVector? SeenAt, AiVector? AskerAt, int Draw);

/// <summary>How a marker shows: exactly at its place, or as a rough area on the minimap.</summary>
public enum ChatMarkKind : byte
{
    Exact,
    Area,
}

/// <summary>
/// A marker for the one who asked (§3.6, user request 2026-11-03). <see cref="At"/> comes from the
/// map's own item list, never from a player or a model; for an <see cref="ChatMarkKind.Area"/> it
/// is the area's centre, the item somewhere inside <see cref="Radius"/> of it.
/// </summary>
public readonly record struct ChatMark(AiVector At, string Label, ChatMarkKind Kind, float Radius, float Seconds);

/// <summary>An answer: the true line, a shorter one for a bot in a fight, what a model's rephrasing is held to, and the marker it brings.</summary>
public sealed record ChatAnswer(ChatTopicKind Kind, string Line, string ShortLine, ChatAnswerCheck? Check, ChatMark? Mark);

/// <summary>
/// What a model's rephrasing of an answer is held to (§3.6): a tiny model invents (on the first
/// measure it put dm_lockdown's RPG in a central plaza, by a clock tower), so every word of what it
/// wrote must be a word of the fact or plain filler, the words the answer turns on must be there, a
/// "no" stays a "no" and a "yes" a "yes", and it keeps at least a third of the fact's words, so a
/// bare "f" is no answer. Anything else is refused and the fact itself goes out. Pure, safe on the
/// worker. Numbers need no rule of their own: a number not in the fact is a word not in the fact.
/// </summary>
public sealed record ChatAnswerCheck(string Fact, ImmutableArray<string> Must, ImmutableHashSet<string> Filler, ImmutableHashSet<string> Negations, ImmutableHashSet<string> Agreements)
{
    /// <summary>Whether <paramref name="line"/> says what the fact says and nothing more.</summary>
    public bool Holds(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        string[] said = ChatText.Words(line.ToLowerInvariant());
        string[] fact = ChatText.Words(Fact.ToLowerInvariant());
        bool factDenies = fact.Any(Negations.Contains);
        bool lineDenies = said.Any(Negations.Contains);
        if (said.Length < Math.Max(2, fact.Length / 3) || factDenies != lineDenies)
            return false;
        if (factDenies && said.Any(Agreements.Contains))
            return false;

        // Word for word it may add filler and leave words out, in the fact's own order, so what
        // it keeps cannot be shuffled into another claim ("no pistol at spawn").
        int next = 0;
        foreach (string word in said)
        {
            int at = Array.IndexOf(fact, word, next);
            if (at >= 0)
                next = at + 1;
            else if (!Filler.Contains(word) && !(factDenies && Negations.Contains(word)))
                return false;
        }

        string lower = " " + string.Join(' ', said) + " ";
        return Must.All(m => lower.Contains(" " + m + " ", StringComparison.Ordinal));
    }
}

/// <summary>
/// Reads a player's question and answers it truthfully (plan_hl2mp_ai §3.6, user request
/// 2026-11-03): the facts come from code, here: the map's item and spawn lists and layout
/// (<see cref="MapKnowledge"/>, <see cref="ChatPlaces"/>), the file's how-to answers, the board
/// and the bot's own last killer. Nothing here knows where another player is, save an asker this
/// bot can see, and only to say how far a thing is from them. A good weapon is answered
/// vaguely and marked as a rough area (ruling 2026-11-03: "bots should be more vague with finding
/// better weapons"); the area's offset is drawn from <paramref name="seed"/> and the item, so
/// the same seed gives the same offset. Pure and main-thread.
/// </summary>
public sealed class ChatAnswerBook(ChatHelp help, MapKnowledge map, ChatPlaces places, IReadOnlySet<string> goodClasses, ulong seed)
{
    /// <summary>How long a marker lasts.</summary>
    public const float MarkSeconds = 10f;

    /// <summary>A good weapon's rough area: its radius, and how far off centre the weapon may lie, as shares of it.</summary>
    public const float AreaRadius = 500f, AreaOffsetMin = 0.35f, AreaOffsetMax = 0.75f;

    /// <summary>World units in a metre, as the game's player scale has it (16 units a foot).</summary>
    public const float UnitsPerMetre = 52.5f;

    /// <summary>A map answer's marker lasts <see cref="MarkSeconds"/> whatever it says; a line with this many words or fewer is short enough for a bot in a fight.</summary>
    public const int ShortWords = 6;

    private readonly ImmutableArray<(string Phrase, ChatItem Item)> _phrases = [.. help.Items
        .SelectMany(i => i.Words.Select(w => (w, i)))
        .OrderByDescending(p => p.w.Length)];

    /// <summary>
    /// The topic of a line, or null when it is no question for this route: a line that ends in a
    /// question mark, opens with a question word, says "where", or names a bot, and whose words name
    /// a topic. A line with a question mark and no topic is <see cref="ChatTopicKind.Unknown"/>
    /// (a shrug); one with no mark and no topic is talk, for the ordinary reply.
    /// </summary>
    public ChatTopic? Read(string text, bool namesABot, IReadOnlySet<string> people)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(people);
        string lower = " " + string.Join(' ', ChatText.Words(text.ToLowerInvariant())) + " ";
        string[] said = ChatText.Words(text.ToLowerInvariant());
        if (said.Length == 0 || text.TrimStart().StartsWith('/') || text.TrimStart().StartsWith('!'))
            return null;
        bool mark = text.TrimEnd().EndsWith('?');
        bool asks = mark || help.QuestionWords.Contains(said[0]) || said.Any(w => w.StartsWith("where", StringComparison.Ordinal));
        if (!asks && !namesABot)
            return null;
        if (!mark && help.SmallTalk.Any(s => Has(lower, s)))
            return null;
        if (mark && help.SmallTalk.Any(s => lower.Trim() == s))
            return null;

        ChatTopic? topic = TopicOf(lower, said, people);

        // A bot's name alone asks nothing of it: "pia nice spot" is talk, unless it names a thing.
        if (!asks && topic is { Kind: ChatTopicKind.People or ChatTopicKind.Unknown })
            return null;
        if (topic is not null)
            return topic;
        return mark ? new ChatTopic(ChatTopicKind.Unknown) : null;
    }

    private ChatTopic? TopicOf(string lower, string[] said, IReadOnlySet<string> people)
    {
        bool where = help.WhereWords.Any(w => Has(lower, w));
        (string Phrase, ChatItem Item)? item = null;
        foreach ((string phrase, ChatItem i) in _phrases)
        {
            if (Has(lower, phrase))
            {
                item = (phrase, i);
                break;
            }
        }

        // Where someone is, before anything else: "where is ravi", "where are you", "where's everyone".
        bool ammo = help.AmmoGeneric.Any(w => Has(lower, w));
        if (where && item is null && !ammo && (said.Any(people.Contains) || said.Any(help.PeopleWords.Contains)))
            return new ChatTopic(ChatTopicKind.People);

        // A thing asked for by what it is does not turn into its how-to because the words overlap: "is there a gravity gun".
        if (item is { } asked && AskedFor(lower, where) is { } intent)
            return new ChatTopic(intent, asked.Item);
        if (where && Has(lower, "ladder"))
            return new ChatTopic(ChatTopicKind.WhereLadder);

        foreach (ChatHowTo h in help.HowTo.OrderByDescending(h => h.Words.Max(w => w.Length)))
        {
            if (h.Words.Any(w => w.Contains(' ', StringComparison.Ordinal) && Has(lower, w)))
                return new ChatTopic(ChatTopicKind.HowTo, HowTo: h);
        }

        if (help.LastKillerWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.LastKiller);
        if (help.ScoreWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.Score);
        if (help.OwnScoreWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.OwnScore);
        if (help.SpawnWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.Spawns);
        if (help.MapWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.MapName);
        if (help.WeaponListWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.WeaponList);
        if (help.ItemListWords.Any(w => Has(lower, w)))
            return new ChatTopic(ChatTopicKind.ItemList);

        if (item is { } found)
            return new ChatTopic(ChatTopicKind.Exists, found.Item);

        if (help.NotInGame.FirstOrDefault(w => Has(lower, w)) is { } lacking)
            return new ChatTopic(ChatTopicKind.NotInGame, Word: lacking);
        if (ammo)
            return new ChatTopic(ChatTopicKind.AmmoAny);

        foreach (ChatHowTo h in help.HowTo)
        {
            if (!where && h.Words.Any(w => Has(lower, w)))
                return new ChatTopic(ChatTopicKind.HowTo, HowTo: h);
        }

        return where ? new ChatTopic(ChatTopicKind.Unknown) : null;
    }

    private ChatTopicKind? AskedFor(string lower, bool where)
    {
        if (where)
            return ChatTopicKind.Where;
        if (help.CountWords.Any(w => Has(lower, w)))
            return ChatTopicKind.Count;
        if (help.RespawnWords.Any(w => Has(lower, w)))
            return ChatTopicKind.Respawn;
        return help.ExistsWords.Any(w => Has(lower, w)) ? ChatTopicKind.Exists : null;
    }

    private static bool Has(string spacedLower, string phrase)
        => spacedLower.Contains(" " + string.Join(' ', ChatText.Words(phrase)) + " ", StringComparison.Ordinal);

    /// <summary>The answer to <paramref name="topic"/>. A shrug has no check: nothing a model wrote stands in for one.</summary>
    public ChatAnswer Answer(ChatTopic topic, in ChatAsked asked)
    {
        ArgumentNullException.ThrowIfNull(topic);
        return topic.Kind switch
        {
            ChatTopicKind.People => Shrug(help.PeopleShrugs, ChatTopicKind.People, asked.Draw),
            ChatTopicKind.Where => Where(topic.Item!, asked),
            ChatTopicKind.WhereLadder => WhereLadder(asked),
            ChatTopicKind.Exists => Exists(topic.Item!, asked),
            ChatTopicKind.Count => Count(topic.Item!),
            ChatTopicKind.Respawn => Respawn(topic.Item!),
            ChatTopicKind.NotInGame => Fact(topic.Kind, Fill("notInGame", ("word", topic.Word)), [topic.Word]),
            ChatTopicKind.HowTo => Fact(topic.Kind, topic.HowTo!.Answer, topic.HowTo.Must),
            ChatTopicKind.MapName => MapName(),
            ChatTopicKind.Spawns => Fact(topic.Kind, Fill("spawns", ("n", Num(map.Spawns.Length))), [Num(map.Spawns.Length)]),
            ChatTopicKind.WeaponList => Fact(topic.Kind, Fill("weaponList", ("list", List(i => i.Classes[0].StartsWith("weapon_", StringComparison.Ordinal)))), []),
            ChatTopicKind.ItemList => Fact(topic.Kind, Fill("itemList", ("list", List(i => !i.Classes[0].StartsWith("weapon_", StringComparison.Ordinal) && !i.Classes[0].Contains("ammo", StringComparison.Ordinal)))), []),
            ChatTopicKind.AmmoAny => Fact(topic.Kind, Fill("ammo", ("n", Num(map.Items.Count(i => i.Kind == ItemKind.Ammo)))), [Num(map.Items.Count(i => i.Kind == ItemKind.Ammo))]),
            ChatTopicKind.Score => Score(asked.Score),
            ChatTopicKind.OwnScore => Fact(topic.Kind, Fill("ownScore", ("frags", Num(asked.Score.Frags)), ("deaths", Num(asked.Score.Deaths))), [Num(asked.Score.Frags), Num(asked.Score.Deaths)]),
            ChatTopicKind.LastKiller => asked.LastKiller is { } who
                ? new ChatAnswer(topic.Kind, Fill("lastKiller", ("name", ChatPromptBuilder.Untrusted(who, ChatPromptBuilder.MaxName))), "", null, null)
                : new ChatAnswer(topic.Kind, Fill("lastKillerNone"), "", null, null),
            _ => Shrug(help.Shrugs, ChatTopicKind.Unknown, asked.Draw),
        };
    }

    /// <summary>Whether <paramref name="item"/> is one a bot keeps vague about: a good weapon.</summary>
    public bool IsGood(ChatItem item) => item.Classes.Any(goodClasses.Contains);

    private ChatAnswer Shrug(ImmutableArray<string> from, ChatTopicKind kind, int draw)
        => new(kind, from[(int)((uint)draw % (uint)from.Length)], "", null, null);

    private ChatAnswer Fact(ChatTopicKind kind, string line, IEnumerable<string> must, ChatMark? mark = null, string shortLine = "")
        => new(kind, line, shortLine, new ChatAnswerCheck(line, [.. must.Select(m => string.Join(' ', ChatText.Words(m.ToLowerInvariant())))], help.Filler, help.Negations, help.Agreements), mark);

    /// <summary>A plain line with a word in front of it now and then, so the same answer twice does not read as a recording; the first draw has none.</summary>
    public string Voiced(string line, int draw)
    {
        ArgumentNullException.ThrowIfNull(line);
        string lead = help.LeadIns[(int)((uint)draw % (uint)help.LeadIns.Length)];
        string[] words = ChatText.Words(line.ToLowerInvariant());
        if (words.Length == 0 || help.Agreements.Contains(words[0]) || (words.Any(help.Negations.Contains) && help.Agreements.Contains(lead.Trim())))
            return line;
        return lead + line;
    }

    private ItemSpawn[] Spawned(ChatItem item) => [.. map.Items.Where(i => item.Classes.Contains(i.ClassName, StringComparer.Ordinal))];

    private ChatAnswer Absent(ChatItem item, ChatTopicKind kind)
    {
        bool loadout = item.Classes.Any(help.Loadout.Contains);
        string line = Fill(loadout ? "loadout" : "absent", ("item", item.Say));
        return Fact(kind, line, loadout ? [.. ChatText.Words(item.Say), "spawn with"] : ChatText.Words(item.Say));
    }

    private ChatAnswer Where(ChatItem item, in ChatAsked asked)
    {
        ItemSpawn[] spawned = Spawned(item);
        if (spawned.Length == 0)
            return Absent(item, ChatTopicKind.Where);
        ItemSpawn chosen = Nearest(spawned, asked.AskerAt);
        if (IsGood(item))
            return VagueWhere(item, spawned, chosen, asked.Draw);

        string? self = item.Say;
        var must = new List<string>(ChatText.Words(item.Say));
        string line = "";
        AiVector? seenAt = asked.SeenAt;
        int metres = seenAt is { } s0 ? (int)MathF.Round(ChatPlaces.Distance(s0, chosen.Position) / UnitsPerMetre) : 0;
        string dir = seenAt is { } s1 ? ChatPlaces.Direction(s1, chosen.Position) : "";
        // The most the line's length has room for: what stands by it goes first, then the side.
        for (int detail = 2; detail >= 0; detail--)
        {
            string place = Where(chosen.Position, self, detail);
            line = seenAt is not null
                ? Fill(spawned.Length == 1 ? "whereOneNear" : "whereManyNear", ("item", item.Say), ("m", Num(metres)), ("dir", dir), ("place", place))
                : Fill(spawned.Length == 1 ? "whereOne" : "whereMany", ("item", item.Say), ("many", item.Many), ("n", Num(spawned.Length)), ("place", place));
            if (line.Length <= ChatOutputFilter.MaxLine)
                break;
        }

        if (seenAt is not null)
        {
            must.Add(Num(metres));
            must.AddRange(ChatText.Words(dir));
        }
        else
        {
            must.AddRange(EssentialPlaceWords(chosen));
        }

        return Fact(ChatTopicKind.Where, line, must, new ChatMark(chosen.Position, item.Label, ChatMarkKind.Exact, 0f, MarkSeconds), ShortPlace(chosen));
    }

    private ChatAnswer WhereLadder(in ChatAsked asked)
    {
        if (places.LadderNear(asked.AskerAt) is not { } at)
            return Shrug(help.Shrugs, ChatTopicKind.Unknown, asked.Draw);
        string line = Fill("whereLadder", ("place", Where(at, null, 1, ladder: false)));
        return Fact(ChatTopicKind.Where, line, ChatText.Words(places.EndOf(at)).Where(w => w != "end"), new ChatMark(at, "Ladder", ChatMarkKind.Exact, 0f, MarkSeconds), ShortPlaceAt(at));
    }

    private ChatAnswer VagueWhere(ChatItem item, ItemSpawn[] spawned, ItemSpawn chosen, int draw)
    {
        string vague = VagueWords(chosen, draw);
        string line = Fill(spawned.Length == 1 ? "vagueOne" : "vagueMany", ("item", item.Say), ("many", item.Many), ("vague", vague));
        return Fact(ChatTopicKind.Where, line, ChatText.Words(item.Say), AreaOf(chosen, item.Label), vague);
    }

    /// <summary>The rough area around a good weapon: the same for the same seed and item, never centred on it, always holding it.</summary>
    public ChatMark AreaOf(in ItemSpawn item, string label)
    {
        var random = new AiRandom(AiRandom.SeedFor(seed, item.Index));
        float angle = random.NextFloat() * 2f * MathF.PI;
        float reach = AreaRadius * (AreaOffsetMin + (AreaOffsetMax - AreaOffsetMin) * random.NextFloat());
        var centre = new AiVector(item.Position.X + reach * MathF.Cos(angle), item.Position.Y + reach * MathF.Sin(angle), item.Position.Z);
        return new ChatMark(centre, label, ChatMarkKind.Area, AreaRadius, MarkSeconds);
    }

    private static ItemSpawn Nearest(ItemSpawn[] from, AiVector? to)
    {
        if (to is not { } at)
            return from[0];
        ItemSpawn best = from[0];
        float bestDistance = float.MaxValue;
        foreach (ItemSpawn i in from)
        {
            float d = ChatPlaces.Distance(i.Position, at);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>A thing a person can be pointed at by name: never a good weapon, so a landmark does not give one away.</summary>
    private string? LandmarkName(ItemSpawn item)
    {
        if (goodClasses.Contains(item.ClassName))
            return null;
        foreach (ChatItem i in help.Items)
        {
            if (i.Classes.Contains(item.ClassName, StringComparer.Ordinal))
                return i.Say;
        }

        return null;
    }

    /// <summary>A place in words: the end, then the floor, a ladder; at <paramref name="detail"/> 1 and up the side too, at 2 what stands by it.</summary>
    private string Where(AiVector at, string? notThis, int detail = 2, bool ladder = true)
    {
        ChatPlace p = places.Describe(at, LandmarkName, notThis);
        var b = new StringBuilder();
        b.Append(p.End == "middle" ? "in the middle" : "at the " + p.End);
        if (detail >= 1 && p.Side.Length > 0)
            b.Append(" on the ").Append(p.Side).Append(" side");
        if (p.Floor.Length > 0)
            b.Append(", ").Append(p.Floor);
        if (ladder && p.UpALadder)
            b.Append(", up a ladder");
        else if (ladder && p.ByLadder)
            b.Append(", by a ladder");
        else if (detail >= 2 && p.Near.Length > 0)
            b.Append(", by ").Append(p.Near.StartsWith("a ", StringComparison.Ordinal) ? p.Near : "the " + p.Near);
        return b.ToString();
    }

    private IEnumerable<string> EssentialPlaceWords(in ItemSpawn item)
    {
        var words = new List<string>(ChatText.Words(places.EndOf(item.Position)));
        words.RemoveAll(w => w == "end");
        words.AddRange(ChatText.Words(places.FloorOf(item.Position)).Where(w => w is "upstairs" or "downstairs"));
        return words;
    }

    private string ShortPlace(in ItemSpawn item) => ShortPlaceAt(item.Position);

    private string ShortPlaceAt(AiVector at)
    {
        string floor = places.FloorOf(at);
        string end = places.EndOf(at);
        return floor.Length > 0 ? end + ", " + floor : end;
    }

    /// <summary>Which part of the map, no finer: a floor and a side or an end, by the draw. No landmark, no distance, nothing of a spot.</summary>
    public string VagueWords(in ItemSpawn item, int draw)
    {
        string floor = places.FloorOf(item.Position);
        string side = places.SideOf(item.Position);
        string end = places.EndOf(item.Position);
        bool bySide = side.Length > 0 && (end == "middle" || (uint)draw % 2u == 0u);
        string key = bySide ? (floor.Length > 0 ? "floorSide" : "side") : floor.Length > 0 ? "floorEnd" : "end";
        string line = help.Vague[key].Replace("{floor}", floor, StringComparison.Ordinal).Replace("{side}", side, StringComparison.Ordinal)
                          .Replace("{end}", end == "middle" ? "middle of the map" : end, StringComparison.Ordinal);
        return line;
    }

    private ChatAnswer Exists(ChatItem item, in ChatAsked asked)
    {
        ItemSpawn[] spawned = Spawned(item);
        if (spawned.Length == 0)
            return Absent(item, ChatTopicKind.Exists);
        if (spawned.Length > 1)
            return Fact(ChatTopicKind.Exists, Fill("yesMany", ("n", Num(spawned.Length)), ("many", item.Many)), [Num(spawned.Length)]);
        return Fact(ChatTopicKind.Exists, IsGood(item) ? Fill("yesOne", ("item", item.Say), ("vague", VagueWords(spawned[0], asked.Draw))) : Fill("yesOneExact", ("item", item.Say)), ChatText.Words(item.Say));
    }

    private ChatAnswer Count(ChatItem item)
    {
        ItemSpawn[] spawned = Spawned(item);
        return spawned.Length switch
        {
            0 => Fact(ChatTopicKind.Count, Fill("countNone", ("many", item.Many)), []),
            1 => Fact(ChatTopicKind.Count, Fill("countOne", ("item", item.Say)), ChatText.Words(item.Say)),
            _ => Fact(ChatTopicKind.Count, Fill("count", ("n", Num(spawned.Length)), ("many", item.Many)), [Num(spawned.Length)]),
        };
    }

    private ChatAnswer Respawn(ChatItem item)
    {
        ItemSpawn[] spawned = Spawned(item);
        if (spawned.Length == 0)
            return Absent(item, ChatTopicKind.Respawn);
        float seconds = spawned[0].RespawnSeconds;
        return seconds <= 0f
            ? Fact(ChatTopicKind.Respawn, Fill("respawnNone", ("item", item.Say)), [])
            : Fact(ChatTopicKind.Respawn, Fill("respawn", ("item", item.Say), ("n", Num((int)seconds))), [Num((int)seconds)]);
    }

    private ChatAnswer MapName()
    {
        string floors = places.FloorCount == 1 ? "one floor" : Num(places.FloorCount) + " floors";
        return Fact(ChatTopicKind.MapName, Fill("mapName", ("map", map.Map), ("floors", floors), ("spawns", Num(map.Spawns.Length))), [map.Map, Num(map.Spawns.Length)]);
    }

    private string List(Func<ChatItem, bool> which)
    {
        var parts = new List<string>();
        foreach (ChatItem item in help.Items.Where(which))
        {
            int n = Spawned(item).Length;
            if (n > 0)
                parts.Add(n == 1 ? "1 " + item.Say : Num(n) + " " + item.Many);
        }

        return parts.Count == 0 ? "nothing at all" : string.Join(", ", parts);
    }

    private ChatAnswer Score(in ChatScore score)
    {
        if (score.Leader == "you")
            return Fact(ChatTopicKind.Score, Fill("leaderSelf", ("n", Num(score.LeaderFrags))), [Num(score.LeaderFrags)]);
        return new ChatAnswer(ChatTopicKind.Score, Fill("leaderOther", ("leader", ChatPromptBuilder.Untrusted(score.Leader, ChatPromptBuilder.MaxName)), ("n", Num(score.LeaderFrags))), "", null, null);
    }

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private string Fill(string key, params ReadOnlySpan<(string Name, string Value)> values)
    {
        string text = help.Templates[key];
        foreach ((string name, string value) in values)
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        return text;
    }
}
