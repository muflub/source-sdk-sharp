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

/// <summary>A pack: its seed, depth and party size, its robots, and how many times its temperaments were re-rolled for being all one style.</summary>
public sealed record Pack(ulong Seed, int Depth, int PartySize, ImmutableArray<RolledRobot> Robots, int StyleRerolls)
{
    /// <summary>The pack's threat: the sum of its robots' (<see cref="RobotStats.Threat"/>).</summary>
    public double Threat => Robots.Sum(r => r.Stats.Threat);
}

/// <summary>
/// The roster roll (plan_tf2_rpg 2f, B.11, D11, R25; plan_descent_logic RU7, section 3.3): the
/// robots of one pack, deterministic in (level seed, room, depth, party size, marker count).
/// Pack size is the depth's range, raised 25 % a party member past the first; templates are
/// drawn by the depth's weights; scale from the pack seed and the robot's index; a robot is a
/// champion with the depth's chance, carrying one to the depth's most affixes, at most one
/// immunity, and no immunity that would make more than half the pack immune to one type. A
/// pack whose members all share a style is re-rolled. Robots stand at the room's markers in
/// turn; a room with no marker has no pack.
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

    public Pack RollPack(ulong levelSeed, int roomIndex, int depth, int partySize, int markerCount)
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
        var affixes = RollAffixes(seed, row, size);

        // Temperaments, re-rolled while every member shares one style (B.11: packs are mixed).
        int attempt = 0;
        List<Temperament> temperaments;
        while (true)
        {
            int a = attempt;
            temperaments = [.. templates.Select((t, i) => _temperaments.Roll(seed, i, t, scales[i], a))];
            if (size < 2 || temperaments.Select(x => x.Style).Distinct(StringComparer.Ordinal).Count() > 1 || attempt >= S.Temperament.PackRerolls)
                break;
            attempt++;
        }

        var robots = ImmutableArray.CreateBuilder<RolledRobot>(size);
        for (int i = 0; i < size; i++)
            robots.Add(new RolledRobot(i, templates[i].Id, scales[i], affixes[i],
                _robots.Stats(templates[i], scales[i], row.MonsterLevel, affixes[i], party), temperaments[i], i % markerCount));
        return new Pack(seed, depth, party, robots.MoveToImmutable(), attempt);
    }

    /// <summary>Each robot's champion affixes: none, or one to the depth's most, drawn without repeats.</summary>
    private List<ImmutableArray<string>> RollAffixes(ulong seed, DepthRow row, int size)
    {
        var all = _content.Champions.Rows;
        int halfCap = size / 2;
        var immune = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<ImmutableArray<string>>(size);
        for (int i = 0; i < size; i++)
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
