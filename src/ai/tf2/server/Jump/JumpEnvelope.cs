using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// How far a blast jump reaches, as the simulator gives it
/// (plan_bots_teams_tf2_part_g G.5 step 4: nothing here is a number of the
/// plan's or of a measurement in the game, so it follows the blast's physics
/// when they change). Every recipe of a search's coarse grids is flown in
/// open air over a flat floor, steered straight along its way:
/// <paramref name="Rise"/> is the highest any rose over its takeoff,
/// <paramref name="Range"/> the furthest any came down from it, and
/// <paramref name="Flights"/> the flights that took.
/// </summary>
public readonly record struct JumpEnvelope(float Rise, float Range, int Flights)
{
    /// <summary>How far along its way an envelope's flight is steered for: past where any comes down, so none brakes.</summary>
    public const float Far = 100_000f;

    /// <summary>Open air over a flat floor at 0: 16,384 by 2,048 and 8,192 tall.</summary>
    public static VoxelColumns OpenAir() => VoxelColumns.Of(0f, 0f, 64f, 256, 32, static (_, _) => [new FreeSpan(0f, 8192f)]);

    /// <summary>Where an envelope's flights take off in <see cref="OpenAir"/>, heading east.</summary>
    public static Vector Takeoff => new(1024f, 1024f, VoxelMoveWorld.DistEpsilon);

    /// <summary>
    /// The envelope of <paramref name="settings"/>' grids, flown by
    /// <paramref name="open"/>: a pilot in the loop over a simulator of
    /// <see cref="OpenAir"/>.
    /// </summary>
    public static JumpEnvelope Measure(JumpFlier open, JumpSearchSettings settings)
    {
        float rise = 0f, range = 0f;
        int flights = 0;
        Vector takeoff = Takeoff, landing = takeoff + new Vector(Far, 0f, 0f);
        for (int f = 0; f < 2; f++)
        {
            JumpGrid grid = f == 0 ? settings.Up : settings.Forward;
            for (int p = 0; p < grid.Pitches; p++)
            {
                for (int d = 0; d < grid.Delays; d++)
                {
                    var recipe = new JumpRecipe(takeoff, landing, grid.RunUp, grid.PitchFrom + p * grid.PitchStep, settings.YawOffset,
                                                grid.DelayFrom + d * grid.DelayStep, Ducked: true);
                    JumpOutcome outcome = open.Fly(recipe, recipe.Start(), settings.MaxCommands);
                    flights++;
                    if (!outcome.Flew || outcome.BlastDamage <= 0f)
                        continue;
                    rise = MathF.Max(rise, outcome.Apex);
                    range = MathF.Max(range, outcome.Touch.X - takeoff.X);
                }
            }
        }

        return new JumpEnvelope(rise, range, flights);
    }
}
