using SourceSharp.Abi;

using TF2.Shared;

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
/// The engine's answer, on the game thread only: <c>MASK_SOLID</c> (solid
/// brushes, moveables, windows, monsters, gratings: the world, props, bodies and
/// players) and the heroes' team's contents bit, in the player-movement group,
/// through <see cref="IEngineWorldQuery.TraceBoxInGroup"/>: a robot body collides
/// as a TF player's movement does (stock TracePlayerBBox: PlayerSolidMask, which
/// adds the other team's bit, and COLLISION_GROUP_PLAYER_MOVEMENT), so it passes
/// debris (a dead robot's tf_ammo_pack, a dropped weapon) where it stopped
/// against it in COLLISION_GROUP_NONE (live, 2026-11-16: a x1.5 pyro 20 s against
/// a pack in each of 5 trials), and a hero still stops it (the team rule hits a
/// player in that group only for a mask with his team's bit). That trace reports
/// no start-in-solid, so a sweep that stopped at once asks
/// <see cref="IEngineWorldQuery.EntityTraceHull"/> the same question with the
/// same contents and group, which does: one more trace, only then.
/// </summary>
[SideService(Side.Server, As = [typeof(IMotionWorld)])]
public sealed class EngineMotionWorld(IEngineWorldQuery world, IEngineEntities entities) : IMotionWorld
{
    /// <summary><c>MASK_SOLID</c>: CONTENTS_SOLID | MOVEABLE | WINDOW | MONSTER | GRATE (bspflags.h), the contents <see cref="TraceMask.Solid"/> names.</summary>
    public const uint MaskSolid = 0x1u | 0x4000u | 0x2u | 0x2000000u | 0x8u;

    /// <summary><c>COLLISION_GROUP_PLAYER_MOVEMENT</c>: the group a player's movement traces in.</summary>
    public const int PlayerMovement = (int)EngineCollisionGroup.PlayerMovement;

    /// <summary>What a body's sweeps collide with: <see cref="MaskSolid"/> and the heroes' team's bit (they are RED; the robots are against them).</summary>
    public const uint BodyMask = MaskSolid | (uint)TFContents.RedTeam;

    public MotionTrace Sweep(in Vector start, in Vector end, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        if (start.X == end.X && start.Y == end.Y && start.Z == end.Z)
        {
            // A place test (the loop's stuck check, the motion audit): only the start-in-solid answer means anything.
            var p = world.EntityTraceHull(ignore, start, end, mins, maxs, BodyMask, PlayerMovement);
            if (!p.StartSolid)
                return MotionTrace.Clear(end);
            int at = p.Entity == EntityHandle.None ? 0 : p.Entity.EntryIndex;
            return new MotionTrace(true, 0f, start, Vector.Zero, true, p.AllSolid, p.Entity, at, at > 0 && entities.IsPlayer(p.Entity));
        }

        var t = world.TraceBoxInGroup(start, end, mins, maxs, BodyMask, ignore, PlayerMovement);
        if (!t.Hit)
            return MotionTrace.Clear(end);

        bool startSolid = false, allSolid = false;
        if (t.Fraction <= 0f)
        {
            var s = world.EntityTraceHull(ignore, start, end, mins, maxs, BodyMask, PlayerMovement);
            startSolid = s.StartSolid;
            allSolid = s.AllSolid;
        }

        var hit = t.Entity;
        int index = hit == EntityHandle.None ? 0 : hit.EntryIndex;
        bool player = index > 0 && entities.IsPlayer(hit);
        return new MotionTrace(true, t.Fraction, t.End, t.Normal, startSolid, allSolid, hit, index, player);
    }
}
