using SourceSharp.Ai;

namespace TF2.Descent.Ai.Senses;

/// <summary>What made a robot start to hunt (the dev log's: <see cref="HordeAlerts"/>).</summary>
public enum AlertCause : byte
{
    /// <summary>Its brain recognised a hero (the vision's <c>SightGained</c>).</summary>
    Sight,

    /// <summary>Its brain heard a sound (<see cref="AlertRecord.Kind"/> says which).</summary>
    Sound,

    /// <summary>It was hit.</summary>
    Damage,

    /// <summary>A pack-mate with a body was hit or killed within earshot (B.3's AllyDamaged, AllyDied).</summary>
    Ally,

    /// <summary>A robot of its pack saw a hero or heard a sound, and the pack was alerted (<c>RobotRoster.AlertPack</c>).</summary>
    PackCall,

    /// <summary>The far simulation's hearing: a sleeping or far robot heard a sound.</summary>
    FarSound,

    /// <summary>A brain started from a carried alert (a body lent to a robot that had been alerted far).</summary>
    Carried,
}

/// <summary>
/// One robot's first alert of a spell: when, the robot's row, pack, place and room, what alerted it
/// (and the sound's kind), where that came from and its room, and the hero seat behind it (-1 none);
/// <see cref="Bodied"/> whether the robot held a body.
/// </summary>
public readonly record struct AlertRecord(long Tick, int Row, int Pack, AlertCause Cause, SoundKind Kind, AiVector RobotAt, int RobotRoom,
                                          AiVector SourceAt, int SourceRoom, int Seat, bool Bodied);

/// <summary>
/// The horde's alert log (dev: <c>tf2_descent_horde_alert_log</c>): every robot's FIRST alert of a
/// spell (none for it in the last <see cref="WindowTicks"/>), from the senses that tell the brains
/// (<c>HordeBrains</c>) and the far simulation's hearing, so a measurement can say which sense
/// started each hunt. A ring of <see cref="Capacity"/> records the server drains each tick; one more
/// overwrites the oldest and is counted (<see cref="Overwritten"/>). Every alert is counted by cause,
/// first of a spell or not. Allocates nothing after it is made.
/// </summary>
public sealed class HordeAlerts
{
    /// <summary>Records the ring holds between two drains.</summary>
    public const int Capacity = 512;

    private const long Never = long.MinValue / 2;

    private readonly AlertRecord[] _ring;
    private readonly long[] _lastTick;
    private readonly long[] _byCause;
    private int _head, _count;

    public HordeAlerts(long windowTicks, AlertRecord[] ring, long[] lastTick, long[] byCause)
    {
        WindowTicks = windowTicks;
        _ring = ring;
        _lastTick = lastTick;
        _byCause = byCause;
    }

    /// <summary>A log for robot rows 0 to <paramref name="rows"/> − 1, a spell <paramref name="windowSeconds"/> long.</summary>
    public static HordeAlerts For(int rows, double windowSeconds, double ticksPerSecond)
    {
        var last = new long[Math.Max(1, rows)];
        Array.Fill(last, Never);
        return new HordeAlerts((long)Math.Ceiling(windowSeconds * ticksPerSecond), new AlertRecord[Capacity], last,
                               new long[Enum.GetValues<AlertCause>().Length]);
    }

    /// <summary>A robot's alerts this many ticks apart are one spell: only the first is recorded.</summary>
    public long WindowTicks { get; }

    /// <summary>Records written since the level began.</summary>
    public long Recorded { get; private set; }

    /// <summary>Records lost to a full ring.</summary>
    public long Overwritten { get; private set; }

    /// <summary>Alerts of <paramref name="cause"/>, first of a spell or not.</summary>
    public long Count(AlertCause cause) => _byCause[(int)cause];

    /// <summary>Records waiting to be drained.</summary>
    public int Waiting => _count;

    /// <summary>An alert: counted, and recorded when it is the robot's first of a spell.</summary>
    public void Add(in AlertRecord r)
    {
        _byCause[(int)r.Cause]++;
        if ((uint)r.Row >= (uint)_lastTick.Length)
            return;
        long last = _lastTick[r.Row];
        _lastTick[r.Row] = r.Tick;
        if (r.Tick - last < WindowTicks)
            return;
        if (_count == _ring.Length)
        {
            _head = (_head + 1) % _ring.Length;
            _count--;
            Overwritten++;
        }

        _ring[(_head + _count) % _ring.Length] = r;
        _count++;
        Recorded++;
    }

    /// <summary>Moves up to <paramref name="into"/>'s length of the waiting records into it, oldest first; answers how many.</summary>
    public int Drain(Span<AlertRecord> into)
    {
        int n = Math.Min(into.Length, _count);
        for (int k = 0; k < n; k++)
            into[k] = _ring[(_head + k) % _ring.Length];
        _head = (_head + n) % _ring.Length;
        _count -= n;
        return n;
    }
}
