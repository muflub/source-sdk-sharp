namespace TF2.Descent.Ai.Motion;

/// <summary>What stopped a body.</summary>
public enum BlockerKind : byte
{
    None = 0,

    /// <summary>The world: brushes, and static props (the engine reports a static prop as the world).</summary>
    World,

    /// <summary>An entity that is neither a robot body nor a hero: a prop, a door, a building.</summary>
    Prop,

    /// <summary>
    /// Other robots: robots pass through each other, so this is a body held up
    /// in a crowd (the teammate pushaway turned enough of its wish aside to
    /// slow it); <see cref="BlockedReport.Who"/> is the nearest overlapping
    /// body's index.
    /// </summary>
    Robot,

    /// <summary>A hero; <see cref="BlockedReport.Who"/> is the hero's seat (its player's entity index).</summary>
    Hero,
}

/// <summary>
/// A body's blocked report (plan_descent_logic HO3, for the door shove of 3.9
/// and the brain's Stuck event): what stopped it (<see cref="Kind"/>; none when
/// it moves freely), who (<see cref="Who"/>: a body index, a hero seat, or an
/// entity index for a prop; 0 for the world), the tick the blocked run began
/// (<see cref="Since"/>), its last blocked tick (<see cref="Last"/>), and how many
/// ticks of the run were blocked (<see cref="Ticks"/>).
/// </summary>
public readonly record struct BlockedReport(BlockerKind Kind, int Who, long Since, long Last, int Ticks)
{
    public static BlockedReport Clear => default;

    public bool Blocked => Kind != BlockerKind.None;

    /// <summary>How long the run has lasted at <paramref name="now"/>, in ticks.</summary>
    public long For(long now) => Blocked ? now - Since + 1 : 0;
}

/// <summary>
/// A body's last landing (plan_descent_logic HO3, L12): the tick it touched
/// ground, the tick it left it, the speed it fell at, and how far it rose above
/// where it left the ground (zero for a fall). <see cref="Tick"/> is 0 before
/// the first landing of the lend.
/// </summary>
public readonly record struct LandedReport(long Tick, long LeftGround, float FallSpeed, float PeakRise)
{
    public bool Ever => Tick != 0;

    public long AirTicks => Tick - LeftGround;
}

/// <summary>
/// A body's last arrival (plan_descent_logic 3.6, the brain's <c>Arrived</c>): the tick its order's
/// last corner was reached, where the body stood, and how many arrivals the lend has had.
/// <see cref="Tick"/> is 0 before the first.
/// </summary>
public readonly record struct ArrivedReport(long Tick, SourceSharp.Ai.AiVector At, int Count)
{
    public bool Ever => Tick != 0;
}

