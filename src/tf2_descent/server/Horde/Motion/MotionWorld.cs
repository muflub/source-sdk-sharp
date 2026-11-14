using SourceSharp.Abi;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// One hull sweep as the motion reads it: whether it struck, how far it got and
/// where it stopped, the face it stopped on, whether it began (and stayed) in
/// solid, and what it struck: the entity, its index (0 for the world, which is
/// also what a static prop reports) and whether it is a player.
/// </summary>
public readonly record struct MotionTrace(
    bool Hit,
    float Fraction,
    Vector End,
    Vector Normal,
    bool StartSolid,
    bool AllSolid,
    EntityHandle Entity,
    int EntityIndex,
    bool IsPlayer)
{
    public static MotionTrace Clear(in Vector end) => new(false, 1f, end, Vector.Zero, false, false, EntityHandle.None, -1, false);
}

/// <summary>
/// What a robot body's motion asks of the world (plan_descent_logic 3.7): a
/// hull swept from one place to another, through the world, props, the other
/// bodies and the heroes, passing through <paramref name="ignore"/> (the body
/// itself). The movement step asks it for each trace of a command; the accept
/// asks it once a body a tick.
/// </summary>
public interface IMotionWorld
{
    MotionTrace Sweep(in Vector start, in Vector end, in Vector mins, in Vector maxs, EntityHandle ignore);
}

/// <summary>
/// The engine's answer, on the game thread only: <see cref="TraceMask.Solid"/>
/// (solid brushes, moveables, windows, monsters, gratings: the world, props,
/// bodies and players) through <see cref="IEngineWorldQuery.TraceBox"/>. That
/// trace reports no start-in-solid, so a sweep that stopped at once asks
/// <see cref="IEngineWorldQuery.EntityTraceHull"/> the same question with the
/// same contents, which does: one more trace, only then.
/// </summary>
[SideService(Side.Server, As = [typeof(IMotionWorld)])]
public sealed class EngineMotionWorld(IEngineWorldQuery world, IEngineEntities entities) : IMotionWorld
{
    /// <summary><c>MASK_SOLID</c>: CONTENTS_SOLID | MOVEABLE | WINDOW | MONSTER | GRATE (bspflags.h), the contents <see cref="TraceMask.Solid"/> names.</summary>
    public const uint MaskSolid = 0x1u | 0x4000u | 0x2u | 0x2000000u | 0x8u;

    public MotionTrace Sweep(in Vector start, in Vector end, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        if (start.X == end.X && start.Y == end.Y && start.Z == end.Z)
        {
            // A place test (the loop's stuck check, the motion audit): only the start-in-solid answer means anything.
            var p = world.EntityTraceHull(ignore, start, end, mins, maxs, MaskSolid, 0);
            if (!p.StartSolid)
                return MotionTrace.Clear(end);
            int at = p.Entity == EntityHandle.None ? 0 : p.Entity.EntryIndex;
            return new MotionTrace(true, 0f, start, Vector.Zero, true, p.AllSolid, p.Entity, at, at > 0 && entities.IsPlayer(p.Entity));
        }

        var t = world.TraceBox(start, end, mins, maxs, TraceMask.Solid, ignore);
        if (!t.Hit)
            return MotionTrace.Clear(end);

        bool startSolid = false, allSolid = false;
        if (t.Fraction <= 0f)
        {
            var s = world.EntityTraceHull(ignore, start, end, mins, maxs, MaskSolid, 0);
            startSolid = s.StartSolid;
            allSolid = s.AllSolid;
        }

        var hit = t.Entity;
        int index = hit == EntityHandle.None ? 0 : hit.EntryIndex;
        bool player = index > 0 && entities.IsPlayer(hit);
        return new MotionTrace(true, t.Fraction, t.End, t.Normal, startSolid, allSolid, hit, index, player);
    }
}
