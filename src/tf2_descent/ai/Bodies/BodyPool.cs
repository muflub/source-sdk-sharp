using SourceSharp.Ai;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Horde;

namespace TF2.Descent.Ai.Bodies;

/// <summary>
/// A body lent to a robot: the body's index (1 to the pool's size, then the boss's; the AI
/// session's slot) and the serial of this lend. The serial changes on every lend, so a lease
/// from an earlier lend no longer returns the body.
/// </summary>
public readonly record struct BodyLease(int Body, int Serial)
{
    public bool IsValid => Body != 0;

    /// <summary>The AI session's actor for this lend (plan_descent_logic 3.6: body index is slot index).</summary>
    public ActorId Actor => new(Body, Serial);
}

/// <summary>
/// The fixed pool of robot bodies (plan_descent_logic 3.2, 3.6; E1): bodies 1 to
/// <see cref="PoolBodies"/> for any robot, and <see cref="PoolBodies"/> + 1 onwards for the
/// boss alone. Index 0 is never a body. A body is lent to one robot at a time; each lend gives
/// it a new serial. Data only: the server keeps an entity per body index.
/// </summary>
public sealed class BodyPool
{
    private readonly int _poolBodies;
    private readonly int[] _serial;     // by body index; [0] unused
    private readonly RobotId[] _holder; // by body index; None when free

    public BodyPool(int poolBodies, int bossBodies)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(poolBodies, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(bossBodies, 0);
        _poolBodies = poolBodies;
        _serial = new int[poolBodies + bossBodies + 1];
        _holder = new RobotId[poolBodies + bossBodies + 1];
    }

    /// <summary>A pool sized by horde.json.</summary>
    public static BodyPool For(HordeSettings settings) => new(settings.PoolBodies, settings.BossBodies);

    /// <summary>Bodies any robot may take.</summary>
    public int PoolBodies => _poolBodies;

    /// <summary>The highest body index (the last boss body, or the last pool body when there is none).</summary>
    public int LastBody => _serial.Length - 1;

    /// <summary>The boss's body, 0 when the pool has none.</summary>
    public int BossBody => LastBody > _poolBodies ? _poolBodies + 1 : 0;

    public bool IsBossBody(int body) => body > _poolBodies && body <= LastBody;

    public bool IsFree(int body) => !_holder[Check(body)].IsValid;

    /// <summary>The robot holding <paramref name="body"/>, None when it is free.</summary>
    public RobotId Holder(int body) => _holder[Check(body)];

    /// <summary>The serial of <paramref name="body"/>'s latest lend (0 before its first).</summary>
    public int Serial(int body) => _serial[Check(body)];

    /// <summary>How many pool bodies (not the boss's) are free.</summary>
    public int FreePoolBodies
    {
        get
        {
            int n = 0;
            for (int b = 1; b <= _poolBodies; b++)
                if (!_holder[b].IsValid) n++;
            return n;
        }
    }

    /// <summary>Lends <paramref name="body"/> to <paramref name="robot"/>. Throws when it is lent already: a body is never lent twice.</summary>
    public BodyLease Lend(int body, RobotId robot)
    {
        Check(body);
        if (!robot.IsValid) throw new ArgumentException("a body is lent to a robot", nameof(robot));
        if (_holder[body].IsValid)
            throw new InvalidOperationException($"body {body} is lent to {_holder[body]}; it cannot also go to {robot}");
        _holder[body] = robot;
        return new BodyLease(body, ++_serial[body]);
    }

    /// <summary>Takes a body back. Throws for a lease that is not the body's current lend.</summary>
    public void Return(BodyLease lease)
    {
        Check(lease.Body);
        if (!_holder[lease.Body].IsValid || _serial[lease.Body] != lease.Serial)
            throw new InvalidOperationException($"body {lease.Body} serial {lease.Serial} is not lent out (current serial {_serial[lease.Body]})");
        _holder[lease.Body] = RobotId.None;
    }

    private int Check(int body)
    {
        if (body < 1 || body > LastBody)
            throw new ArgumentOutOfRangeException(nameof(body), body, $"bodies are 1 to {LastBody}");
        return body;
    }
}
