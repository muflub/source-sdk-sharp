//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The arithmetic a pushing brush does — <c>plan_entity.md</c> step 2g.
///
/// <para>
/// <b>WHAT MOVED.</b> <c>CStandaloneEntity::UpdateMovement</c> stepped a mover
/// toward a position or an orientation, asked <c>BlockerAt</c> whether anybody
/// was standing where it was about to be, committed the step or refused it, and
/// worked out how fast it had actually gone. Every one of those is a decision
/// about what the game does. The arithmetic is here, engine-free, so it is
/// checked by a fact rather than by standing on a lift; the frame that drives it
/// is <c>SourceSharp.Server/EntityMotion.cs</c>.
/// </para>
///
/// <para>
/// <b>IT IS BIT-EXACT AGAINST A RECORDING AND THAT IS WHY IT IS SPELLED LIKE
/// THIS.</b> <c>Fixtures/mover-updatemovement-stock.tsv</c> is 1394 frames of
/// the native stepper taken off a live <c>dm_lockdown</c> before any of this
/// existed, and <c>MoverFixture</c> replays them with no tolerance. Two pieces
/// of Source's mathlib do not agree with the obvious managed spelling and both
/// are on this path:
/// </para>
///
/// <list type="bullet">
/// <item><description>
/// <c>VectorNormalize</c> is <c>_mm_rsqrt_ss</c> plus one Newton-Raphson step
/// over a length squared with <c>1e-10f</c> added, and it RETURNS the length it
/// reconstructs from that approximation rather than the length. The stepper
/// compares that return against the step, so the approximation decides which
/// frame a door arrives on. <see cref="MoveVec.Normalize"/> is step 3e's port of
/// it and is used here rather than re-derived, because two spellings of one
/// conversion is the drift this whole port exists to remove.
/// </description></item>
/// <item><description>
/// <c>AngleDiff</c> is <c>fmodf</c> with a sign fix-up that is asymmetric —
/// it branches on <c>destAngle &gt; srcAngle</c>, not on the sign of the
/// remainder — so it is spelled term for term below.
/// </description></item>
/// </list>
/// </summary>
public static class MoverMath
{
    /// <summary>
    /// One frame of a linear travel: how far it gets, and whether that is the
    /// end of it.
    ///
    /// <para>
    /// <b>The step is CLAMPED to what is left</b>, which is what makes a mover
    /// arrive exactly rather than overshoot and oscillate around its target —
    /// an unclamped step does that at any speed high enough to matter. The
    /// clamp is also why the last frame of a travel is slower than the mover's
    /// speed, which is the whole reason <see cref="Velocity"/> measures the
    /// step that was committed instead of multiplying the speed by a direction.
    /// </para>
    /// </summary>
    public static LinearStep AdvanceLinear(in Vector current, in Vector target,
                                           float speed, float frameTime)
    {
        Vector toTarget = target - current;

        // THE ENGINE'S NORMALIZE AND ITS RETURN VALUE. `remaining` is
        // `sqrlen * invlen` — the distance as the rsqrt approximation
        // reconstructs it — and it is what stock compares the step against.
        // Using an accurate distance here answers "arrived" one frame early or
        // late on a travel whose last step lands within a few ulps of the end.
        float remaining = MoveVec.Normalize(ref toTarget);
        float step = speed * frameTime;

        return step >= remaining
            ? new LinearStep(target, true)
            : new LinearStep(current + toTarget * step, false);
    }

    /// <summary>
    /// One frame of a rotation toward a target, per axis.
    ///
    /// <para>
    /// PER AXIS AND NOT ALONG A SHORTEST ARC: stock turns pitch, yaw and roll
    /// independently and arrives when all three have, which is why a platform
    /// told to turn 90 degrees about yaw does not also drift in pitch.
    /// </para>
    /// </summary>
    public static AngularStep AdvanceAngular(in QAngle current, in QAngle target,
                                             float speed, float frameTime)
    {
        float step = speed * frameTime;
        bool arrived = true;

        return new AngularStep(
            new QAngle(Axis(current.Pitch, target.Pitch, step, ref arrived),
                       Axis(current.Yaw, target.Yaw, step, ref arrived),
                       Axis(current.Roll, target.Roll, step, ref arrived)),
            arrived);
    }

    private static float Axis(float current, float target, float step, ref bool arrived)
    {
        float remaining = AngleDiff(target, current);

        if (MathF.Abs(remaining) <= step)
            return target;

        arrived = false;
        return current + (remaining > 0f ? step : -step);
    }

    /// <summary>
    /// One frame of a continuous spin, which never arrives and so never
    /// reports. Stock's <c>m_angVelocity</c> arm.
    /// </summary>
    public static QAngle AdvanceSpin(in QAngle current, in QAngle degreesPerSecond,
                                     float frameTime)
        => new(current.Pitch + degreesPerSecond.Pitch * frameTime,
               current.Yaw + degreesPerSecond.Yaw * frameTime,
               current.Roll + degreesPerSecond.Roll * frameTime);

    /// <summary>
    /// <c>AngleDiff</c> — <c>mathlib_base.cpp:3476</c>, term for term.
    ///
    /// <para>
    /// The fix-up branches on which angle is larger rather than on the sign of
    /// the remainder, and the two are not the same function: at
    /// <c>AngleDiff(0, 360)</c> the remainder is <c>-0</c> and stock takes the
    /// <c>destAngle &gt; srcAngle</c> arm's <c>false</c> side. A "tidier"
    /// version that compares the remainder against ±180 answers differently
    /// there.
    /// </para>
    /// </summary>
    public static float AngleDiff(float destAngle, float srcAngle)
    {
        float delta = (destAngle - srcAngle) % 360f;

        if (destAngle > srcAngle)
        {
            if (delta >= 180f)
                delta -= 360f;
        }
        else
        {
            if (delta <= -180f)
                delta += 360f;
        }

        return delta;
    }

    /// <summary>
    /// How fast a mover actually went over one frame — <c>shell_mover_velocity.h</c>,
    /// which this replaces.
    ///
    /// <para>
    /// The step it COMMITTED, in WORLD space, over the frame it took. Not the
    /// speed times a direction: the last step of a travel is clamped short, and
    /// a rider handed the nominal speed on that frame is handed a floor that is
    /// not there. Not the local-space step either — a parented mover's own frame
    /// is not the frame a rider stands in. And not the INTENDED step: a mover
    /// whose destination has somebody standing in it does not move at all that
    /// frame, and a blocked lift reporting the speed it wanted would hand a
    /// rider a floor that stayed where it was.
    /// </para>
    ///
    /// <para>
    /// A frametime of zero or less is a paused or single-stepped server, not a
    /// mover moving infinitely fast; it answers zero rather than dividing.
    /// </para>
    /// </summary>
    public static Vector Velocity(in Vector worldBefore, in Vector worldAfter, float frameTime)
        => frameTime <= 0f
            ? Vector.Zero
            : new Vector((worldAfter.X - worldBefore.X) / frameTime,
                         (worldAfter.Y - worldBefore.Y) / frameTime,
                         (worldAfter.Z - worldBefore.Z) / frameTime);

    /// <summary>
    /// How far into a mover's top face somebody standing on it may be.
    ///
    /// <para>
    /// A step, not a slop. The brush moves first and the carry puts the rider's
    /// feet back on the face afterwards, so between the two a legitimate
    /// passenger is one whole step inside the brush. Four units covers a step at
    /// ~260 units a second on a 66-tick server. Kept the same number as
    /// <c>Riders.Sink</c>, and it has to be: this side decides who is not a
    /// blocker and that side decides who is carried, and a mover that refuses a
    /// step for somebody it was about to carry never moves again.
    /// </para>
    /// </summary>
    public const float RiderSink = 4f;

    /// <summary>
    /// Whether somebody overlapping a mover's destination is standing ON it
    /// rather than in its way — <c>standalone_riders.h</c>, which this replaces.
    ///
    /// <para>
    /// A rider standing on the top face overlaps the destination box by exactly
    /// one step whenever the brush moves up, so without this rule a lift with a
    /// passenger reports itself blocked by its own passenger and never moves.
    /// Somebody genuinely in the way — standing on the floor in front of a door,
    /// under a descending platform — has their feet below the brush's top and
    /// still blocks.
    /// </para>
    ///
    /// <para>
    /// Feet against the mover's <b>PRE-STEP</b> top, because the question is who
    /// is standing on the mover NOW, not where it is going.
    /// </para>
    /// </summary>
    public static bool IsRider(float feetZ, float currentTopZ)
        => feetZ >= currentTopZ - RiderSink;

    /// <summary>
    /// The same question asked of one candidate the walk offered, with the
    /// "is this even a person" half in front of it.
    ///
    /// <para>
    /// <b>Players only</b>, which is stock's <c>FL_CLIENT|FL_NPC</c> mask with
    /// the <c>FL_NPC</c> half absent rather than forgotten: nothing in this mod
    /// is an NPC, because it has no AI. The other thing a mover meets is another
    /// managed brush, and a door does not stop for a door in stock either. If AI
    /// ever arrives, this is the parameter that grows.
    /// </para>
    ///
    /// <para>
    /// <b>Overlap IS a parameter here and was not in the native rule</b>, and
    /// that is a stated divergence rather than an oversight. Native was fed by
    /// one walk of the spatial partition over the destination BOX, so a
    /// candidate that reached the rule had already overlapped by the engine's
    /// own test. The managed side has <c>EntitiesInSphere</c> and no box query
    /// (<c>abi-requests/2g.md</c> asks for one), so the box test is done here,
    /// on the caller's side of the same question. A sphere around a long thin
    /// plate is much larger than the plate, so leaving it out would have a train
    /// stop for somebody standing beside the track.
    /// </para>
    /// </summary>
    public static bool Blocks(bool isPlayer, bool overlapsDestination,
                              float feetZ, float currentTopZ)
        => isPlayer && overlapsDestination && !IsRider(feetZ, currentTopZ);

    /// <summary>
    /// Whether two axis-aligned boxes, both in world space, overlap at all.
    ///
    /// <para>
    /// Touching faces count. The engine's own box enumeration is inclusive at
    /// its bounds, and a rider resting exactly on a face is the commonest
    /// candidate there is.
    /// </para>
    /// </summary>
    public static bool BoxesOverlap(in Vector aMins, in Vector aMaxs,
                                    in Vector bMins, in Vector bMaxs)
        => aMins.X <= bMaxs.X && aMaxs.X >= bMins.X
        && aMins.Y <= bMaxs.Y && aMaxs.Y >= bMins.Y
        && aMins.Z <= bMaxs.Z && aMaxs.Z >= bMins.Z;
}

/// <summary>One frame of a linear travel: where it got to, and whether that is the target.</summary>
public readonly record struct LinearStep(Vector Origin, bool Arrived);

/// <summary>One frame of a rotation: the new facing, and whether every axis has arrived.</summary>
public readonly record struct AngularStep(QAngle Angles, bool Arrived);
