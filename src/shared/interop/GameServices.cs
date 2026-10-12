namespace SourceSharp.Abi;

/// <summary>
/// What the server thinks the time is.
///
/// <para>
/// A one-property service, and it earns its place. Nearly every rule with any
/// duration in it — a respawn wait, a spawn protection window, a door that
/// closes by itself, a fire that burns down — is written against the current
/// time, and every one of those reads <c>Engine.CurrentTime</c> directly today.
/// Outside the game that answers zero, which is why so many of the rules in
/// this mod take a <c>now</c> argument that their caller has to remember to
/// thread through: the argument IS this service, passed by hand.
/// </para>
///
/// <para>
/// Nothing existing is being changed to use it here. It is registered so that
/// the next rule with a clock in it can ask for one instead of inventing
/// another <c>now</c> parameter.
/// </para>
/// </summary>
public interface IGameClock
{
    /// <summary>Server time in seconds, as the engine counts it.</summary>
    float Now { get; }
}

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
