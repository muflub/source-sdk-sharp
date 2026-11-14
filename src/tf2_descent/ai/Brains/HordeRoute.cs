using SourceSharp.Ai;
using TF2.Descent.Ai.Level;

namespace TF2.Descent.Ai.Brains;

/// <summary>A decider that walks by a <see cref="HordeRoute"/> (the dev print reads its path).</summary>
public interface IHordeRouted
{
    /// <summary>The brain's walker, or none before a decider that has one is made.</summary>
    HordeRoute? Route { get; }
}

/// <summary>What a decider's routes are planned over: the level (the room route) and its graph's paths, when the server has them.</summary>
public sealed class HordeRoutes(HordeLevel level, IHordePaths? paths)
{
    public HordeLevel Level => level;

    public IHordePaths? Paths => paths;

    /// <summary>A walker for the brain at <paramref name="slot"/>: its path and where it is along it.</summary>
    public HordeRoute NewRoute(int slot) => new(level, paths, slot);
}

/// <summary>
/// One brain's way to a goal (plan_descent_logic 3.6, 3.7): a path on the level's graph when its
/// view says the graph is ready and the paths answer for the same graph
/// (<see cref="HordeView.NavEpoch"/>), else the room route (<see cref="RoomRoute"/>, the fallback
/// before the graph is ready and for a goal the graph cannot reach). Reads only what the
/// recording writes (its own origin, the goal, the view's epoch and class, the tick), so a
/// decider that walks by it replays byte for byte.
///
/// <list type="bullet">
/// <item><b>Reuse.</b> A path is kept while its goal moves less than <see cref="ReuseUnits"/>
/// from the one it was planned to (its last corner follows the goal), the class is the same and
/// the body stays near it (<see cref="OffPathUnits"/>). A goal that moved farther is planned
/// again at the brain's next turn; until then the old path is kept, its last corner following
/// the goal (live, a robot sent by the room route between its turns walked into the alcove's
/// wall).</item>
/// <item><b>Budget.</b> A brain searches only on its own tick of each <see cref="SearchEveryTicks"/>
/// (five decides): the ticks where <c>(tick + slot) % SearchEveryTicks == 0</c>, one of its
/// decide ticks, so the pool's brains take turns round the cycle; and each search expands at
/// most <see cref="SearchBudget"/> cells. With 48 brains that is at most two searches a tick
/// (slots s and s + 30), all on the AI workers. Between its turns a brain without a path goes
/// by the room route.</item>
/// <item><b>Following.</b> The next <see cref="MoveIntent.MaxCorners"/> corners from where the
/// body is along the path; a corner is passed within <see cref="PassUnits"/> on the floor or
/// once the body is past it towards the next. A corner reached by a jump makes the move a jump
/// near its take-off.</item>
/// </list>
/// </summary>
public sealed class HordeRoute
{
    /// <summary>The most corners a path holds; one that goes on is asked again near its end.</summary>
    public const int MaxCorners = 32;

    /// <summary>A goal that moved less than this from the one its path was planned to keeps the path.</summary>
    public const float ReuseUnits = 96f;

    /// <summary>Ticks between two searches of one brain (five decides at the core's period).</summary>
    public const int SearchEveryTicks = 5 * DecideSchedule.Period;

    /// <summary>The cells a search expands at most.</summary>
    public const int SearchBudget = 12_000;

    /// <summary>How near a corner on the floor the body has passed it.</summary>
    public const float PassUnits = 40f;

    /// <summary>A body farther than this from the leg it is on has left its path: it is planned again.</summary>
    public const float OffPathUnits = 160f;

    /// <summary>How near its take-off a jump's corner presses jump.</summary>
    public const float JumpTakeOffUnits = 56f;

    private readonly HordeLevel _level;
    private readonly IHordePaths? _paths;
    private AiVector[] _corners = new AiVector[MaxCorners];
    private NavEdgeType[] _arrive = new NavEdgeType[MaxCorners];
    private AiVector[] _nextCorners = new AiVector[MaxCorners];
    private NavEdgeType[] _nextArrive = new NavEdgeType[MaxCorners];
    private int _count, _at;
    private bool _truncated, _has;
    private AiVector _start, _goal;
    private ClearanceClass _class;
    private int _epoch;
    private readonly int _slot;

    public HordeRoute(HordeLevel level, IHordePaths? paths, int slot = 0)
    {
        _level = level;
        _paths = paths;
        _slot = slot;
    }

    /// <summary>Whether <paramref name="tick"/> is the brain at <paramref name="slot"/>'s turn to search.</summary>
    public static bool SearchTurn(Tick tick, int slot) => ((long)tick.Value + slot) % SearchEveryTicks == 0;

    /// <summary>Searches this brain asked of the paths.</summary>
    public int Searches { get; private set; }

    /// <summary>Moves that came from a path on the graph.</summary>
    public int OnGraph { get; private set; }

    /// <summary>Moves that went by the room route.</summary>
    public int ByRooms { get; private set; }

    /// <summary>Whether the walker holds a path now.</summary>
    public bool HasPath => _has;

    /// <summary>The path's corners left from where the body is, nearest first.</summary>
    public ReadOnlySpan<AiVector> Left => _has ? _corners.AsSpan(_at, _count - _at) : default;

    /// <summary>A move from <paramref name="from"/> to <paramref name="to"/> at <paramref name="speed"/>, at <paramref name="tick"/>, for the robot whose view is <paramref name="self"/>.</summary>
    public MoveIntent Follow(Tick tick, in SelfState self, AiVector from, AiVector to, MoveSpeed speed)
    {
        int epoch = HordeView.NavEpoch(self);
        ClearanceClass cls = HordeView.NavClass(self);
        if (epoch == 0 || _paths is not { } paths || paths.Epoch != epoch)
        {
            _has = false;
            ByRooms++;
            return RoomRoute.Follow(_level, from, to, speed);
        }

        // The path in hand: on this graph and class, the body still on it, and not run out.
        bool usable = _has && _epoch == epoch && _class == cls;
        if (usable)
        {
            Advance(from);
            usable = _at < _count && !Off(from) && !(_truncated && _count - _at <= 1);
        }

        bool moved = usable && _goal.DistanceTo(to) > ReuseUnits;
        if ((!usable || moved) && SearchTurn(tick, _slot))
        {
            Searches++;
            PathAnswer answer = paths.Find(from, to, cls, SearchBudget, _nextCorners, _nextArrive, out int count, out bool truncated);
            if (answer == PathAnswer.Found && count > 0)
            {
                (_corners, _nextCorners) = (_nextCorners, _corners);
                (_arrive, _nextArrive) = (_nextArrive, _arrive);
                _count = count;
                _truncated = truncated;
                _at = 0;
                _start = from;
                _goal = to;
                _class = cls;
                _epoch = epoch;
                Advance(from);

                // A goal already within a corner's reach leaves no corner to walk to: no path.
                usable = _at < _count;
            }
            else
            {
                usable = false;
            }
        }

        // Between its turns a brain whose goal moved keeps its path, the last corner following the goal.
        _has = usable;
        if (!_has)
        {
            ByRooms++;
            return RoomRoute.Follow(_level, from, to, speed);
        }

        // The last corner follows a goal that moved a little.
        if (!_truncated)
            _corners[_count - 1] = to;

        OnGraph++;
        int n = Math.Min(MoveIntent.MaxCorners, _count - _at);
        ReadOnlySpan<AiVector> c = _corners.AsSpan(_at, n);
        AiVector takeOff = _at > 0 ? _corners[_at - 1] : _start;
        bool jump = _arrive[_at] is NavEdgeType.Jump or NavEdgeType.CrouchJump && Flat(from - takeOff) <= JumpTakeOffUnits;
        var kind = jump ? (_arrive[_at] == NavEdgeType.CrouchJump ? MoveKind.CrouchJump : MoveKind.Jump) : MoveKind.Follow;
        return new MoveIntent(kind, speed, (byte)n, c[0], n > 1 ? c[1] : default, n > 2 ? c[2] : default, n > 3 ? c[3] : default,
                              (c[0] - from).Normalised(), CrouchOn: false, Ticks: 0);
    }

    /// <summary>Lets the path go (a decider that holds, or a brain that starts over).</summary>
    public void Drop() => _has = false;

    /// <summary>Moves past the corners the body has reached, or is past towards the next.</summary>
    private void Advance(AiVector from)
    {
        while (_at < _count)
        {
            AiVector corner = _corners[_at];
            if (Flat(corner - from) <= PassUnits && MathF.Abs(corner.Z - from.Z) <= 72f)
            {
                _at++;
                continue;
            }

            // Past it towards the next: beyond the corner along the next leg, and near that leg.
            if (_at + 1 < _count && _arrive[_at] == NavEdgeType.Walk && _arrive[_at + 1] == NavEdgeType.Walk)
            {
                AiVector next = _corners[_at + 1];
                AiVector leg = Flat3(next - corner);
                float len = leg.Length2D;
                if (len > 1e-3f)
                {
                    AiVector d = Flat3(from - corner);
                    float along = (d.X * leg.X + d.Y * leg.Y) / len;
                    float across = MathF.Abs(d.X * leg.Y - d.Y * leg.X) / len;
                    if (along > 0f && along < len && across <= PassUnits && MathF.Abs(corner.Z - from.Z) <= 72f)
                    {
                        _at++;
                        continue;
                    }
                }
            }

            break;
        }
    }

    /// <summary>Whether the body is farther than <see cref="OffPathUnits"/> from the leg it is on.</summary>
    private bool Off(AiVector from)
    {
        AiVector a = _at > 0 ? _corners[_at - 1] : _start, b = _corners[_at];
        AiVector ab = Flat3(b - a), ap = Flat3(from - a);
        float len2 = ab.X * ab.X + ab.Y * ab.Y;
        float t = len2 > 1e-6f ? Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / len2, 0f, 1f) : 0f;
        float dx = ap.X - ab.X * t, dy = ap.Y - ab.Y * t;
        return MathF.Sqrt(dx * dx + dy * dy) > OffPathUnits || MathF.Abs(from.Z - (a.Z + (b.Z - a.Z) * t)) > 128f;
    }

    private static float Flat(AiVector v) => v.Length2D;

    private static AiVector Flat3(AiVector v) => new(v.X, v.Y, 0f);
}
