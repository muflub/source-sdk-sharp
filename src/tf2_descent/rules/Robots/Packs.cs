using System.Collections.Immutable;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Furniture;

namespace TF2.Descent.Rules.Robots;

/// <summary>One robot of a rolled pack: its template, scale, champion affixes, numbers, temperament and the room marker it stands at.</summary>
public sealed record RolledRobot(
    int Index,
    string Template,
    double Scale,
    ImmutableArray<string> Affixes,
    RobotStats Stats,
    Temperament Temperament,
    int Marker)
{
    public bool Champion => !Affixes.IsEmpty;
}

/// <summary>
/// A pack: its seed, depth and party size, its robots, how many times its temperaments were
/// re-rolled for being all one style, how many of its robots were drawn again because they fit
/// no marker of the room (<see cref="Refits"/>), and how many fit none after every redraw and
/// were left out (<see cref="Unplaced"/>).
/// </summary>
public sealed record Pack(ulong Seed, int Depth, int PartySize, ImmutableArray<RolledRobot> Robots, int StyleRerolls, int Refits = 0, int Unplaced = 0)
{
    /// <summary>The pack's threat: the sum of its robots' (<see cref="RobotStats.Threat"/>).</summary>
    public double Threat => Robots.Sum(r => r.Stats.Threat);

    /// <summary>The size the depth and party rolled: the robots kept and those left out.</summary>
    public int RolledSize => Robots.Length + Unplaced;
}

/// <summary>
/// What a pack's room can hold (plan_tf2_rpg 5d; plan_descent_logic HO9): whether a robot of
/// <paramref name="template"/> at <paramref name="scale"/> can stand at the room's marker
/// <paramref name="marker"/> and leave the room from there. The spawn director answers it from
/// the level's clearances and doors; with none, every robot fits every marker.
/// </summary>
public interface IPackFit
{
    bool Fits(RobotTemplate template, double scale, int marker);
}

/// <summary>
/// The roster roll (plan_tf2_rpg 2f, 5d, B.11, D11, R25; plan_descent_logic RU7, HO9, section
/// 3.3): the robots of one pack, deterministic in (level seed, room, depth, party size, marker
/// count, and what fits). Pack size is the depth's range, raised 25 % a party member past the
/// first; templates are drawn by the depth's weights; scale from the pack seed and the robot's
/// index. Each robot stands at the first marker, from its turn (index modulo the markers) on,
/// that <see cref="IPackFit"/> says holds it at its scale; one that fits none is drawn again
/// (template and scale, from a stream of its own) up to horde.json's
/// <c>director.refitAttempts</c> times, then left out. Of the robots kept, each is a champion
/// with the depth's chance, carrying one to the depth's most affixes, at most one immunity, and
/// no immunity that would make more than half the pack immune to one type. A pack whose members
/// all share a style is re-rolled. A room with no marker has no pack. With no fit, or when
/// everything fits, the pack is the one the roll gave before HO9.
/// </summary>
public sealed class PackRules
{
    private readonly DescentContent _content;
    private readonly RobotRules _robots;
    private readonly TemperamentRules _temperaments;

    public PackRules(DescentContent content, RobotRules robots, TemperamentRules temperaments)
    {
        _content = content;
        _robots = robots;
        _temperaments = temperaments;
    }

    private RobotRulesSettings S => _content.RobotRules;

    /// <summary>The seed of a room's pack.</summary>
    public static ulong PackSeed(ulong levelSeed, int roomIndex, int depth) =>
        Seeded.From(levelSeed, "pack", depth, roomIndex).NextULong();

    /// <summary>The pack size for a depth's rolled base size and the party (D11).</summary>
    public int PackSize(int baseSize, int partySize)
    {
        int party = Math.Clamp(partySize, 1, S.Party.MaxMembers);
        return (int)Math.Round(baseSize * (1 + S.Party.PackSizePerExtraMemberPct / 100 * (party - 1)), MidpointRounding.AwayFromZero);
    }

    public Pack RollPack(ulong levelSeed, int roomIndex, int depth, int partySize, int markerCount, IPackFit? fit = null)
    {
        ulong seed = PackSeed(levelSeed, roomIndex, depth);
        int party = Math.Clamp(partySize, 1, S.Party.MaxMembers);
        if (markerCount <= 0) return new Pack(seed, depth, party, [], 0);

        var row = _robots.Depth(depth);
        var roll = new Seeded(seed);
        int size = PackSize(row.PackMin + roll.Next(row.PackMax - row.PackMin + 1), party);
        var weights = row.Templates.Select(w => w.Weight).ToList();
        var templates = new List<RobotTemplate>();
        for (int i = 0; i < size; i++)
            templates.Add(_content.Robots[row.Templates[roll.Pick(weights)].Template]);

        var scales = templates.Select((t, i) => RobotRules.RollScale(t, seed, i)).ToList();

        // Where each stands; a robot no marker holds is drawn again, then left out.
        int attempts = _content.Horde.Director.RefitAttempts;
        var markers = new int[size];
        var kept = new List<int>(size);
        int refits = 0, unplaced = 0;
        for (int i = 0; i < size; i++)
        {
            int m = FirstFit(fit, templates[i], scales[i], i, markerCount);
            for (int a = 0; m < 0 && a < attempts; a++)
            {
                var again = Seeded.From(seed, "refit", i, a);
                var t = _content.Robots[row.Templates[again.Pick(weights)].Template];
                double s = RobotRules.RollScale(t, again.NextULong(), i);
                m = FirstFit(fit, t, s, i, markerCount);
                if (m < 0) continue;
                templates[i] = t;
                scales[i] = s;
                refits++;
            }
            markers[i] = m;
            if (m >= 0) kept.Add(i);
            else unplaced++;
        }

        var affixes = RollAffixes(seed, row, kept);

        // Temperaments, re-rolled while every member shares one style (B.11: packs are mixed).
        int attempt = 0;
        List<Temperament> temperaments;
        while (true)
        {
            int a = attempt;
            temperaments = [.. kept.Select(i => _temperaments.Roll(seed, i, templates[i], scales[i], a))];
            if (kept.Count < 2 || temperaments.Select(x => x.Style).Distinct(StringComparer.Ordinal).Count() > 1 || attempt >= S.Temperament.PackRerolls)
                break;
            attempt++;
        }

        var robots = ImmutableArray.CreateBuilder<RolledRobot>(kept.Count);
        for (int k = 0; k < kept.Count; k++)
        {
            int i = kept[k];
            robots.Add(new RolledRobot(i, templates[i].Id, scales[i], affixes[k],
                _robots.Stats(templates[i], scales[i], row.MonsterLevel, affixes[k], party), temperaments[k], markers[i]));
        }
        return new Pack(seed, depth, party, robots.MoveToImmutable(), attempt, refits, unplaced);
    }

    /// <summary>The first marker, from robot <paramref name="index"/>'s turn on, that holds it; -1 when none does.</summary>
    private static int FirstFit(IPackFit? fit, RobotTemplate t, double scale, int index, int markerCount)
    {
        int first = index % markerCount;
        if (fit is null) return first;
        for (int k = 0; k < markerCount; k++)
        {
            int m = (first + k) % markerCount;
            if (fit.Fits(t, scale, m)) return m;
        }
        return -1;
    }

    /// <summary>
    /// Each kept robot's champion affixes: none, or one to the depth's most, drawn without
    /// repeats, from a stream keyed by its own index, so a robot left out moves no other's roll.
    /// The half-immune cap is half the robots kept.
    /// </summary>
    private List<ImmutableArray<string>> RollAffixes(ulong seed, DepthRow row, List<int> kept)
    {
        var all = _content.Champions.Rows;
        int halfCap = kept.Count / 2;
        var immune = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<ImmutableArray<string>>(kept.Count);
        foreach (int i in kept)
        {
            var roll = Seeded.From(seed, "champion", i);
            if (row.MaxAffixes <= 0 || !roll.Chance(row.ChampionPct))
            {
                result.Add([]);
                continue;
            }
            int count = 1 + roll.Next(row.MaxAffixes);
            var pool = all.ToList();
            var chosen = ImmutableArray.CreateBuilder<string>();
            for (int k = 0; k < count && pool.Count > 0; k++)
            {
                // An immunity is open only while this robot has none and the pack's count for its type stays at most half.
                pool.RemoveAll(a => a.Immunity is { } type
                                    && (chosen.Any(c => _content.Champions[c].Immunity is not null)
                                        || immune.GetValueOrDefault(type) + 1 > halfCap));
                if (pool.Count == 0) break;
                var pick = pool[roll.Next(pool.Count)];
                pool.Remove(pick);
                chosen.Add(pick.Id);
                if (pick.Immunity is { } t) immune[t] = immune.GetValueOrDefault(t) + 1;
            }
            result.Add(chosen.ToImmutable());
        }
        return result;
    }
}
