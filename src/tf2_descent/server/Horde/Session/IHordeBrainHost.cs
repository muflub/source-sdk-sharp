using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Far;

namespace TF2.Descent.Server.Horde.Session;

/// <summary>
/// The horde controller's calls into its AI session (plan_descent_logic HO4):
/// <see cref="Begin"/> once the controller has begun (its roster, pool,
/// bodies and level exist), <see cref="Tick"/> each server tick before the
/// controller's own decide, <see cref="TakeFarSounds"/> for the far
/// simulation's tick, and <see cref="End"/> first thing when the controller
/// ends, while the level's data is still held: the workers are stopped and
/// joined there, before anything they read is released.
/// </summary>
public interface IHordeBrainHost
{
    void Begin(HordeController horde, long now);

    void Tick(long now);

    /// <summary>The sounds heard since the far simulation last took them (B.3: the same sounds the brains hear), valid until the next tick.</summary>
    ReadOnlySpan<SoundEvent> TakeFarSounds();

    void End();
}

/// <summary>
/// A taunt told to a robot's brain (HO8's status sink, a hero's threat skill): the session's
/// answer is a Damaged of nothing with the hero as its source.
/// </summary>
public interface IRobotTaunts
{
    void Taunted(TF2.Descent.Ai.Roster.RobotId robot, int body, int seat);
}

/// <summary>
/// THE SEAM WHERE A ROBOT'S ACTIONS GO (plan_descent_logic 3.8, HO7): once per
/// intent that carries one, the action its brain chose for the body (fire,
/// melee, an ability), on the game thread. The session hands it the level's
/// controller at <see cref="Begin"/>, a <see cref="Tick"/> each server tick
/// before the tick's intents (windups land, beams heal, projectiles are
/// counted there), and <see cref="End"/> when the level goes. The horde's
/// answer is <c>Attacks.RobotAttacks</c>.
/// </summary>
public interface IRobotActionSink
{
    void Begin(HordeController horde, long now);

    void Tick(long now);

    void Act(int body, in ActionIntent action);

    void End();
}
