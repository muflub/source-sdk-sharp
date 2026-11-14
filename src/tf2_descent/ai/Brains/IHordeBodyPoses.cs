using SourceSharp.Ai;

namespace TF2.Descent.Ai.Brains;

/// <summary>Where each lent body is now (the server's bodies; a fake in facts). Asked for lent bodies only.</summary>
public interface IHordeBodyPoses
{
    AiVector Position(int body);

    /// <summary>The body's facing, degrees.</summary>
    float Yaw(int body);
}

/// <summary>
/// Where the brains' intents go (the server's motion and action seams; a
/// recorder in facts): a body's move and look until <c>expiresTick</c>, a hold,
/// and an action (fire, melee, an ability) once per intent that carries one.
/// </summary>
public interface IHordeOrders
{
    void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick);

    void Hold(int body);

    void Act(int body, in ActionIntent action);
}

/// <summary>A hero this tick: its seat (player index), feet, eye and room.</summary>
public readonly record struct HeroPose(int Seat, AiVector Feet, AiVector Eye, int Room);

/// <summary>
/// A sound the horde may hear (plan_tf2_rpg B.3): what it was, where, how far
/// it carries in units of walk (its reach), the hero seat that made it (-1 for
/// none) and the room it is in (-1 for none, which no robot hears).
/// </summary>
public readonly record struct HordeSound(SoundKind Kind, AiVector Position, float Reach, int Seat, int Room);
