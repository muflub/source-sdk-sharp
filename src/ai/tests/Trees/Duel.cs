using SourceSharp.Ai.Hl2mp.Trees;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// A bot and one opponent on an open floor, for the trees' facts: the bot's
/// memory and brain with the game's skill curves at a profile, the HL2MP
/// decider, a health kit behind the bot, and a view per step.
/// </summary>
public sealed class Duel
{
    public static readonly ActorId Bot = new(2, 1);
    public static readonly ActorId Opponent = new(3, 0x4000_0001);
    public static readonly ActorId Other = new(4, 0x4000_0002);
    public const int Smg1 = 3, Pistol = 1;
    public static readonly AiVector Kit = new(-600f, 0f, 0f);

    public static readonly WeaponKnowledge Weapons = new([
        new WeaponFacts(Pistol, "weapon_pistol", 18, 0.1f, 1.43f, true, false, 0f),
        new WeaponFacts(Smg1, "weapon_smg1", 45, 0.075f, 1.5f, true, false, 0f),
    ]);

    private readonly PerceivedView _view = new(8);
    private int _tick;

    public Duel(SkillProfile profile, params ItemSpawn[] items) : this(profile, null, items)
    {
    }

    public Duel(SkillProfile profile, NavGraph? graph, params ItemSpawn[] items) : this(profile, graph, null, items)
    {
    }

    /// <summary>A duel over <paramref name="graph"/> whose map's static sight is <paramref name="sight"/> (§10 Cover).</summary>
    public Duel(SkillProfile profile, NavGraph? graph, ICellSight? sight, params ItemSpawn[] items)
    {
        Params = SourceSharp.Ai.Tests.Skills.Curves.Game().For(profile);
        ItemSpawn[] all = items.Length > 0 ? items : [new ItemSpawn(0, "item_healthkit", ItemKind.Health, Kit, 30f)];
        var map = new MapKnowledge("duel", [.. all], [], Weapons);
        Memory = new BotMemory(Bot, 33, MemoryParams.For(profile), new MemoryWorld(map, graph, 0.015f, id => id.ToString(), sight), new HabitStore());
        Decider = new Hl2mpDecider(new BrainSetup(Bot, 7, Memory, Params, 0.015f));
        Brain = new Brain(Bot, 7, Memory, Params, Decider);
    }

    public static SkillProfile Preset(string name)
    {
        if (!SkillPresets.TryGet(name, out SkillProfile? p))
            throw new ArgumentException(name);
        return p!;
    }

    public DecisionParams Params { get; }
    public BotMemory Memory { get; }
    public Hl2mpDecider Decider { get; }
    public Brain Brain { get; }

    public int Tick => _tick;

    /// <summary>The bot at the origin with an SMG1, full health and clip unless said; the opponents sighted where given.</summary>
    public SelfState Self(int health = 100, int clip = 30, int reserve = 90, SelfFlags extra = SelfFlags.None, int weapon = Smg1)
        => new(AiVector.Zero, AiVector.Zero, default, health, 0, weapon, clip, reserve, SelfFlags.Alive | SelfFlags.Grounded | extra);

    public IntentCommand Step(SelfState self, (ActorId Id, AiVector At)[] sighted, params AiEvent[] events)
    {
        _tick += 6;
        var tick = new Tick(_tick);
        _view.Reset(Bot, tick, self);
        foreach ((ActorId id, AiVector at) in sighted)
            _view.AddSighted(new ActorFacts(id, at, AiVector.Zero, Smg1, ActorActivity.None, 1f));
        return Brain.Step(tick, _view, events);
    }

    public AiEvent[] SeeAt(ActorId who, AiVector at) => [AiEvent.SightGained(Bot, new Tick(_tick + 6), who, at, AiVector.Zero, 1f)];

    public AiEvent Shots(ActorId who, int rounds) => AiEvent.SeenFire(Bot, new Tick(_tick + 6), who, Smg1, rounds);

    public AiEvent Row(ActorId who, int frags, int rank) => AiEvent.ScoreboardRow(Bot, new Tick(_tick + 6), who, frags, 0, rank);
}
