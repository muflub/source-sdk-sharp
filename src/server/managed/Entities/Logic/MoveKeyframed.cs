//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Walks a chain of <see cref="KeyframeTrack"/> keys, and turns round at each
/// end of it.
///
/// It is the head of its own chain: a move_keyframed IS a keyframe, which is why
/// it derives from one, and the first leg runs from where the mapper drew it to
/// whatever its NextKey names.
///
/// A leg is TIMED rather than driven by arrival. That is stock's shape as well as
/// this one's — a keyframe carries a speed, the speed and the gap give a
/// duration, and both position and orientation are then made to take exactly that
/// long. It matters because the two can disagree: a leg that turns ninety degrees
/// while moving eight units must not finish the move and then stand still
/// rotating.
///
/// There are no inputs, because stock declares none — the entity starts by itself
/// and never stops. That is what lets this drop stock's machinery for reversing
/// in the middle of a leg: with nothing able to interrupt it, the direction can
/// only ever change at a key. The machinery is there for classes derived from
/// CBaseMoveBehavior, and a move_keyframed cannot reach it.
/// </summary>
[ScriptedEntity(EntityNames.MoveKeyframed)]
public sealed class MoveKeyframed : KeyframeTrack
{
    /// <summary>What a map may set on this entity. See <see cref="MoveKeyframedVars"/>.</summary>
    [ScriptVars] public MoveKeyframedVars MoveKeyframedVars = new();

    /// <summary>The key being travelled from.</summary>
    public KeyframeTrack? CurrentKey { get; private set; }

    /// <summary>The key being travelled to, or null when at rest.</summary>
    public KeyframeTrack? TargetKey { get; private set; }

    /// <summary>1 forward along the chain, -1 back along it, 0 at rest.</summary>
    public int Direction { get; private set; }

    public override void OnSpawn()
    {
        base.OnSpawn();

        // Its own first key, before anything has been linked: a mover with no
        // NextKey at all still has somewhere it is.
        CurrentKey = this;

        // Deferred rather than started here, for the reason KeyframeTrack.LinkChain
        // gives: the keys this names do not all exist yet. Stock waits half a
        // second at this point and then throws the wait away by starting
        // immediately, which reads like the same worry acted on and then undone.
        SetNextThink(0.2f);
    }

    /// <summary>
    /// The end of a leg, and the start of the next one.
    ///
    /// The direction is read before the attempt because a failed attempt clears
    /// it: at the end of the chain the leg cannot be extended, and what makes
    /// this a round trip rather than a one-way journey is trying the opposite of
    /// the direction it WAS going.
    /// </summary>
    public override void OnThink()
    {
        LinkChain();

        int direction = Direction == 0 ? 1 : Direction;

        if (!StartMoving(direction))
            StartMoving(-direction);
    }

    private bool StartMoving(int direction)
    {
        if (direction == 0)
        {
            StopMoving();
            return false;
        }

        if (Direction == direction)
        {
            // Carrying on: the key just reached becomes the key being left.
            CurrentKey = TargetKey;
            TargetKey = CurrentKey?.NextKey(direction);
        }
        else
        {
            Direction = direction;
            TargetKey = CurrentKey?.NextKey(direction);
        }

        if (CurrentKey is null || TargetKey is null)
        {
            StopMoving();
            return false;
        }

        // A key's speed describes the leg AFTER it, so travelling backwards the
        // duration belongs to the key being moved TO rather than the one being
        // left.
        Travel(TargetKey, direction == 1 ? CurrentKey.TimeToNext : TargetKey.TimeToNext);
        return true;
    }

    /// <summary>
    /// Stops where it is, leaving <see cref="CurrentKey"/> alone.
    ///
    /// Deliberately: the key it stopped on is the key it has to set off from when
    /// the direction reverses, and forgetting it is how a mover reaches the end
    /// of a chain and never comes back.
    /// </summary>
    private void StopMoving()
    {
        Direction = 0;
        TargetKey = null;
        StopThinking();
    }

    private void Travel(KeyframeTrack target, float seconds)
    {
        if (seconds <= 0f)
        {
            // A key with no speed, or two keys that are the same key in every
            // way that matters. Stock lands on the next one a frame later rather
            // than stalling on it, so this does too.
            Origin = target.KeyOrigin;
            SetAngles(target.KeyAngles);
            SetNextThink(0f);
            return;
        }

        float distance = (target.KeyOrigin - Origin).Length;

        if (distance > 0f)
            MoveTo(target.KeyOrigin, distance / seconds);

        // The shell turns each of pitch, yaw and roll at the speed it is given
        // and takes the short way round, so the speed that makes all three
        // arrive together is the one that gets the LARGEST of them there in time.
        float turn = LargestTurn(Entities.GetAngles(Handle), target.KeyAngles);

        if (turn > 0f)
            RotateTo(target.KeyAngles, turn / seconds);

        SetNextThink(seconds);
    }

    private static float LargestTurn(Vector from, Vector to)
        => MathF.Max(MathF.Abs(ShortestTurn(to.X, from.X)),
           MathF.Max(MathF.Abs(ShortestTurn(to.Y, from.Y)),
                     MathF.Abs(ShortestTurn(to.Z, from.Z))));

    /// <summary>
    /// The engine's AngleDiff, repeated here because the answer has to agree
    /// with the stepper: a speed worked out the long way round arrives late.
    /// </summary>
    private static float ShortestTurn(float destination, float source)
    {
        float delta = (destination - source) % 360f;

        if (delta > 180f)
            delta -= 360f;
        else if (delta < -180f)
            delta += 360f;

        return delta;
    }
}

/// <summary>
/// The map keys <see cref="MoveKeyframed"/> declares.
/// </summary>
[ScriptVars]
public partial class MoveKeyframedVars
{
    // Declared so a map's values survive being read back, and used by nothing.
    // Stock hands them to the keyframe library, which interpolates a leg as a
    // spline through the keys on either side of it; this travels the straight
    // line between two keys. See docs/divergences.md.
    public int PositionInterpolator { get; set; }

    public int RotationInterpolator { get; set; }
}
