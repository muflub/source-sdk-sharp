namespace SourceSharp.Ai;

/// <summary>
/// When a bot decides (plan_hl2mp_ai §2.3): at the ticks where
/// <c>(tick + slot) % period == 0</c>, period 6, about 10 Hz at 66 ticks a
/// second. The slot term spreads bots over the ticks, so eight bots never all
/// decide on the same one.
/// </summary>
public static class DecideSchedule
{
    /// <summary>Ticks between a bot's decisions.</summary>
    public const int Period = 6;

    public static bool IsDecideTick(Tick tick, int slot) => IsDecideTick(tick, slot, Period);

    public static bool IsDecideTick(Tick tick, int slot, int period)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        return ((long)tick.Value + slot) % period == 0;
    }
}
