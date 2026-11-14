using SourceSharp.Abi;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// The world the movement loop asks, answered from the nav's free space
/// (plan_bots_teams_tf2_part_g G.5 step 3): a hull swept through
/// <see cref="VoxelColumns"/>, no engine and no entity. Every solid is a box
/// (a column's footprint, between two free spans or outside them), so every
/// plane it answers is one of the six axis planes: a ramp is the stair its
/// columns make, each riser under a voxel.
///
/// <para>
/// <b>Accuracy.</b> The sweep itself is exact against those boxes, and stops
/// <see cref="DistEpsilon"/> short of the one it hits, as the engine's does,
/// so a body at rest is that far off the floor and never in it. What it
/// sweeps against is as good as the columns: a voxel sideways, the file's
/// floor heights below, up to a voxel low overhead (<see cref="VoxelColumns"/>).
/// </para>
///
/// <para>
/// It holds no state: one world serves any number of threads. There is no
/// water, no ladder, no moving ground and no other player; every surface is
/// the default one.
/// </para>
/// </summary>
public sealed class VoxelMoveWorld(VoxelColumns columns) : IMoveWorld
{
    /// <summary>How far short of a solid a sweep stops (the engine's <c>DIST_EPSILON</c>).</summary>
    public const float DistEpsilon = 0.03125f;

    /// <summary><c>CONTENTS_SOLID</c>: what every hit is.</summary>
    public const int ContentsSolid = 1;

    /// <summary>The surface every floor has: the default material's friction, nothing scaled.</summary>
    public const float SurfaceFriction = 0.8f;

    /// <summary>How far a column's solid runs under its lowest floor and over its highest span.</summary>
    private const float Far = 1e6f;

    public VoxelColumns Columns => columns;

    public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs, uint mask, int collisionGroup)
    {
        // A ray from the hull's centre against each solid grown by the hull's half extents.
        var sweep = new Sweep
        {
            Ex = (maxs.X - mins.X) * 0.5f, Ey = (maxs.Y - mins.Y) * 0.5f, Ez = (maxs.Z - mins.Z) * 0.5f,
            Cx = start.X + (mins.X + maxs.X) * 0.5f, Cy = start.Y + (mins.Y + maxs.Y) * 0.5f, Cz = start.Z + (mins.Z + maxs.Z) * 0.5f,
            Dx = end.X - start.X, Dy = end.Y - start.Y, Dz = end.Z - start.Z,
            Fraction = 1f, Side = -1,
        };

        float voxel = columns.VoxelSize;
        int x0 = columns.ColumnX(MathF.Min(start.X, end.X) + mins.X - DistEpsilon), x1 = columns.ColumnX(MathF.Max(start.X, end.X) + maxs.X + DistEpsilon);
        int y0 = columns.ColumnY(MathF.Min(start.Y, end.Y) + mins.Y - DistEpsilon), y1 = columns.ColumnY(MathF.Max(start.Y, end.Y) + maxs.Y + DistEpsilon);
        float lowZ = MathF.Min(start.Z, end.Z) + mins.Z - DistEpsilon, highZ = MathF.Max(start.Z, end.Z) + maxs.Z + DistEpsilon;

        for (int y = y0; y <= y1; y++)
        {
            float boxY = columns.OriginY + y * voxel;
            for (int x = x0; x <= x1; x++)
            {
                float boxX = columns.OriginX + x * voxel;
                ReadOnlySpan<FreeSpan> spans = columns.Spans(x, y);

                // The column's solids: under the first span, between spans, over the last. A column with no span is solid throughout.
                float under = -Far;
                for (int i = 0; i <= spans.Length; i++)
                {
                    float over = i < spans.Length ? spans[i].Bottom : Far;
                    if (over >= lowZ && under <= highZ)
                        sweep.Clip(boxX, boxY, under, boxX + voxel, boxY + voxel, over);
                    if (i < spans.Length)
                        under = spans[i].Top;
                }
            }
        }

        if (sweep.StartSolid)
        {
            return new MoveTrace
            {
                Fraction = 0f, EndPos = start, PlaneNormal = new Vector(0f, 0f, 1f), PlaneDist = start.Z, StartSolid = true, AllSolid = sweep.AllSolid,
                Contents = ContentsSolid, HasEntity = true, HasGround = true, HitWorld = true,
            };
        }

        if (sweep.Side < 0)
            return MoveTrace.Clear(end);

        float sign = sweep.Side >= 3 ? 1f : -1f;
        Vector normal = (sweep.Side % 3) switch
        {
            0 => new Vector(sign, 0f, 0f),
            1 => new Vector(0f, sign, 0f),
            _ => new Vector(0f, 0f, sign),
        };
        Vector stop = new(start.X + sweep.Dx * sweep.Fraction, start.Y + sweep.Dy * sweep.Fraction, start.Z + sweep.Dz * sweep.Fraction);
        return new MoveTrace
        {
            Fraction = sweep.Fraction, EndPos = stop, PlaneNormal = normal, PlaneDist = stop.Dot(normal),
            Contents = ContentsSolid, HasEntity = true, HasGround = true, HitWorld = true,
        };
    }

    /// <summary>One sweep's ray and the nearest solid it has met so far.</summary>
    private struct Sweep
    {
        public float Ex, Ey, Ez, Cx, Cy, Cz, Dx, Dy, Dz;
        public float Fraction;

        /// <summary>The face stopped at: 0 to 2 the solid's low x, y, z faces, 3 to 5 its high ones; -1 for none.</summary>
        public int Side;

        public bool StartSolid, AllSolid;

        /// <summary>
        /// Clips the ray against one solid box grown by the hull: where it
        /// enters (less <see cref="DistEpsilon"/>) if that is the nearest yet,
        /// or that it starts inside.
        /// </summary>
        public void Clip(float lowX, float lowY, float lowZ, float highX, float highY, float highZ)
        {
            float enter = -1f, leave = 1f;
            int side = -1;
            bool inside = true;

            // How far outside each face the ray starts (positive is outside) and ends.
            if (!Face(lowX - Ex - Cx, -Dx, 0, ref enter, ref leave, ref side, ref inside)
                || !Face(lowY - Ey - Cy, -Dy, 1, ref enter, ref leave, ref side, ref inside)
                || !Face(lowZ - Ez - Cz, -Dz, 2, ref enter, ref leave, ref side, ref inside)
                || !Face(Cx - (highX + Ex), Dx, 3, ref enter, ref leave, ref side, ref inside)
                || !Face(Cy - (highY + Ey), Dy, 4, ref enter, ref leave, ref side, ref inside)
                || !Face(Cz - (highZ + Ez), Dz, 5, ref enter, ref leave, ref side, ref inside))
                return;

            if (enter < leave && enter >= 0f)
            {
                if (enter < Fraction)
                {
                    Fraction = enter;
                    Side = side;
                }

                return;
            }

            if (!inside)
                return;

            // It starts in the solid; it is in it all the way unless it leaves before the end.
            StartSolid = true;
            if (leave <= 0f || leave >= 1f)
                AllSolid = true;
        }

        /// <summary>One face: false when the whole ray is outside it, so the solid is missed.</summary>
        private static bool Face(float startOut, float travel, int face, ref float enter, ref float leave, ref int side, ref bool inside)
        {
            float endOut = startOut + travel;
            if (startOut > 0f && endOut > 0f)
                return false;
            if (startOut <= 0f && endOut <= 0f)
                return true;
            if (startOut > 0f)
                inside = false;

            if (startOut > endOut)
            {
                // Crossing inward: stop short of the face.
                float f = MathF.Max(0f, startOut - DistEpsilon) / (startOut - endOut);
                if (f > enter)
                {
                    enter = f;
                    side = face;
                }
            }
            else
            {
                float f = (startOut + DistEpsilon) / (startOut - endOut);
                if (f < leave)
                    leave = f;
            }

            return true;
        }
    }

    public int PointContents(in Vector point, int slot) => 0;

    public void ResetPointContentsCache(bool optimizations)
    {
    }

    public MoveSurface Surface(int surfaceProps) => new() { Valid = true, Friction = SurfaceFriction, MaxSpeedFactor = 1f, JumpFactor = 1f };

    public void ResetTouchList()
    {
    }

    public void AddToTouched(in MoveTrace trace, in Vector velocity)
    {
    }

    public void StartSound(in Vector origin, string sound)
    {
    }

    public void PlayerSetAnimation(int activity)
    {
    }

    /// <summary>Nobody dies of a landing here: the flight is what is asked for.</summary>
    public bool PlayerFallingDamage(float damage) => true;

    public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force)
    {
    }

    public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps)
    {
    }

    public void Splash()
    {
    }

    public void RumbleEffect(byte index, byte data, byte flags)
    {
    }

    /// <summary>The stuck check's clock: fixed, so a run is the same run every time.</summary>
    public float EngineTime() => 0f;

    public void ResetDuckLatched()
    {
    }

    public void SetPlayerSurface(int material)
    {
    }
}
