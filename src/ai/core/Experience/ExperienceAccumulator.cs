using System.Globalization;

namespace SourceSharp.Ai;

/// <summary>
/// One player in a sampled frame, as the accumulator is told of them: where
/// they stand, where they look, as what (stage, role, class), whether they
/// live and whether a bot plays them. It has no name, id or slot: nothing
/// that says who.
/// </summary>
public readonly record struct ActorSample(AiVector Place, float Yaw, float Pitch, ExperienceContext Context, bool Alive, bool Bot);

/// <summary>One side of a hit or a kill: where they stood, as what, and whether a bot plays them.</summary>
public readonly record struct CombatSample(AiVector Place, ExperienceContext Context, bool Bot);

/// <summary>
/// Turns what players did into an <see cref="ExperienceDelta"/> (G.3's feed):
/// sampled frames of every player fill <c>cell_stat</c>'s seconds and
/// <c>look</c>; damage and kills fill <c>cell_stat</c>'s counts and
/// <c>sight_line</c>; a round's end adds a <c>round</c> row. A human weighs
/// <see cref="HumanWeight"/> and a bot <see cref="BotWeight"/> in
/// <c>cell_stat</c> and <c>look</c>, so bots do not teach each other their
/// own stuck spots. Pure and deterministic: the same calls in the same order
/// give the same delta. One thread at a time (the server's experience worker).
/// </summary>
public sealed class ExperienceAccumulator
{
    public const double HumanWeight = 1d;

    public const double BotWeight = 0.25d;

    private ExperienceDelta _delta = new();
    private readonly SortedDictionary<(int Role, int Class), double> _played = new();
    private double _roundSeconds, _humanSeconds, _botSeconds;

    /// <summary>Frames taken since the accumulator was made.</summary>
    public long Frames { get; private set; }

    /// <summary>Hits and kills taken since the accumulator was made.</summary>
    public long Events { get; private set; }

    /// <summary>The rows waiting in the delta.</summary>
    public int Rows => _delta.Rows;

    /// <summary>The weight of a human's or a bot's doing in <c>cell_stat</c> and <c>look</c>.</summary>
    public static double Weight(bool bot) => bot ? BotWeight : HumanWeight;

    /// <summary>
    /// A sampled frame: each living player was where and looked where its
    /// sample says for the <paramref name="seconds"/> since the last frame.
    /// </summary>
    public void Frame(ReadOnlySpan<ActorSample> actors, double seconds)
    {
        Frames++;
        if (seconds <= 0d)
            return;
        _roundSeconds += seconds;
        foreach (ref readonly ActorSample a in actors)
        {
            if (!a.Alive)
                continue;
            double weighed = seconds * Weight(a.Bot);
            WorldBin bin = WorldBin.Of(a.Place);
            _delta.Add(new CellKey(a.Context, bin, Source(a.Bot)), new CellStat(weighed, 0d, 0d, 0d, 0d));
            _delta.Add(new LookKey(a.Context, bin, LookBuckets.Yaw(a.Yaw), LookBuckets.Pitch(a.Pitch)), new Tally(weighed));
            if (a.Bot)
                _botSeconds += seconds;
            else
                _humanSeconds += seconds;
            _played[(a.Context.Role, a.Context.Class)] = _played.GetValueOrDefault((a.Context.Role, a.Context.Class)) + seconds;
        }
    }

    /// <summary>A hit: the victim's bin takes the damage, the attacker's (when a player dealt it) deals it.</summary>
    public void Damage(in CombatSample victim, CombatSample? attacker, double amount)
    {
        Events++;
        if (amount <= 0d)
            return;
        _delta.Add(Cell(victim), new CellStat(0d, 0d, 0d, amount * Weight(victim.Bot), 0d));
        if (attacker is { } by)
            _delta.Add(Cell(by), new CellStat(0d, 0d, 0d, 0d, amount * Weight(by.Bot)));
    }

    /// <summary>
    /// A kill: a death in the victim's bin and, when a player did it, a kill in
    /// the killer's bin and one more on the sight line between them, under the
    /// victim's context and the weapon's class as the game numbers it.
    /// </summary>
    public void Kill(in CombatSample victim, CombatSample? killer, int weaponClass)
    {
        Events++;
        _delta.Add(Cell(victim), new CellStat(0d, Weight(victim.Bot), 0d, 0d, 0d));
        if (killer is not { } by)
            return;
        _delta.Add(Cell(by), new CellStat(0d, 0d, Weight(by.Bot), 0d, 0d));
        _delta.Add(new SightKey(victim.Context, WorldBin.Of(victim.Place), WorldBin.Of(by.Place), weaponClass), new Tally(1d));
    }

    /// <summary>A spot's use, in full whoever used it: what a detector of a later phase reports.</summary>
    public void Spot(ExperienceContext context, int kind, AiVector at, float yaw, SpotStat stat)
        => _delta.Add(new SpotKey(context, kind, WorldBin.Of(at), LookBuckets.Yaw(yaw)), stat);

    /// <summary>A movement's try, in full whoever tried it.</summary>
    public void Link(LinkKey key, LinkStat stat) => _delta.Add(key, stat);

    /// <summary>A choice's result, in full whoever chose.</summary>
    public void Outcome(OutcomeKey key, OutcomeStat stat) => _delta.Add(key, stat);

    /// <summary>
    /// A round ended, won by <paramref name="winningRole"/> (0 for nobody): a
    /// <c>round</c> row of its seconds, its mean humans and bots, and who
    /// played what; the next round's tallies start from nothing.
    /// </summary>
    public void RoundEnd(int winningRole)
    {
        if (_roundSeconds > 0d)
            _delta.Add(new RoundRow(_roundSeconds, winningRole, _humanSeconds / _roundSeconds, _botSeconds / _roundSeconds, Composition()));
        RoundStart();
    }

    /// <summary>A round began with no end before it (a map's first, a restart): the tallies of the round start from nothing, and no row is added.</summary>
    public void RoundStart()
    {
        _roundSeconds = _humanSeconds = _botSeconds = 0d;
        _played.Clear();
    }

    /// <summary>The delta filled so far, handed over; the accumulator goes on into a new one. The round in play keeps its tallies.</summary>
    public ExperienceDelta Take()
    {
        ExperienceDelta taken = _delta;
        _delta = new ExperienceDelta();
        return taken;
    }

    /// <summary>
    /// Who played what over the round: <c>role.class*count</c> for each role
    /// and class whose mean head count rounds to one or more, in role then
    /// class order.
    /// </summary>
    private string Composition()
    {
        var text = new System.Text.StringBuilder();
        foreach (((int role, int cls), double seconds) in _played)
        {
            int count = (int)Math.Round(seconds / _roundSeconds, MidpointRounding.AwayFromZero);
            if (count < 1)
                continue;
            if (text.Length > 0)
                text.Append(' ');
            text.Append(CultureInfo.InvariantCulture, $"{role}.{cls}*{count}");
        }

        return text.ToString();
    }

    private static CellKey Cell(in CombatSample of) => new(of.Context, WorldBin.Of(of.Place), Source(of.Bot));

    private static ExperienceSource Source(bool bot) => bot ? ExperienceSource.Bot : ExperienceSource.Human;
}
