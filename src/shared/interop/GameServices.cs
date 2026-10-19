namespace SourceSharp.Abi;

/// <summary>The real clock: whatever the engine says.</summary>
public sealed class EngineClock : IGameClock
{
    /// <summary>
    /// Zero with no engine, which is what an engine side that is not loaded
    /// answers and is deliberately not corrected here. A clock that invented a
    /// time outside the game would make a rule behave differently in a test
    /// than in a map, which is the opposite of the point.
    /// </summary>
    public float Now => _side.CurrentTime;

    // plan_engineapi_vtables B5, plan_abi_hotreload D8: always one side's
    // EngineSide, handed in by that side's composition. The parameterless
    // constructor that followed the active side is gone.
    private readonly EngineSide _side;

    internal EngineClock(EngineSide side) => _side = side ?? throw new ArgumentNullException(nameof(side));
}
