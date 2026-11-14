using System.Collections.Immutable;

namespace SourceSharp.Ai;

/// <summary>
/// What one stored experience is of (G.3's <c>map</c> row): the map by name
/// and its file's checksum, so an edited map starts clean, and the mode's id
/// as the game names it. The rounds a row decays by are counted for this key.
/// </summary>
public readonly record struct ExperienceKey(string Map, string Checksum, string Mode)
{
    /// <summary>The key of no map: what an empty experience is of.</summary>
    public static ExperienceKey None => new("", "", "");

    /// <summary>Whether this names a map and a mode.</summary>
    public bool IsValid => !string.IsNullOrEmpty(Map) && !string.IsNullOrEmpty(Mode);
}

/// <summary>
/// The rest of a row's dimensions (G.3's <c>context</c>), as small values the
/// game supplies: the stage (the active objective's index), the team's role
/// and the player's class. In a query a field of <see cref="Any"/> takes every
/// value; a stored row never holds it.
/// </summary>
public readonly record struct ExperienceContext(int Stage, int Role, int Class)
{
    /// <summary>In a query: every value of the field.</summary>
    public const int Any = -1;

    /// <summary>The query that takes every row.</summary>
    public static ExperienceContext All => new(Any, Any, Any);

    /// <summary>Whether a row of <paramref name="row"/> answers this query.</summary>
    public bool Takes(ExperienceContext row)
        => (Stage == Any || Stage == row.Stage) && (Role == Any || Role == row.Role) && (Class == Any || Class == row.Class);
}

/// <summary>Whose doing a <c>cell_stat</c> row counts: a human's or a bot's, kept apart so a reader can tell them.</summary>
public enum ExperienceSource : byte
{
    Human = 0,
    Bot = 1,
}

/// <summary>Where a <c>link</c> row came from: a human seen doing it, the solver that worked it out, or a bot's practice.</summary>
public enum LinkSource : byte
{
    Human = 0,
    Solver = 1,
    Practice = 2,
}

/// <summary>
/// A row's counts: they add (a merge is a sum, so two servers sharing a store
/// agree whatever the order) and scale (a row fades by half each half-life).
/// </summary>
public interface IExperienceStat<T>
    where T : struct, IExperienceStat<T>
{
    T Plus(T other);

    T Scaled(double factor);
}

/// <summary>One count: a sight line's kills, a look bucket's seconds.</summary>
public readonly record struct Tally(double Count) : IExperienceStat<Tally>
{
    public Tally Plus(Tally other) => new(Count + other.Count);

    public Tally Scaled(double factor) => new(Count * factor);
}

/// <summary>What happened in a bin (G.3's <c>cell_stat</c>): seconds present, deaths, kills, damage taken and dealt.</summary>
public readonly record struct CellStat(double Seconds, double Deaths, double Kills, double DamageTaken, double DamageDealt) : IExperienceStat<CellStat>
{
    public CellStat Plus(CellStat o) => new(Seconds + o.Seconds, Deaths + o.Deaths, Kills + o.Kills, DamageTaken + o.DamageTaken, DamageDealt + o.DamageDealt);

    public CellStat Scaled(double f) => new(Seconds * f, Deaths * f, Kills * f, DamageTaken * f, DamageDealt * f);
}

/// <summary>A place something was set up or held (G.3's <c>spot</c>): uses, seconds held, kills from it, deaths at it.</summary>
public readonly record struct SpotStat(double Uses, double SecondsHeld, double Kills, double Deaths) : IExperienceStat<SpotStat>
{
    public SpotStat Plus(SpotStat o) => new(Uses + o.Uses, SecondsHeld + o.SecondsHeld, Kills + o.Kills, Deaths + o.Deaths);

    public SpotStat Scaled(double f) => new(Uses * f, SecondsHeld * f, Kills * f, Deaths * f);
}

/// <summary>
/// A movement the nav lacks (G.3's <c>link</c>): tries, successes, and the
/// successes' seconds and health cost as sums, so a merge stays a sum; the
/// means divide by the successes.
/// </summary>
public readonly record struct LinkStat(double Tries, double Successes, double Seconds, double HealthCost) : IExperienceStat<LinkStat>
{
    public LinkStat Plus(LinkStat o) => new(Tries + o.Tries, Successes + o.Successes, Seconds + o.Seconds, HealthCost + o.HealthCost);

    public LinkStat Scaled(double f) => new(Tries * f, Successes * f, Seconds * f, HealthCost * f);

    /// <summary>The mean seconds of a success; 0 with none.</summary>
    public double MeanSeconds => Successes > 0d ? Seconds / Successes : 0d;

    /// <summary>The mean health a success cost; 0 with none.</summary>
    public double MeanHealthCost => Successes > 0d ? HealthCost / Successes : 0d;
}

/// <summary>What a choice came to (G.3's <c>outcome</c>): how often it was tried and its scores' sum.</summary>
public readonly record struct OutcomeStat(double Tries, double Score) : IExperienceStat<OutcomeStat>
{
    public OutcomeStat Plus(OutcomeStat o) => new(Tries + o.Tries, Score + o.Score);

    public OutcomeStat Scaled(double f) => new(Tries * f, Score * f);
}

/// <summary>A <c>cell_stat</c> row's key.</summary>
public readonly record struct CellKey(ExperienceContext Context, WorldBin Bin, ExperienceSource Source);

/// <summary>A <c>sight_line</c> row's key: where the victim stood (and as what), where the killer stood, and the weapon's class as the game numbers it.</summary>
public readonly record struct SightKey(ExperienceContext Context, WorldBin Victim, WorldBin Killer, int WeaponClass);

/// <summary>A <c>look</c> row's key: a bin and the bucket of the view from it (<see cref="LookBuckets"/>).</summary>
public readonly record struct LookKey(ExperienceContext Context, WorldBin Bin, int Yaw, int Pitch);

/// <summary>A <c>spot</c> row's key: the kind as the game numbers it (a sentry place, a sniper's hold), the bin and the yaw bucket faced.</summary>
public readonly record struct SpotKey(ExperienceContext Context, int Kind, WorldBin Bin, int Yaw);

/// <summary>An <c>outcome</c> row's key: the kind of choice as the game numbers it (a route family, a class pick) and the choice.</summary>
public readonly record struct OutcomeKey(ExperienceContext Context, int Kind, long Choice);

/// <summary>
/// A <c>link</c> row's key: the class that can do it, from where to where, the
/// kind as the game numbers it, how to do it and who showed it.
/// </summary>
public readonly record struct LinkKey(int Class, WorldBin From, WorldBin To, int Kind, LinkSource Source, LinkRecipe Recipe);

/// <summary>
/// How a link is done, as bytes only the game's own code reads (a blast
/// jump's stance, aim and timing). Two recipes are one when their bytes are.
/// </summary>
public sealed class LinkRecipe(ImmutableArray<byte> bytes) : IEquatable<LinkRecipe>
{
    /// <summary>The recipe of a link that needs none.</summary>
    public static LinkRecipe None => new([]);

    public ImmutableArray<byte> Bytes { get; } = bytes.IsDefault ? [] : bytes;

    public bool Equals(LinkRecipe? other) => other is not null && Bytes.AsSpan().SequenceEqual(other.Bytes.AsSpan());

    public override bool Equals(object? obj) => Equals(obj as LinkRecipe);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Bytes.AsSpan());
        return hash.ToHashCode();
    }
}

/// <summary>
/// A round played (G.3's <c>round</c>): how long, which role won (0 for
/// nobody), the mean humans and bots in it, and who played what as text
/// (<c>role.class*count</c>, the accumulator's). <see cref="Weight"/> is 1 as
/// a round is added and what is left of it when a snapshot reads it.
/// </summary>
public readonly record struct RoundRow(double Seconds, int WinningRole, double Humans, double Bots, string Composition, double Weight = 1d);

/// <summary>
/// The decay arithmetic (G.3): a row carries the round count it was last
/// written at, and a read or a merge scales it by a half for every half-life
/// of rounds played on its map and mode since. Nothing is rewritten on a timer.
/// </summary>
public static class ExperienceDecay
{
    /// <summary><c>ai_experience_half_life</c>'s default, in rounds.</summary>
    public const double DefaultHalfLife = 20d;

    /// <summary>A row unwritten for this many half-lives (a thousandth left) is dropped by the next merge.</summary>
    public const double ForgetHalfLives = 10d;

    /// <summary>
    /// What is left of a row after <paramref name="rounds"/> rounds at
    /// <paramref name="halfLife"/>: 1 for none (or a count that ran backwards),
    /// a half at one half-life. A half-life of 0 or less never decays.
    /// </summary>
    public static double Factor(long rounds, double halfLife)
        => rounds <= 0 || halfLife <= 0d ? 1d : Math.Pow(0.5d, rounds / halfLife);

    /// <summary>Whether a row last written <paramref name="rounds"/> rounds ago is forgotten.</summary>
    public static bool Forgotten(long rounds, double halfLife) => halfLife > 0d && rounds > halfLife * ForgetHalfLives;
}
