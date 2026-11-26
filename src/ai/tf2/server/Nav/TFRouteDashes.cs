using SourceSharp.Abi;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// What each bot's route said of the air jumps just ahead (round 2's S2, <see cref="INavRouteReports"/>): the main thread
/// notes each drive step's count, stamped with the tick it was handed on; a decide of tick D reads the newest stamped
/// before D (<see cref="Before"/>), so the hands get the Atomizer out for a triple jump in time, however the workers ran.
/// Read from the brains' threads. Emptied at each level's start and end: the tick count starts again with a level, and a
/// report kept from the last one would hide the first equal report of the new one.
/// </summary>
[SideService(Side.Server, As = [typeof(INavRouteReports)])]
public sealed class TFRouteDashes : INavRouteReports, IGameLifecycle
{
    /// <summary>The slots kept: a player's entity index.</summary>
    public const int Slots = 256;

    /// <summary>Reports kept a slot: a decide reads one stamped a tick or two before it.</summary>
    public const int Kept = 4;

    private readonly (int Stamp, int Dashes)[] _ring = new (int, int)[Slots * Kept];
    private readonly int[] _next = new int[Slots];
    private readonly int[] _count = new int[Slots];

    /// <summary>Beside the jumps (TFJumps, First + 920), whose book the link reports go to: a stage of its own, as the driver requires.</summary>
    public int LifecycleOrder => ModStages.First + 921;

    public void OnLevelInitPreEntity(string mapName) => Clear();

    public void OnLevelShutdown() => Clear();

    /// <summary>Forgets every report.</summary>
    public void Clear()
    {
        lock (_ring)
        {
            Array.Clear(_ring);
            Array.Clear(_next);
            Array.Clear(_count);
        }
    }

    public void Dashes(ActorId bot, Tick stamp, int dashes)
    {
        int slot = bot.Slot;
        if ((uint)slot >= Slots)
            return;
        lock (_ring)
        {
            int newest = (_next[slot] + Kept - 1) % Kept;
            if (_count[slot] > 0 && _ring[(slot * Kept) + newest].Dashes == dashes)
                return;
            _ring[(slot * Kept) + _next[slot]] = (stamp.Value, dashes);
            _next[slot] = (_next[slot] + 1) % Kept;
            _count[slot] = Math.Min(_count[slot] + 1, Kept);
        }
    }

    /// <summary>The dashes the newest report stamped before <paramref name="tick"/> said for the bot in <paramref name="slot"/>; 0 with none.</summary>
    public int Before(int slot, Tick tick)
    {
        if ((uint)slot >= Slots)
            return 0;
        lock (_ring)
        {
            for (int i = 1; i <= _count[slot]; i++)
            {
                (int stamp, int dashes) = _ring[(slot * Kept) + ((_next[slot] + Kept - i) % Kept)];
                if (stamp < tick.Value)
                    return dashes;
            }
        }

        return 0;
    }
}
