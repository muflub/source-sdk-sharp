using SourceSharp.Ai;
using TF2.Descent.Ai.Bodies;

namespace TF2.Descent.Ai.Roster;

public sealed partial class RobotRoster
{
    /// <summary>The row's live state and carried memory, as a body would hold them.</summary>
    public RobotBodyState BodyState(int i)
    {
        Row(i);
        return new RobotBodyState(_room[i], _position[i], _yaw[i], _life[i], _statuses[i], _clip[i], Carried(i));
    }

    /// <summary>
    /// A robot takes a body (plan_descent_logic 3.3): the row becomes Embodied on
    /// <paramref name="lease"/>, and what the body starts with is the row's own room, position,
    /// yaw, life, statuses, clip and carried memory, unchanged. Only a far robot with no body
    /// can be promoted.
    /// </summary>
    public RobotBodyState Promote(int i, BodyLease lease, long now)
    {
        Row(i);
        if (_state[i] != RobotState.Far)
            throw new InvalidOperationException($"{Id(i)} is {_state[i]}: only a far robot takes a body");
        if (!lease.IsValid) throw new ArgumentException("a promotion needs a body", nameof(lease));
        _state[i] = RobotState.Embodied;
        _body[i] = lease.Body;
        _bodySerial[i] = lease.Serial;
        _lastNear[i] = now;
        _dwellUntil[i] = 0;
        return BodyState(i);
    }

    /// <summary>
    /// A robot gives its body up (plan_descent_logic 3.3): the body's live state and carried
    /// memory are saved to the row as they are, the body is cleared, and the row is Far, or
    /// stays Dead. Returns the lease to hand back to the pool.
    /// </summary>
    public BodyLease Demote(int i, in RobotBodyState end, long now)
    {
        Row(i);
        if (_state[i] is not (RobotState.Embodied or RobotState.Dead) || _body[i] == 0)
            throw new InvalidOperationException($"{Id(i)} is {_state[i]} with body {_body[i]}: only a robot holding a body gives one up");
        var lease = new BodyLease(_body[i], _bodySerial[i]);
        _room[i] = end.Room;
        _position[i] = end.Position;
        _yaw[i] = end.Yaw;
        _life[i] = end.Life;
        _statuses[i] = end.Statuses;
        Refresh(i, now);
        _clip[i] = end.Clip;
        _targetSeat[i] = end.Carried.TargetSeat;
        _lastKnown[i] = end.Carried.LastKnownPosition;
        _lastKnownTick[i] = end.Carried.LastKnownTick;
        _alert[i] = end.Carried.Alert;
        _threatSeat[i] = end.Carried.ThreatSeat;
        _threat[i] = end.Carried.Threat;
        _body[i] = 0;
        _bodySerial[i] = 0;
        _state[i] = _state[i] == RobotState.Dead || end.Life <= 0 ? RobotState.Dead : RobotState.Far;
        return lease;
    }

    /// <summary>Sets the memory a far robot carries (the far simulation's hearing; a fact's setup).</summary>
    public void SetCarried(int i, in CarriedMemory carried)
    {
        Row(i);
        _targetSeat[i] = carried.TargetSeat;
        _lastKnown[i] = carried.LastKnownPosition;
        _lastKnownTick[i] = carried.LastKnownTick;
        _alert[i] = carried.Alert;
        _threatSeat[i] = carried.ThreatSeat;
        _threat[i] = carried.Threat;
    }
}
