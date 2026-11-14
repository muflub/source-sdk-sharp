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
/// THE SEAM WHERE A ROBOT'S ACTIONS GO (plan_descent_logic 3.8, HO7): once per
/// intent that carries one, the action its brain chose for the body (fire,
/// melee, an ability). Attacks are HO7's; until it lands the default counts
/// and drops them.
/// </summary>
public interface IRobotActionSink
{
    void Act(int body, in ActionIntent action);
}

/// <summary>The action sink until HO7: counts each kind and does nothing.</summary>
[SideService(Side.Server, As = [typeof(IRobotActionSink)])]
public sealed class RobotActionTally : IRobotActionSink
{
    private readonly long[] _byKind = new long[8];

    public long Actions { get; private set; }

    public long Of(ActionKind kind) => (uint)kind < (uint)_byKind.Length ? _byKind[(int)kind] : 0;

    public void Act(int body, in ActionIntent action)
    {
        Actions++;
        if ((uint)action.Kind < (uint)_byKind.Length)
            _byKind[(int)action.Kind]++;
    }
}
