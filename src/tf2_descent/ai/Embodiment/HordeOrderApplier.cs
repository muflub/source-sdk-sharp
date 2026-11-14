using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;

namespace TF2.Descent.Ai.Embodiment;

/// <summary>
/// The bodies as the horde's data side sees them (the server's pool of
/// <c>tf2_descent_robot</c> entities, HO2; a fake in facts). <see cref="Begin"/> puts a body out
/// for a robot with the state it starts with; <see cref="End"/> parks it and answers the state
/// it ends with.
/// </summary>
public interface IRobotBodies
{
    void Begin(BodyLease lease, RobotId robot, in RobotBodyState start);

    RobotBodyState End(BodyLease lease, RobotId robot);
}

/// <summary>
/// Carries the embodiment rules' orders out on the roster, the pool and the bodies, in the
/// order given: a wake or sleep changes the room, a promotion lends the body and hands it the
/// row's state, a demotion or release takes the body's state back to the row and returns the
/// body. An order for a robot whose id has gone stale throws.
/// </summary>
public sealed class HordeOrderApplier
{
    private readonly RobotRoster _roster;
    private readonly BodyPool _pool;
    private readonly IRobotBodies _bodies;

    public HordeOrderApplier(RobotRoster roster, BodyPool pool, IRobotBodies bodies)
    {
        _roster = roster;
        _pool = pool;
        _bodies = bodies;
    }

    public void Apply(HordeOrders orders, long now)
    {
        foreach (ref readonly var o in orders.AsSpan())
        {
            switch (o.Kind)
            {
                case HordeOrderKind.WakeRoom:
                    _roster.WakeRoom(o.Room);
                    break;
                case HordeOrderKind.SleepRoom:
                    _roster.SleepRoom(o.Room);
                    break;
                case HordeOrderKind.Release:
                case HordeOrderKind.Demote:
                {
                    int i = Live(o.Robot);
                    var lease = _roster.Lease(i);
                    if (lease.Body != o.Body) throw new InvalidOperationException($"{o.Robot} holds body {lease.Body}, not {o.Body}");
                    var end = _bodies.End(lease, o.Robot);
                    _pool.Return(_roster.Demote(i, end, now));
                    break;
                }
                case HordeOrderKind.Promote:
                {
                    int i = Live(o.Robot);
                    var lease = _pool.Lend(o.Body, o.Robot);
                    var start = _roster.Promote(i, lease, now);
                    _bodies.Begin(lease, o.Robot, start);
                    break;
                }
            }
        }
    }

    private int Live(RobotId id) =>
        _roster.IsLive(id) ? id.Index : throw new InvalidOperationException($"{id} is not on the roster");
}
