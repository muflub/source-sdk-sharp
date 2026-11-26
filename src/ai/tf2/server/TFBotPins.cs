using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using TF2.Server.Econ;
using TF2.Shared;

namespace SourceSharp.Ai.TF2;

/// <summary>What one TF2 bot was pinned to and its brain's seed (plan_bots_teams_tf2 P7 step 4): a class and an archetype, either none.</summary>
public sealed record TFBotPin(ActorId Bot, ulong Seed, TFClass Class, string Archetype)
{
    /// <summary>The defindexes <c>items=</c> pinned (round 2's live proofs: an Atomizer Scout), each the bot's in its slot when it is a works row there; none by default.</summary>
    public IReadOnlyList<int> Items { get; init; } = [];

    /// <summary>The archetype the bot plays <paramref name="playerClass"/> as: its pin when that is its pinned class, else its seed's draw.</summary>
    public TFArchetype? ArchetypeFor(TFClass playerClass)
        => TFClassSpecs.Of(playerClass)?.Ways.For(Seed, playerClass == Class ? Archetype : null);
}

/// <summary>
/// TF2's options on <c>ai_bot_add</c> (plan_bots_teams_tf2 P7 step 4): <c>class=&lt;class&gt;</c> and
/// <c>archetype=&lt;name&gt;</c>, a pin for tests and A/B runs; <c>items=&lt;defindex&gt;[,...]</c> (round 2) pins items over
/// the archetype's, each in the slot whose works rows hold it, so a hook is shown live before an archetype plays it. A pinned class is the bot's whatever the composition
/// wants, and it never changes; a pinned archetype is one of that class's own (its file's table). Kept per bot as it
/// joins the game (every level), with the seed its brain and its archetype draw read.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotOptions)])]
public sealed class TFBotPins : IBotOptions
{
    public const string ClassKey = "class", ArchetypeKey = "archetype", ItemsKey = "items";

    private readonly Dictionary<int, TFBotPin> _bySlot = new();

    public IReadOnlyCollection<string> Keys { get; } = [ClassKey, ArchetypeKey, ItemsKey];

    /// <summary>The defindexes an <c>items=</c> word names (<c>450</c>, <c>450,772</c>), or null when a part is no number.</summary>
    public static int[]? ItemsNamed(string word)
    {
        var items = new List<int>();
        foreach (string part in word.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int defindex) || defindex < 0)
                return null;
            items.Add(defindex);
        }

        return [.. items];
    }

    /// <summary>The playable class <paramref name="word"/> names (<c>soldier</c>, <c>heavy</c>, <c>heavyweapons</c>), or Undefined.</summary>
    public static TFClass ClassNamed(string word)
    {
        foreach (TFClass c in TFKnowledge.Playable())
        {
            if (string.Equals(word, TFClassNames.Raw(c), StringComparison.OrdinalIgnoreCase) || (c == TFClass.HeavyWeapons && string.Equals(word, "heavy", StringComparison.OrdinalIgnoreCase)))
                return c;
        }

        return TFClass.Undefined;
    }

    public string? Check(IReadOnlyDictionary<string, string> options)
    {
        TFClass cls = TFClass.Undefined;
        if (options.TryGetValue(ItemsKey, out string? items) && ItemsNamed(items) is null)
            return $"items={items} is no list of defindexes (items=450 or items=450,772)";
        if (options.TryGetValue(ClassKey, out string? word) && (cls = ClassNamed(word)) == TFClass.Undefined)
            return $"'{word}' is no class ({string.Join("|", TFKnowledge.Playable().Select(TFClassNames.Raw))})";
        if (!options.TryGetValue(ArchetypeKey, out string? archetype))
            return null;
        if (cls == TFClass.Undefined)
            return $"archetype={archetype} needs a class= to be one of";
        TFArchetypeTable ways = TFClassSpecs.Of(cls)?.Ways ?? TFArchetypeTable.None(cls);
        return ways.Named(archetype) is null
            ? $"'{archetype}' is no {TFClassNames.Raw(cls)} archetype ({(ways.Rows.Length == 0 ? "it has none yet" : string.Join("|", ways.Rows.Select(r => r.Name)))})"
            : null;
    }

    public void Joined(RosterBot bot)
    {
        TFClass cls = bot.Options.TryGetValue(ClassKey, out string? word) ? ClassNamed(word) : TFClass.Undefined;
        string archetype = bot.Options.TryGetValue(ArchetypeKey, out string? a) ? a : "";
        int[] items = bot.Options.TryGetValue(ItemsKey, out string? i) ? ItemsNamed(i) ?? [] : [];
        _bySlot[bot.Actor.Slot] = new TFBotPin(bot.Actor, AiRandom.SeedFor(AiBots.MatchSeed ^ SourceSharp.Ai.Server.Senses.ServerAiSession.BrainSalt, bot.SeedIndex), cls, archetype)
        {
            Items = items,
        };
    }

    /// <summary>The pin of the bot in <paramref name="slot"/> (its entity index), or null for no AI bot joined there.</summary>
    public TFBotPin? OfSlot(int slot) => _bySlot.GetValueOrDefault(slot);

    /// <summary>The pin of <paramref name="bot"/>, or null when another holds its slot (or none).</summary>
    public TFBotPin? Of(ActorId bot) => _bySlot.TryGetValue(bot.Slot, out TFBotPin? pin) && pin.Bot == bot ? pin : null;
}

/// <summary>
/// The AI's choice of a TF2 bot's items (plan_bots_teams_tf2 C.3.1, P7 step 3), asked by the bots' item source before
/// its seeded draw (<see cref="IBotItemChoice"/>): the loadout that follows from the archetype the bot plays the class
/// as (its pin's, or its seed's draw: the same seed and archetype its decider reads), or -1 to leave the slot to the draw.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotItemChoice)])]
public sealed class TFArchetypeItems(TFBotPins pins) : IBotItemChoice
{
    public int Choose(int entIndex, TFClass cls, TFLoadoutSlot slot, IReadOnlyList<BotWorksItem> works)
    {
        if (pins.OfSlot(entIndex) is not { } pin)
            return -1;
        foreach (int item in pin.Items)
        {
            foreach (BotWorksItem w in works)
            {
                if (w.Defindex == item)
                    return item;
            }
        }

        return TFArchetypeChoice.Choose(pin.ArchetypeFor(cls), pin.Seed, slot, works);
    }
}
