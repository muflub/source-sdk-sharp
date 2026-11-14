using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Rules.Robots;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// A blast pushes a robot as it pushes a player (plan_descent_logic L12, HO3):
/// a member of the hit chain (<see cref="IRobotHitSink"/>) that turns a blast
/// hit on a living, lent body into a push on its motion
/// (<see cref="IRobotImpulse"/>), of the rules' size
/// (<see cref="BlastPushRules.VelocityChange"/>: the hit's damage over the
/// robot's scaled hull, by the force scale, capped, less its knockback
/// resistance, halved for a Heavy) along the hit's force (the direction stock
/// gives the push: away from the blast). The damage itself is not touched.
///
/// <para>
/// Every blast is another's: no robot fires yet, so none is its own (the
/// self-blast scales are the rules', for HO6 and HO12's blast jumps). A bullet
/// or a swing does not push here (HO8 clamps those first). A hit that killed
/// pushes nothing: the body goes back to the pool.
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class RobotBlastPush(IHordeRules rules, RobotMotion motion) : IRobotHitSink, IOrderedHook
{
    public const int Order = ModStages.First + 520;

    public int HookOrder => Order;

    /// <summary>Blasts turned into pushes, and the last push given.</summary>
    public long Pushed { get; private set; }

    public AiVector LastPush { get; private set; }

    public void OnRobotHit(in RobotHit hit)
    {
        if (hit.Killed || hit.DamageType != RobotDamageKinds.Blast)
            return;
        if (hit.Force.X == 0f && hit.Force.Y == 0f && hit.Force.Z == 0f)
            return;
        if (!motion.TryBody(hit.Body, out var body) || body.Robot != hit.Robot)
            return;

        var v = rules.Rules.BlastPush.VelocityChange(new BlastForce(new Vec3(hit.Force.X, hit.Force.Y, hit.Force.Z), hit.Dealt, BlastOwner.Other),
                                                     body.Scale, body.KnockbackResistPct, body.Heavy);
        var push = new AiVector((float)v.X, (float)v.Y, (float)v.Z);
        if (motion.Push(hit.Body, push))
        {
            Pushed++;
            LastPush = push;
        }
    }
}

/// <summary>
/// Hands a hit to every member of the hit chain in order: what the bodies are
/// given as their one sink. A plain object (not a service), so it is not a
/// member itself.
/// </summary>
public sealed class RobotHitChain(IHooks<IRobotHitSink> hooks) : IRobotHitSink
{
    public void OnRobotHit(in RobotHit hit)
    {
        var all = hooks.All;
        for (int k = 0; k < all.Count; k++)
            all[k].OnRobotHit(in hit);
    }
}
