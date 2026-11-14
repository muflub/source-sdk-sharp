using SourceSharp.Ai;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// The seam between a robot's brain and its body's motion (plan_descent_logic
/// 3.6 and 3.7): the horde's session hands each embodied robot's move and look
/// here, in the AI core's own intent types, and the motion keeps carrying them
/// out for that body, tick after tick, until the next order or the tick the
/// order expires at, whichever is first. A body that is parked or dead has no
/// motion: an order for one is dropped.
///
/// <para>
/// <paramref name="body"/> is the pooled body's index (1 to 48, and the boss
/// body), which is also the brain's slot in the horde's session. The corners of
/// a <see cref="MoveIntent"/> are world positions on the level's navigation,
/// nearest first. A look that names an actor is resolved against the actor's
/// position now, each tick, through <see cref="IHordeActors"/>, so a robot's
/// aim is never a tick stale.
/// </para>
/// </summary>
public interface IRobotMotion
{
    /// <summary>The move and the look <paramref name="body"/> carries out from now until <paramref name="expiresTick"/>.</summary>
    void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick);

    /// <summary>Stop where it stands and look where it looks, until the next order.</summary>
    void Hold(int body);
}

/// <summary>
/// Where an actor of the horde's session is right now: a hero or another
/// robot, by the session's own id. The session answers it; the motion asks it
/// each tick for the actor a look names. False for an actor that is gone.
/// </summary>
public interface IHordeActors
{
    bool TryEye(ActorId actor, out AiVector eye);
}
