using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>The world's own answers the validator compares the nav with.</summary>
public interface IWorldProbe
{
    /// <summary>Whether a box <paramref name="width"/> wide and <paramref name="height"/> tall stands clear with its feet at <paramref name="feet"/>.</summary>
    bool BoxClear(AiVector feet, float width, float height);

    /// <summary>Whether a box <paramref name="half"/> to each side, swept from <paramref name="from"/> to <paramref name="to"/>, meets solid on the way (or starts in it).</summary>
    bool Meets(AiVector from, AiVector to, AiVector half);

    /// <summary>What the last ask found, for a mismatch's line (an entity's class, "world"), or empty.</summary>
    string LastHit { get; }

    /// <summary>Whether the last ask that found something found the world (brushes, displacements, static props), not an entity.</summary>
    bool LastHitWorld { get; }
}

/// <summary>
/// One arm of the validator (plan_hl2mp_ai §6.3): how many asks, how many the
/// world disagreed with, and the arm's own ceiling.
/// </summary>
public sealed record NavValidationArm(string Name, int Asked, int Bad, double Ceiling)
{
    /// <summary>The share of asks the world disagreed with.</summary>
    public double Rate => Asked == 0 ? 0 : (double)Bad / Asked;

    /// <summary>An arm that asked nothing proves nothing, so it fails.</summary>
    public bool Passed => Asked > 0 && Rate <= Ceiling;
}

/// <summary>
/// What a validation found, by arm (ruled 2026-11-02, §6.3, §12.4):
/// <list type="bullet">
/// <item><see cref="Standing"/>: the nav stands a player where the world blocks one; at most 0.5 %.</item>
/// <item><see cref="Floor"/>: the nav stands a player where the world has no ground within the step height and a voxel below (a phantom floor, which routes a bot over air); at most 0.5 %.</item>
/// <item><see cref="Solid"/>: the nav is solid where the world is open; at most 3 %, drift evidence only, since map-tools over-estimates solids by design (format §17.6).</item>
/// </list>
/// </summary>
public sealed record NavValidation(NavValidationArm Standing, NavValidationArm Floor, NavValidationArm Solid, IReadOnlyList<string> Examples, int EntityInTheWay = 0)
{
    /// <summary>The standing arm's ceiling.</summary>
    public const double StandingCeiling = 0.005;

    /// <summary>The floor arm's ceiling.</summary>
    public const double FloorCeiling = 0.005;

    /// <summary>The solid arm's ceiling: drift evidence, not a geometry gate.</summary>
    public const double SolidCeiling = 0.03;

    public IReadOnlyList<NavValidationArm> Arms => [Standing, Floor, Solid];

    /// <summary>Every arm within its ceiling.</summary>
    public bool Passed => Standing.Passed && Floor.Passed && Solid.Passed;
}

/// <summary>
/// The nav validator (plan_hl2mp_ai §6.3), evidence only: for
/// <paramref name="n"/> seeded random standing cells, the world must hold a
/// standing player at the cell's centre; for <paramref name="n"/> more, the
/// world must have ground under the cell's floor; for <paramref name="n"/> seeded
/// solid points next to cells (the column beside at chest height when the
/// file says it is solid there, else under the floor), the world must be
/// solid. A nav built from geometry that drifted from the shipped map shows
/// as mismatches. Cells a dynamic obstacle takes are skipped: the file keeps
/// them open, the world has the entity in them.
/// </summary>
public static class NavValidator
{
    private const int MaxExamples = 12;

    /// <summary>
    /// The standing box's width: the player's 32, less half a unit a side.
    /// The engine's box traces meet a wall a fraction of a unit before the
    /// file's exact thresholds do (dm_lockdown, n = 2,000: 25 cells not clear
    /// at 32, 6 at 31, all a 32-wide gap the file fits exactly).
    /// </summary>
    public const float Width = 31f;

    /// <summary>The height the engine steps a player up by (sv_stepsize).</summary>
    public const float StepBand = 18f;

    public static NavValidation Run(DerivedNav nav, INavSource src, IVoxelSpace space, IWorldProbe world, int n, ulong seed, float width = Width)
    {
        ArgumentNullException.ThrowIfNull(nav);
        NavGraph g = nav.Graph;
        var obstacleCell = new bool[g.CellCount];
        for (int c = 0; c < g.CellCount; c++)
        {
            foreach (NavEdge e in g.EdgesFrom(c))
            {
                if (e.Obstacle >= 0)
                    obstacleCell[e.From] = obstacleCell[e.To] = true;
            }
        }

        int[] standing = [.. Enumerable.Range(0, g.CellCount).Where(c => g.Cell(c).Admits(ClearanceClass.Stand)
                                                                       && (g.Cell(c).Flags & NavCellFlags.Ladder) == 0 && !obstacleCell[c])];
        var examples = new List<string>();
        var rng = new AiRandom(seed);
        int standingBad = 0, floorBad = 0, solidBad = 0, entities = 0, asked = Math.Min(n, standing.Length);
        for (int i = 0; i < asked; i++)
        {
            // The box above the step band: the engine steps a player up out
            // of a slope or a lip under its feet, so the floor's own column
            // answers for the first step height (a displacement's surface
            // rises inside a 16-unit column).
            NavCell cell = g.Cell(standing[(int)(rng.NextUInt64() % (ulong)standing.Length)]);
            AiVector feet = cell.Centre + new AiVector(0, 0, StepBand);
            if (!world.BoxClear(feet, width, 72f - StepBand))
            {
                // A player, a weapon or a charger on the spot is not the
                // geometry the file was built from: counted apart.
                if (!world.LastHitWorld)
                {
                    entities++;
                    continue;
                }

                standingBad++;
                Note(examples, $"stand at ({feet.X:F0} {feet.Y:F0} {feet.Z:F0}) is not clear: {world.LastHit}");
            }
        }

        // The floor arm: a slab the player's width swept down from just above
        // the cell's floor through the step height and a voxel more must meet
        // ground. Open all the way is a phantom floor: the nav stands a player
        // on air (any surface counts, a brush entity's too).
        for (int i = 0; i < asked; i++)
        {
            NavCell cell = g.Cell(standing[(int)(rng.NextUInt64() % (ulong)standing.Length)]);
            float voxel = cell.MaxX - cell.MinX;
            AiVector top = cell.Centre + new AiVector(0, 0, 1f);
            AiVector bottom = cell.Centre - new AiVector(0, 0, StepBand + voxel);
            float r = width * 0.5f;
            // A slab, then a ray at the centre: a thin support (a pipe) the
            // slab's sweep can slip past still answers the ray (ai_nav_probe).
            if (!world.Meets(top, bottom, new AiVector(r, r, 0.4f)) && !world.Meets(top, bottom, new AiVector(0.05f, 0.05f, 0.05f)))
            {
                floorBad++;
                Note(examples, $"floor at ({top.X:F0} {top.Y:F0} {cell.FloorZ:F0}) has no ground within {StepBand + voxel:F0} below");
            }
        }

        for (int i = 0; i < asked; i++)
        {
            int at = standing[(int)(rng.NextUInt64() % (ulong)standing.Length)];
            NavCell cell = g.Cell(at);
            AiVector c = cell.Centre;
            float w = cell.MaxX - cell.MinX;
            float bottom = src.Leaf(nav.LeafOfCell[at]).BottomZ;
            // What the file says is solid (some solid overlaps the voxel): the
            // voxel beside the cell at chest height when the file has it
            // solid, else the voxel under the cell's run, which holds its
            // floor. A slab is swept from the cell's free column across the
            // voxel: a displacement is a surface, which a box can only meet.
            AiVector from = new(c.X, c.Y, bottom + 0.5f), to = new(c.X, c.Y, bottom - w);
            AiVector half = new(w * 0.5f - 0.1f, w * 0.5f - 0.1f, 0.4f);
            int side = (int)(rng.NextUInt64() % 4);
            for (int k = 0; k < 4; k++)
            {
                (float dx, float dy) = ((side + k) % 4) switch { 0 => (1f, 0f), 1 => (0f, 1f), 2 => (-1f, 0f), _ => (0f, -1f) };
                var beside = new AiVector(c.X + dx * w, c.Y + dy * w, cell.FloorZ + 40f);
                if (!space.Free(beside))
                {
                    float z = Snap(beside.Z, w);
                    from = new AiVector(c.X, c.Y, z);
                    to = new AiVector(c.X + dx * w * 1.5f, c.Y + dy * w * 1.5f, z);
                    half = new AiVector(dx != 0f ? 0.4f : w * 0.5f - 0.1f, dy != 0f ? 0.4f : w * 0.5f - 0.1f, w * 0.5f - 0.1f);
                    break;
                }
            }

            AiVector p = to;
            if (!world.Meets(from, to, half))
            {
                solidBad++;
                Note(examples, $"solid at ({p.X:F0} {p.Y:F0} {p.Z:F0}) is open (cell floor {cell.FloorZ:F0})");
            }
        }

        return new NavValidation(new NavValidationArm("standing", asked - entities, standingBad, NavValidation.StandingCeiling),
                                 new NavValidationArm("floor", asked, floorBad, NavValidation.FloorCeiling),
                                 new NavValidationArm("solid", asked, solidBad, NavValidation.SolidCeiling), examples, entities);
    }

    /// <summary>The centre of the <paramref name="size"/> voxel holding <paramref name="v"/> (the grid's origin is a multiple of the voxel).</summary>
    private static float Snap(float v, float size) => MathF.Floor(v / size) * size + size * 0.5f;

    private static void Note(List<string> examples, string line)
    {
        if (examples.Count < MaxExamples)
            examples.Add(line);
    }
}

/// <summary><see cref="IWorldProbe"/> over the engine's hull traces (main thread only), as the Phase 0 probe measured them.</summary>
public sealed class EngineWorldProbe(ITraceQueries traces, IEngineEntities entities) : IWorldProbe
{
    public string LastHit { get; private set; } = "";

    public bool LastHitWorld { get; private set; }

    private TraceAnswer Remember(TraceAnswer t)
    {
        LastHitWorld = t.Entity == EntityHandle.None || entities.GetIndex(t.Entity) == 0;
        LastHit = t.Entity == EntityHandle.None ? "nothing" : LastHitWorld ? "world" : entities.GetClassName(t.Entity);
        return t;
    }

    // What a moving player collides with: brushes, clips, grates, windows, and other players.
    private const uint MaskPlayerSolid = GameMovement.MaskPlayerSolid;

    public bool BoxClear(AiVector feet, float width, float height)
    {
        float r = width * 0.5f;
        var start = new Vector(feet.X, feet.Y, feet.Z);
        TraceAnswer t = Remember(traces.Hull(start, start with { Z = feet.Z + 1f }, new Vector(-r, -r, 0f), new Vector(r, r, height),
                                             MaskPlayerSolid, EntityHandle.None));
        return !t.StartSolid && t.Fraction >= 1f;
    }

    public bool Meets(AiVector from, AiVector to, AiVector half)
    {
        TraceAnswer t = Remember(traces.Hull(new Vector(from.X, from.Y, from.Z), new Vector(to.X, to.Y, to.Z), new Vector(-half.X, -half.Y, -half.Z),
                                             new Vector(half.X, half.Y, half.Z), MaskPlayerSolid, EntityHandle.None));
        return t.StartSolid || t.Fraction < 1f;
    }
}
