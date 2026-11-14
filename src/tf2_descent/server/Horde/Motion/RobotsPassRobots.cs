using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// Robots are one team, as MvM's are, and a robot body does not block another
/// (plan_descent_logic HO3; stock TF2: teammates pass through each other,
/// <c>tf_avoidteammates</c>, and push apart where they overlap, which
/// <see cref="RobotMotion"/>'s separation does). A member of the server's
/// per-entity trace chain (<see cref="IEntityCollideRule"/>): a trace that
/// passes a robot body (its motion's step and accept, the audit's place test,
/// a robot's own attack traces) does not hit another robot body. Every other
/// trace is unchanged: a hero, a rocket, a bullet still hit robots, and a
/// robot's sweeps still hit the world, props and heroes (a hero in a doorway
/// still stops a robot, ruling L9).
/// </summary>
[SideService(Side.Server)]
public sealed class RobotsPassRobots(RobotMotion motion) : IEntityCollideRule, IOrderedHook
{
    public const int Order = ModStages.First + 530;

    public int HookOrder => Order;

    public bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask)
        => mover == EntityHandle.None || motion.BodyOf(mover) == 0 || motion.BodyOf(entity) == 0;
}
