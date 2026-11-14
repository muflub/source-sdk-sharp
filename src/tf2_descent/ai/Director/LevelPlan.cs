using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SourceSharp.Ai;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Ai.Director;

/// <summary>
/// One robot as the spawn director placed it (plan_descent_logic HO9): what the rules rolled
/// (<see cref="Roll"/>), the room and the marker it belongs to (-1 for the boss, who has a spot
/// of its own), its own post round that marker and the way it faces there, its row seed, and
/// whether no free post was found for it, so it was put on its marker (<see cref="Crowded"/>).
/// </summary>
public sealed record PlannedRobot(RolledRobot Roll, int Room, int Marker, AiVector Post, float Yaw, ulong Seed, bool Boss, bool Crowded);

/// <summary>
/// One pack as the director placed it: its room, its seed, its robots in roster order, and the
/// pack roll's own counts (style re-rolls, robots drawn again, robots left out).
/// </summary>
public sealed record PlannedPack(int Room, ulong Seed, ImmutableArray<PlannedRobot> Robots, int StyleRerolls, int Refits, int Unplaced)
{
    /// <summary>Robots put on their marker for want of a free post.</summary>
    public int Crowded => Robots.Count(r => r.Crowded);
}

/// <summary>
/// A level's roster as the spawn director decides it (plan_descent_logic HO9, 3.3): for (depth,
/// seed, party size, the level's rooms, markers, clearances and doors), its packs in roster
/// order (the boss's first), the packs dropped for want of room in the roster, the rooms whose
/// whole pack fit nowhere in them (<see cref="Emptied"/>, with the robots left out), and the
/// pack markers no one measured (<see cref="Unmeasured"/>: they held anything). <see cref="Rows"/>
/// is the roster it asks for, one <see cref="RosterRow"/> a robot; <see cref="Digest"/> hashes them.
/// </summary>
public sealed record LevelPlan(int Depth, ulong Seed, int PartySize, ImmutableArray<PlannedPack> Packs, ImmutableArray<DroppedPack> Dropped,
    ImmutableArray<DroppedPack> Emptied, int Unmeasured)
{
    public int Robots => Packs.Sum(p => p.Robots.Length);

    public int Refits => Packs.Sum(p => p.Refits);

    /// <summary>Robots the rolls asked for that fit no marker of their room, in kept packs and emptied rooms alike.</summary>
    public int Unplaced => Packs.Sum(p => p.Unplaced) + Emptied.Sum(e => e.Robots);

    public int Crowded => Packs.Sum(p => p.Crowded);

    /// <summary>The roster the plan asks for, a row a robot, in roster order.</summary>
    public IEnumerable<RosterRow> Rows()
    {
        for (int p = 0; p < Packs.Length; p++)
            foreach (var r in Packs[p].Robots)
                yield return new RosterRow(p, r.Room, r.Marker, r.Roll.Template, r.Roll.Scale, r.Roll.Affixes, r.Roll.Temperament.Style,
                    r.Roll.Stats.Life, r.Post, r.Yaw, r.Boss, r.Seed);
    }

    /// <summary>The digest of <see cref="Rows"/>.</summary>
    public string Digest() => RosterRow.Digest(Rows());

    /// <summary>One line: robots, packs, and what the director could not do as asked.</summary>
    public string Summary => string.Create(CultureInfo.InvariantCulture,
        $"{Robots} robots in {Packs.Length} packs (party {PartySize}); drawn again {Refits}, left out {Unplaced} ({Emptied.Length} rooms emptied), crowded {Crowded}, dropped packs {Dropped.Length}, unmeasured markers {Unmeasured}");
}

/// <summary>
/// A roster row as the director decides it and the roster holds it: its pack, room, home
/// marker, template, scale, champion affixes, temperament style, full life, post, yaw, whether
/// it is the boss, and its seed. <see cref="Line"/> is the same text from either side, so a
/// level's roster can be checked against its replay robot by robot.
/// </summary>
public readonly record struct RosterRow(int Pack, int Room, int Marker, string Template, double Scale, ImmutableArray<string> Affixes, string Style,
    double Life, AiVector Post, float Yaw, bool Boss, ulong Seed)
{
    public string Line() => string.Create(CultureInfo.InvariantCulture,
        $"pack {Pack} room {Room} marker {Marker} {Template} scale {Scale:R} [{string.Join('+', Affixes)}] {Style} life {Life:R} post ({Post.X:R}, {Post.Y:R}, {Post.Z:R}) yaw {Yaw:R}{(Boss ? " boss" : "")} seed {Seed}");

    /// <summary>The first 16 hex digits of the SHA-256 of the rows' lines, one a line.</summary>
    public static string Digest(IEnumerable<RosterRow> rows)
    {
        var text = new StringBuilder();
        foreach (var r in rows) text.Append(r.Line()).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    /// <summary>
    /// Every row where <paramref name="held"/> (a roster's) and <paramref name="replay"/> (the
    /// director's answer for the same inputs) differ, robot by robot, as text: empty when the
    /// roster is its replay. A row one side lacks is a difference too.
    /// </summary>
    public static List<string> Differences(IReadOnlyList<RosterRow> held, IReadOnlyList<RosterRow> replay)
    {
        var lines = new List<string>();
        for (int i = 0; i < Math.Max(held.Count, replay.Count); i++)
        {
            string a = i < held.Count ? held[i].Line() : "(none)", b = i < replay.Count ? replay[i].Line() : "(none)";
            if (a != b) lines.Add($"row {i}: roster {a} / replay {b}");
        }
        return lines;
    }
}
