using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Level;
using TF2.Descent.Rules.Bosses;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>
/// A boss's adds (plan_descent_logic HO13; <see cref="IBossAdds"/>): each add a new row on the
/// roster (<see cref="Ai.Roster.RobotRoster.Add"/>), rolled for the level's depth and the party
/// the roster was rolled for, in a pack of its own per call, standing in the boss's arena on its
/// open mouths in turn when the level ships them (<see cref="OpenMouths"/>; each add faces its
/// mouth's way), else on the arena's pack markers in turn (the marker the boss stands on last),
/// each later round a ring of <c>addSpacing</c> further out; an arena with neither rings the boss. Each is lent a body at
/// once where the pool has one free (a summons is seen arriving, so the never-in-view rule of
/// 3.4 does not hold it back), under TF2's teleporter sparkle; one with no body waits on the
/// roster as any far robot does. The roster's 256 rows and a boss's
/// <see cref="BossTable.MaxAdds"/> over a level are the limits.
/// </summary>
[SideService(Side.Server, As = [typeof(IBossAdds)])]
public sealed class BossAdds(IHordeRules rules, IBossWorld world, ILog log) : IBossAdds
{
    private HordeController? _level;
    private readonly Dictionary<int, int> _given = [];

    /// <summary>Adds put on the roster this level, and those lent a body at once.</summary>
    public int Put { get; private set; }

    public int Lent { get; private set; }

    /// <summary>Adds refused this level: the roster full, or the boss's share spent.</summary>
    public int Refused { get; private set; }

    /// <summary>Adds the boss in <paramref name="bossRow"/> has called this level.</summary>
    public int GivenTo(int bossRow) => _given.GetValueOrDefault(bossRow);

    public int Summon(HordeController horde, int bossRow, string template, int count, ulong seed, long now, int legs = 1)
    {
        ArgumentNullException.ThrowIfNull(horde);
        Begin(horde);

        if (horde.Roster is not { } roster || horde.Level is not { } level || (uint)bossRow >= (uint)roster.Count)
            return 0;

        int room = roster.HomeRoom(bossRow);
        AiVector post = roster.Post(bossRow);
        float spacing = (float)rules.Rules.Bosses.Settings.AddSpacing;
        var mouths = OpenMouths(level, room, legs);
        var spots = mouths.Length > 0 ? Array.ConvertAll(mouths, m => m.Position) : Spots(level, room, post, spacing);
        int put = 0;
        for (int k = 0; k < count; k++)
        {
            if (_given.GetValueOrDefault(bossRow) >= BossTable.MaxAdds)
            {
                Refused += count - k;
                log.Warning($"[TF2Descent/Boss] adds: {roster.Template(bossRow)} has called its {BossTable.MaxAdds} adds this level: {count - k} more refused\n");
                break;
            }

            int turn = _given.GetValueOrDefault(bossRow);
            AiVector spot = spots[turn % spots.Length];
            int ring = turn / spots.Length;
            float a = turn * 2.3999632f;   // the golden angle: a ring's adds spread round their marker
            AiVector at = spot + new AiVector(MathF.Cos(a) * spacing * ring, MathF.Sin(a) * spacing * ring, 0f);
            float yaw = mouths.Length > 0 ? mouths[turn % mouths.Length].Yaw : MathF.Atan2(post.Y - at.Y, post.X - at.X) * (180f / MathF.PI) + 180f;
            ulong s = seed + (ulong)(k + 1) * 0x9E3779B97F4A7C15UL;
            var id = roster.Add(template, null, room, new PackMarker(at, yaw), s, Math.Max(1, horde.PartySize), joinLastPack: k > 0);
            if (!id.IsValid)
            {
                Refused += count - k;
                log.Warning($"[TF2Descent/Boss] adds: the roster is full ({roster.Count} of {roster.Capacity}): {count - k} {template} refused\n");
                break;
            }

            _given[bossRow] = turn + 1;
            put++;
            Put++;
            world.Particle(Champions.ChampionTeleports.In, HordeVectors.ToEngine(at + new AiVector(0f, 0f, 8f)));
            if (horde.LendByHand(id.Index, now, out _))
            {
                Lent++;
                if (roster.Body(id.Index) is int b and > 0 && horde.Bodies is { } bodies)
                    world.Sound(bodies.Body(b).Handle, Champions.ChampionTeleports.ReceiveSound);
            }
        }

        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Boss] adds: {roster.Template(bossRow)} called {put} of {count} {template} into room {room}, tick {now} ({_given.GetValueOrDefault(bossRow)} this level; {Lent} lent bodies, roster {roster.Count} of {roster.Capacity})\n"));
        return put;
    }

    /// <summary>This level's counts: a new level's controller starts them over.</summary>
    public (int Put, int Lent, int Refused) Counts(HordeController horde)
    {
        Begin(horde);
        return (Put, Lent, Refused);
    }

    private void Begin(HordeController horde)
    {
        if (ReferenceEquals(horde, _level))
            return;
        _level = horde;
        _given.Clear();
        Put = 0;
        Lent = 0;
        Refused = 0;
    }

    /// <summary>
    /// The mouths of the level's arena open at <paramref name="legs"/> (the ARNA contract: a mouth of
    /// leg 1 is open from the start, the phase that is its leg opens it), in the file's order; none
    /// when the level ships no mouths or <paramref name="room"/> is not its boss's arena (a dev boss
    /// elsewhere).
    /// </summary>
    public static AddMouth[] OpenMouths(HordeLevel level, int room, int legs)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (level.Boss is not { Layout.Mouths: { Length: > 0 } mouths } boss || boss.ArenaRoom != room)
            return [];
        var open = new List<AddMouth>(mouths.Length);
        foreach (var m in mouths)
            if (m.Leg <= legs)
                open.Add(m);
        return [.. open];
    }

    /// <summary>The arena's pack markers, the boss's own last; the ring round the boss when it has none.</summary>
    public static AiVector[] Spots(HordeLevel level, int room, AiVector post, float spacing)
    {
        ArgumentNullException.ThrowIfNull(level);
        var markers = level.Rooms[room].Markers;
        var spots = new List<AiVector>(markers.Length);
        AiVector? own = null;
        foreach (var m in markers)
        {
            if (m.Position.DistanceTo(post) < spacing * 0.5f)
                own = m.Position;
            else
                spots.Add(m.Position);
        }

        if (own is { } o)
            spots.Add(o);
        if (spots.Count == 0)
            for (int k = 0; k < 4; k++)
            {
                float a = k * MathF.PI / 2f;
                spots.Add(post + new AiVector(MathF.Cos(a) * spacing * 2f, MathF.Sin(a) * spacing * 2f, 0f));
            }

        return [.. spots];
    }
}
