//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

//=============================================================================
// CHL2GameMovement's useable ladders (game/shared/hl2/hl_gamemovement.cpp),
// over the same loop as base CGameMovement.
//
// HL2MP's movement is CHL2GameMovement, and its ladders are func_useableladder
// entities rather than brushes: it overrides LadderMove, FullLadderMove and
// OnLadder, so a player mounts by +USE (or by walking into a ladder's cone or
// end point), climbs along the ladder's own axis, and gets off at an
// info_ladder_dismount. A loop handed an ILadderWorld (Ladders) runs these;
// one with none is base CGameMovement's, which is what the parity fixture
// recorded and what the blob path runs.
//
// The player's ladder state is in MoveState: m_HL2Local.m_hLadder as an entity
// index, and LadderMove_t's forced move. Both sides network it in the player's
// state, as stock does, so the client replay runs the same arithmetic over the
// same words.
//
// NOT PORTED, and why: CReservePlayerSpot (the server's "reserved_spot" entity
// holding the goal hull against other players; its validity trace IS ported,
// which is what decides whether the forced move starts at all); SetSolid
// NONE/BBOX on the player around the forced move (this side has no say over
// the player's solidity); SwallowUseKey's m_afButtonPressed (the weapons
// keep their own pressed buttons); and DisplayLadderHudHint under
// sv_ladder_useonly. m_bPlayUseDenySound is MoveState.PlayUseDenySound.
//
// CHL2GameMovement's CanAccelerate (always true) is the same loop's too, so
// it answers from Movement.cs's CanAccelerate when Ladders is set. Its other
// overrides stay base: HandleSpeedChanges and the HL2 player's ReduceTimers
// are the sprint and suit, which this mod's player does not have, and
// SetGroundEntity's special case is the combine ball, which it does not
// network.
//=============================================================================

public sealed partial class GameMovement
{
    /// <summary>
    /// The useable ladders this loop climbs (HL2MP's movement), or null for
    /// base <c>CGameMovement</c>'s brush ladders. Set by the side that owns the
    /// loop; the server's and the client's are each side's own entities.
    /// </summary>
    public ILadderWorld? Ladders { get; set; }

    /// <summary><c>USE_DISMOUNT_SPEED</c>: the push off a ladder dismounted by +USE in mid-air.</summary>
    private const float UseDismountSpeed = 100f;

    /// <summary><c>COLLISION_GROUP_NONE</c>: Findladder's line traces.</summary>
    private const int CollisionGroupNone = 0;

    /// <summary><c>MAX_COORD_INTEGER</c> (worldsize.h), Findladder's first best distance.</summary>
    private const float MaxCoordInteger = 16384f;

    /// <summary><c>CHL2GameMovement::MaxSpeed</c>: the client's cap when it has one, under the command's.</summary>
    private float LadderMaxSpeed()
    {
        // Min() is `a < b ? a : b`, spelled so.
        if (_s.ClientMaxSpeed != 0f)
            return _s.ClientMaxSpeed < _s.MaxSpeed ? _s.ClientMaxSpeed : _s.MaxSpeed;

        return _s.MaxSpeed;
    }

    /// <summary><c>GetLadder</c>: the player's ladder, when its word still names one.</summary>
    private bool GetLadder(out UseableLadder ladder)
    {
        ladder = default;
        return _s.Ladder != MoveState.NoLadder && Ladders!.TryGet(_s.Ladder, out ladder);
    }

    /// <summary>
    /// <c>SetLadder</c>: the player's ladder word, and OnPlayerGotOffLadder
    /// when a ladder the player was on is let go.
    /// </summary>
    private void SetLadder(UseableLadder? ladder)
    {
        if (ladder is null && GetLadder(out UseableLadder old))
            Ladders!.PlayerGotOff(old.Index, _s.EntIndex);

        _s.Ladder = ladder?.Index ?? MoveState.NoLadder;
    }

    /// <summary><c>CHL2_Player::ExitLadder</c>: off the ladder, with no output fired.</summary>
    private void ExitLadder()
    {
        if (_s.MoveType != MoveTypeLadder)
            return;

        _s.MoveType = MoveTypeWalk;
        _s.MoveCollide = MoveCollideDefault;
        _s.Ladder = MoveState.NoLadder;
    }

    /// <summary>
    /// <c>SwallowUseKey</c>: the +USE that mounted or dismounted is not pressed
    /// again next command, and found the ladder, so no deny sound. The
    /// pressed-buttons half stays out of this loop; see the file header.
    /// </summary>
    private void SwallowUseKey()
    {
        _s.OldButtons |= InUse;
        _s.PlayUseDenySound = false;
    }

    /// <summary>
    /// <c>CBaseEntity::SetGroundEntity( NULL )</c>, which FullLadderMove calls
    /// on the player directly: the ground and FL_ONGROUND go, and none of
    /// CGameMovement::SetGroundEntity's base-velocity bookkeeping happens.
    /// </summary>
    private void PlayerClearGround()
    {
        _s.HasGround = false;
        _s.GroundVelocity = Vector.Zero;
        _s.Flags &= ~FlOnGround;
    }

    //-------------------------------------------------------------------------
    // The forced move: mounting and dismounting slide the player there.
    //-------------------------------------------------------------------------

    /// <summary><c>StartForcedMove</c> (hl_gamemovement.cpp:153).</summary>
    private void StartForcedMove(bool mounting, float transitSpeed, in Vector goal, UseableLadder? ladder)
    {
        // Already active, just ignore.
        if (_s.LadderForceMove)
            return;

        if (ladder is { } on)
        {
            Ladders!.PlayerGotOn(on.Index, _s.EntIndex);

            // A ladder only there for automount checking stops here.
            if (on.Fake)
                return;
        }

        // The server reserves the goal: a player hull there, owned by the
        // player, so nothing else moves into it. The validity half decides
        // whether the move starts; the reservation entity is not ported.
        if (_s.IsServer && !GoalSpotIsValid(goal))
            return;

        _s.LadderGoalPosition = goal;
        _s.LadderStartPosition = _s.Origin;

        Vector delta = _s.LadderGoalPosition - _s.LadderStartPosition;
        float distance = MoveVec.Length(delta);

        float transitTime = distance / transitSpeed;
        if (transitTime < 0.001f)
            transitTime = 0.001f;

        _s.LadderForceMove = true;
        _s.LadderForceMount = mounting;

        _s.LadderStartTime = _s.CurTime;
        _s.LadderArrivalTime = _s.LadderStartTime + transitTime;

        _s.LadderForceLadder = ladder?.Index ?? MoveState.NoLadder;

        // Not stuck during the traversal: the origin is slammed each command.
        _s.MoveType = MoveTypeNone;
        _s.MoveCollide = MoveCollideDefault;
        SetLadder(ladder);

        SwallowUseKey();
    }

    /// <summary>
    /// <c>CReservePlayerSpot::ReserveSpot</c>'s answer: the player's hull
    /// (ducked if FL_DUCKING) fits at the goal, or one unit above it.
    /// </summary>
    private bool GoalSpotIsValid(in Vector goal)
    {
        bool ducking = (_s.Flags & FlDucking) != 0;
        Vector mins = GetPlayerMins(ducking), maxs = GetPlayerMaxs(ducking);

        MoveTrace tr = _w.Trace(goal, goal, mins, maxs, MaskPlayerSolid, CollisionGroupPlayerMovement);
        if (!tr.StartSolid)
            return true;

        Vector up = goal + new Vector(0f, 0f, 1f);
        tr = _w.Trace(up, up, mins, maxs, MaskPlayerSolid, CollisionGroupPlayerMovement);
        return !tr.StartSolid;
    }

    /// <summary><c>ContinueForcedMove</c> (hl_gamemovement.cpp:232). False when finished.</summary>
    private bool ContinueForcedMove()
    {
        // Suppress regular motion.
        _s.ForwardMove = 0.0f;
        _s.SideMove = 0.0f;
        _s.UpMove = 0.0f;

        float frac = (_s.CurTime - _s.LadderStartTime) / (_s.LadderArrivalTime - _s.LadderStartTime);
        if (frac > 1.0f)
            _s.LadderForceMove = false;

        frac = Clamp01(frac);

        Vector delta = _s.LadderGoalPosition - _s.LadderStartPosition;

        // VectorMA( start, frac, delta, org ).
        _s.Origin = new Vector(_s.LadderStartPosition.X + frac * delta.X,
                               _s.LadderStartPosition.Y + frac * delta.Y,
                               _s.LadderStartPosition.Z + frac * delta.Z);

        // Finished: the right move type back, or onto the ladder.
        if (!_s.LadderForceMove)
        {
            _s.MoveType = MoveTypeWalk;

            if (_s.LadderForceMount && _s.LadderForceLadder != MoveState.NoLadder
                && Ladders!.TryGet(_s.LadderForceLadder, out UseableLadder forced))
            {
                _s.MoveType = MoveTypeLadder;
                SetLadder(forced);
            }

            _s.Velocity = Vector.Zero;
        }

        return _s.LadderForceMove;
    }

    /// <summary>Source's <c>clamp( frac, 0.0f, 1.0f )</c>, which lets a NaN through.</summary>
    private static float Clamp01(float value)
        => value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;

    //-------------------------------------------------------------------------
    // Finding a ladder and a way off one.
    //-------------------------------------------------------------------------

    /// <summary><c>Findladder</c> (hl_gamemovement.cpp:304).</summary>
    private UseableLadder? FindLadder(float maxDist, out Vector ladderOrigin)
    {
        UseableLadder? best = null;
        float bestDist = MaxCoordInteger;
        Vector bestOrigin = Vector.Zero;

        float maxDistSqr = maxDist * maxDist;

        int count = Ladders!.Count;
        for (int i = 0; i < count; i++)
        {
            UseableLadder ladder = Ladders.At(i);

            if (!ladder.Enabled)
                continue;

            Vector closest = ClosestPointOnLineSegment(_s.Origin, ladder.Bottom, ladder.Top);
            float distSqr = LengthSqr(closest - _s.Origin);

            // Too far away.
            if (distSqr > maxDistSqr)
                continue;

            // A line to it must be clear. Stock also skips a hit on the ladder
            // itself and, on the second try, on a trigger; neither can answer
            // a MASK_PLAYERSOLID trace (a ladder is SOLID_NONE, a trigger is in
            // the trigger partition), so a hit is anything with an entity.
            MoveTrace tr = _w.Trace(_s.Origin, closest, Vector.Zero, Vector.Zero,
                                    MaskPlayerSolid, CollisionGroupNone);

            if (tr.Fraction != 1.0f && tr.HasEntity)
            {
                // Stepped up from the ground a bit, in case something at ground
                // level is in the way.
                float sizeZ = GetPlayerMaxs().Z - GetPlayerMins().Z;

                tr = _w.Trace(_s.Origin + new Vector(0f, 0f, sizeZ * 0.5f), closest, Vector.Zero, Vector.Zero,
                              MaskPlayerSolid, CollisionGroupNone);

                if (tr.Fraction != 1.0f && tr.HasEntity)
                    continue;
            }

            if (distSqr < bestDist)
            {
                bestDist = distSqr;
                best = ladder;
                bestOrigin = closest;
            }
        }

        ladderOrigin = bestOrigin;
        return best;
    }

    /// <summary>
    /// <c>GetSortedDismountNodeList</c> (hl_gamemovement.cpp:394): the ladder's
    /// dismount points within <paramref name="radius"/>, nearest first. Ties
    /// keep the ladder's order, as the RB tree's insert puts an equal key
    /// after the ones already there.
    /// </summary>
    private List<(Vector Spot, float DistSqr)> SortedDismounts(in Vector origin, float radius, in UseableLadder ladder)
    {
        float radiusSqr = radius * radius;
        var list = new List<(Vector Spot, float DistSqr)>();

        foreach (Vector spot in Ladders!.Dismounts(ladder.Index))
        {
            float distSqr = LengthSqr(spot - origin);
            if (distSqr > radiusSqr)
                continue;

            int at = list.Count;
            while (at > 0 && distSqr < list[at - 1].DistSqr)
                at--;

            list.Insert(at, (spot, distSqr));
        }

        return list;
    }

    /// <summary><c>ExitLadderViaDismountNode</c> (hl_gamemovement.cpp:423).</summary>
    private bool ExitLadderViaDismountNode(in UseableLadder ladder, bool strict, bool useAlternate = false)
    {
        float bestDot = -99999.0f;
        float bestDistance = 99999.0f;
        Vector bestDest = Vector.Zero;
        bool found = false;

        bool foundAlternate = false;
        Vector alternateDest = Vector.Zero;
        float alternateDist = 99999.0f;

        bool ducking = (_s.Flags & FlDucking) != 0;

        foreach ((Vector spot, _) in SortedDismounts(_s.Origin, 100.0f, ladder))
        {
            // Is it valid to put the player there?
            Vector org = spot + new Vector(0f, 0f, 1f);

            MoveTrace tr = _w.Trace(org, org, GetPlayerMins(ducking), GetPlayerMaxs(ducking),
                                    MaskPlayerSolid, CollisionGroupPlayerMovement);

            if (tr.StartSolid)
                continue;

            // The best dot product.
            Vector toSpot = org - (_s.Origin + _s.ViewOffset);
            toSpot.Z = 0.0f;
            float d = MoveVec.Normalize(ref toSpot);

            float dot = toSpot.Dot(_forward);

            // Not facing it.
            if (dot < 0.5f)
            {
                if (useAlternate && d < alternateDist)
                {
                    alternateDest = org;
                    alternateDist = d;
                    foundAlternate = true;
                }

                continue;
            }

            if (dot > bestDot)
            {
                bestDest = org;
                bestDistance = d;
                bestDot = dot;
                found = true;
            }
        }

        if (found)
        {
            // Require a more specific one.
            if (strict && ((bestDot < 0.7f) || (bestDistance > 40.0f)))
                return false;

            StartForcedMove(false, LadderMaxSpeed(), bestDest, null);
            return true;
        }

        // Desperate: an alternate rather than refusing to let them off.
        if (useAlternate && foundAlternate && alternateDist <= 60.0f)
        {
            StartForcedMove(false, LadderMaxSpeed(), alternateDest, null);
            return true;
        }

        return false;
    }

    //-------------------------------------------------------------------------
    // The climb.
    //-------------------------------------------------------------------------

    /// <summary><c>CHL2GameMovement::FullLadderMove</c> (hl_gamemovement.cpp:566).</summary>
    private void FullUseableLadderMove()
    {
        if (!GetLadder(out UseableLadder ladder))
            return;

        CheckWater();

        // Jump pressed? Then nothing here.
        if ((_s.Buttons & InJump) != 0)
        {
            CheckJumpButton();
            return;
        }

        _s.OldButtons &= ~InJump;

        PlayerClearGround();

        // In case this movement is cancelled.
        Vector oldVelocity = _s.Velocity;
        Vector oldOrigin = _s.Origin;

        Vector top = ladder.Top;
        Vector bottom = ladder.Bottom;

        // Parametric distance along the ladder vector.
        CalcDistanceSqrToLine(_s.Origin, top, bottom, out float oldT);

        // The move, with any base velocity.
        _s.Velocity += _s.BaseVelocity;
        TryPlayerMove();
        _s.Velocity -= _s.BaseVelocity;

        int buttonsChanged = _s.OldButtons ^ _s.Buttons;
        int buttonsPressed = buttonsChanged & _s.Buttons;
        bool pressedUse = (buttonsPressed & InUse) != 0;
        bool pressingForwardOrSide = _s.ForwardMove != 0.0f || _s.SideMove != 0.0f;

        Vector ladderVec = top - bottom;
        float ladderLength = MoveVec.Normalize(ref ladderVec);

        // "Not perfect by any means, but should help a bit."
        bool movingAlongLadder = false;
        if (pressingForwardOrSide)
        {
            float fwdDot = _forward.Dot(ladderVec);
            if (MathF.Abs(fwdDot) > 0.9f)
                movingAlongLadder = true;
        }

        CalcDistanceSqrToLine(_s.Origin, top, bottom, out float newT);

        // A fudge of 2 units.
        float tolerance = 1.0f / ladderLength;

        bool wouldLeaveLadder = false;
        if (newT < -tolerance)
            wouldLeaveLadder = newT < oldT;
        else if (newT > (1.0f + tolerance))
            wouldLeaveLadder = newT > oldT;

        // Near the top or the bottom but not moving?
        float dist1Sqr = LengthSqr(top - _s.Origin);
        float dist2Sqr = LengthSqr(bottom - _s.Origin);

        float dist = dist1Sqr < dist2Sqr ? dist1Sqr : dist2Sqr;
        bool nearDismountNode = dist < 16.0f * 16.0f;
        float ladderUnitsPerTick = MaxClimbSpeed * _c.TickInterval;
        bool nearDismountNode2 = dist < ladderUnitsPerTick * ladderUnitsPerTick;

        // Really close to a node, the convar set and a key pressed: a +USE.
        bool autoDismountUse = nearDismountNode2
                            && Ladders!.ConVars.AutoDismount
                            && pressingForwardOrSide
                            && !movingAlongLadder;

        bool fullyUnderwater = _s.WaterLevel == WlEyes;

        bool useDismount = pressedUse || autoDismountUse;

        // Fully underwater, looking straight at a dismount node floats you off mid-span.
        if (fullyUnderwater && !useDismount && ExitLadderViaDismountNode(ladder, true))
            return;

        // Leaving the ladder without +USE or the automation is not allowed.
        if (!useDismount)
        {
            if (wouldLeaveLadder)
            {
                _s.Velocity = oldVelocity;
                _s.Origin = oldOrigin;
            }

            return;
        }

        // Not leaving and not near the end: accept the move.
        if (!wouldLeaveLadder && !nearDismountNode)
        {
            if (pressedUse)
            {
                // A dismount point mid-span?
                if (ExitLadderViaDismountNode(ladder, false))
                    return;

                _s.MoveType = MoveTypeWalk;
                _s.MoveCollide = MoveCollideDefault;
                SetLadder(null);
                _s.PlayUseDenySound = false;

                // Off with a bit of velocity in the facing direction.
                _s.Velocity = _forward * UseDismountSpeed;
                _s.Velocity.Z = 50;
            }

            return;
        }

        // Debounce the use key.
        if (pressedUse)
            SwallowUseKey();

        // Auto exit, if possible.
        if (ExitLadderViaDismountNode(ladder, false, pressedUse))
            return;

        if (wouldLeaveLadder)
        {
            if (pressedUse)
            {
                _s.MoveType = MoveTypeWalk;
                _s.MoveCollide = MoveCollideDefault;
                SetLadder(null);

                _s.Velocity = _forward * UseDismountSpeed;
                _s.Velocity.Z = 50;
            }
            else
            {
                _s.Velocity = oldVelocity;
                _s.Origin = oldOrigin;
            }
        }
    }

    /// <summary><c>CheckLadderAutoMountEndPoint</c> (hl_gamemovement.cpp:748).</summary>
    private bool CheckLadderAutoMountEndPoint(UseableLadder? candidate, in Vector bestOrigin)
    {
        if (candidate is not { } ladder)
            return false;

        Vector top = ladder.Top, bottom = ladder.Bottom;

        float d1 = LengthSqr(top - _s.Origin);
        float d2 = LengthSqr(bottom - _s.Origin);

        if (d1 > 16 * 16 && d2 > 16 * 16)
            return false;

        // Close to the top looks down the ladder; otherwise up it.
        Vector ladderAxis = d1 < 16 * 16 ? bottom - top : top - bottom;

        MoveVec.Normalize(ref ladderAxis);

        if (ladderAxis.Dot(_forward) > Ladders!.ConVars.AutoMountDot)
        {
            StartForcedMove(true, LadderMaxSpeed(), bestOrigin, ladder);
            return true;
        }

        return false;
    }

    /// <summary><c>CheckLadderAutoMountCone</c> (hl_gamemovement.cpp:789).</summary>
    private bool CheckLadderAutoMountCone(UseableLadder? candidate, in Vector bestOrigin,
                                          float maxAngleDelta, float maxDistToLadder)
    {
        // Never back or strafe onto a ladder.
        if (candidate is not { } ladder || !(_s.ForwardMove > 0.0f))
            return false;

        Vector top = ladder.Top, bottom = ladder.Bottom;

        Vector ladderAxis = top - bottom;
        MoveVec.Normalize(ref ladderAxis);

        Vector probe = _s.Origin;
        Vector closest = ClosestPointOnLineSegment(probe, bottom, top);

        Vector toLadder = closest - probe;
        float dist = MoveVec.Normalize(ref toLadder);

        Vector flatLadder = toLadder;
        flatLadder.Z = 0.0f;
        Vector flatForward = _forward;
        flatForward.Z = 0.0f;

        MoveVec.Normalize(ref flatLadder);
        MoveVec.Normalize(ref flatForward);

        float facingDot = flatForward.Dot(flatLadder);

        // acos( facingDot ) * 180 / M_PI: acos's float overload, times an int,
        // over a double, back to float.
        float angle = (float)(MathF.Acos(facingDot) * 180 / Math.PI);

        bool closeToLadder = dist != 0.0f && dist < maxDistToLadder;
        bool reallyCloseToLadder = dist != 0.0f && dist < 4.0f;

        bool facingLadderAxis = angle < maxAngleDelta;
        bool facingAlongAxis = MathF.Abs(ladderAxis.Dot(_forward)) > Ladders!.ConVars.AutoMountDot;

        // Tracker 21776: not this way while strafing.
        bool strafing = !(MathF.Abs(_s.SideMove) < 1.0f);

        if (((facingDot > 0.0f && !strafing) || facingAlongAxis)
            && (facingLadderAxis || reallyCloseToLadder)
            && closeToLadder)
        {
            StartForcedMove(true, LadderMaxSpeed(), bestOrigin, ladder);
            return true;
        }

        return false;
    }

    /// <summary><c>LookingAtLadder</c> (hl_gamemovement.cpp:857): facing towards it.</summary>
    private bool LookingAtLadder(UseableLadder? candidate)
    {
        if (candidate is not { } ladder)
            return false;

        // The closest point on the ladder (could be an end point).
        Vector closest = ClosestPointOnLineSegment(_s.Origin, ladder.Bottom, ladder.Top);

        Vector flatForward = _forward;
        flatForward.Z = 0.0f;

        // The ladder is not solid, so the origin may be past it: back up a bit
        // to do the facing calculation.
        Vector adjustedOrigin = _s.Origin - 8.0f * flatForward;

        Vector toLadder = closest - adjustedOrigin;

        Vector flatLadder = toLadder;
        flatLadder.Z = 0.0f;

        MoveVec.Normalize(ref flatLadder);
        MoveVec.Normalize(ref flatForward);

        float facingDot = flatForward.Dot(flatLadder);

        // `float requiredDot = cond ? -0.99 : 0.0;` -- double literals, stored as float.
        float requiredDot = Ladders!.ConVars.UseOnly ? (float)-0.99 : 0.0f;

        return facingDot >= requiredDot;
    }

    /// <summary><c>CHL2GameMovement::LadderMove</c> (hl_gamemovement.cpp:935).</summary>
    private bool UseableLadderMove()
    {
        if (_s.MoveType == MoveTypeNoClip)
        {
            SetLadder(null);
            return false;
        }

        // Being forced to mount or dismount still acts as on the ladder.
        if (_s.LadderForceMove && ContinueForcedMove())
            return true;

        UseableLadder? bestLadder = null;
        Vector bestOrigin = Vector.Zero;

        bool onLadder = GetLadder(out UseableLadder ladder);

        // Something deactivated the ladder, or an external force hit us: fall.
        if (onLadder && (!ladder.Enabled || LengthSqr(_s.BaseVelocity) > 1.0f))
        {
            ExitLadder();
            onLadder = false;
        }

        if (!onLadder)
            bestLadder = FindLadder(64.0f, out bestOrigin);

        int buttonsChanged = _s.OldButtons ^ _s.Buttons;
        int buttonsPressed = buttonsChanged & _s.Buttons;
        bool pressedUse = (buttonsPressed & InUse) != 0;

        LadderConVars cvars = Ladders!.ConVars;

        if (!onLadder && !pressedUse)
        {
            // In the air, a ladder within 15 degrees and close mounts.
            if (!cvars.UseOnly)
            {
                // Tracker 6625 / 12961: no need to be leaping, but on the ground
                // they must not be backing into it.
                bool onGround = _s.HasGround;
                if ((!onGround || _s.ForwardMove > 0.0f)
                    && CheckLadderAutoMountCone(bestLadder, bestOrigin, 15.0f, 32.0f))
                {
                    return true;
                }

                // Pressing forward, looking at a ladder, near a mount point.
                if (_s.ForwardMove > 0.0f && CheckLadderAutoMountEndPoint(bestLadder, bestOrigin))
                    return true;
            }

            return false;
        }

        // CheckLadderAutoMount: +USE on a ladder in front of them.
        if (!onLadder && LookingAtLadder(bestLadder) && bestLadder is { } toMount)
        {
            StartForcedMove(true, LadderMaxSpeed(), bestOrigin, toMount);
            return true;
        }

        // Reassign the ladder.
        if (!GetLadder(out ladder))
            return false;

        // The press that is climbing it found something: no deny sound.
        if (pressedUse)
            _s.PlayUseDenySound = false;

        // On the ladder.
        _s.MoveType = MoveTypeLadder;
        _s.MoveCollide = MoveCollideDefault;

        _s.EntityGravity = 0.0f;

        float forwardSpeed = 0.0f;
        float rightSpeed = 0.0f;

        float speed = LadderMaxSpeed();

        if ((_s.Buttons & InBack) != 0) forwardSpeed -= speed;
        if ((_s.Buttons & InForward) != 0) forwardSpeed += speed;
        if ((_s.Buttons & InMoveLeft) != 0) rightSpeed -= speed;
        if ((_s.Buttons & InMoveRight) != 0) rightSpeed += speed;

        if ((_s.Buttons & InJump) != 0)
        {
            _s.MoveType = MoveTypeWalk;
            SetLadder(null);

            // Jump in the view direction, unless pressing backward.
            Vector jumpDir = _forward;
            if (_s.ForwardMove < 0.0f)
                jumpDir = new Vector(-jumpDir.X, -jumpDir.Y, -jumpDir.Z);

            MoveVec.Normalize(ref jumpDir);

            _s.Velocity = jumpDir * MaxClimbSpeed;

            // Tracker 13558: no extra z if facing downward at all.
            if (_forward.Z >= 0.0f)
                _s.Velocity.Z = _s.Velocity.Z + 50;

            return false;
        }

        if (forwardSpeed != 0 || rightSpeed != 0)
        {
            // Looking toward the top or the bottom?
            Vector velocity = _forward * forwardSpeed;
            velocity = new Vector(velocity.X + rightSpeed * _right.X,
                                  velocity.Y + rightSpeed * _right.Y,
                                  velocity.Z + rightSpeed * _right.Z);

            MoveVec.Normalize(ref velocity);

            Vector ladderUp = ladder.Top - ladder.Bottom;
            MoveVec.Normalize(ref ladderUp);

            Vector top = ladder.Top, bottom = ladder.Bottom;

            // Mounted in a bogus spot? Then fall off.
            float distFromLadderSqr = CalcDistanceSqrToLine(_s.Origin, top, bottom, out _);
            if (distFromLadderSqr > 36.0f)
            {
                _s.MoveType = MoveTypeWalk;
                SetLadder(null);
                return false;
            }

            bool isHorizontal = MathF.Abs(top.Z - bottom.Z) < 64.0f;

            float changeover = isHorizontal ? 0.0f : 0.3f;

            float factor = 1.0f;
            if (velocity.Z >= 0)
            {
                float dotTop = ladderUp.Dot(velocity);
                if (dotTop < -changeover)
                    factor = -1.0f;   // aimed at the bottom
            }
            else
            {
                float dotBottom = -ladderUp.Dot(velocity);
                if (dotBottom > changeover)
                    factor = -1.0f;
            }

            _s.Velocity = (MaxClimbSpeed * factor) * ladderUp;
        }
        else
        {
            _s.Velocity = Vector.Zero;
        }

        return true;
    }

    //-------------------------------------------------------------------------
    // mathlib_base.cpp's line helpers, term for term.
    //-------------------------------------------------------------------------

    private static float LengthSqr(in Vector v) => v.X * v.X + v.Y * v.Y + v.Z * v.Z;

    /// <summary><c>CalcClosestPointToLineT</c>.</summary>
    private static float ClosestPointToLineT(in Vector p, in Vector a, in Vector b, out Vector dir)
    {
        dir = b - a;

        float div = dir.Dot(dir);
        if (div < 0.00001f)
            return 0;

        return (dir.Dot(p) - dir.Dot(a)) / div;
    }

    /// <summary><c>Vector::MulAdd( a, dir, t )</c>.</summary>
    private static Vector MulAdd(in Vector a, in Vector dir, float t)
        => new(a.X + dir.X * t, a.Y + dir.Y * t, a.Z + dir.Z * t);

    /// <summary><c>CalcDistanceSqrToLine</c>, through <c>CalcClosestPointOnLine</c>.</summary>
    private static float CalcDistanceSqrToLine(in Vector p, in Vector a, in Vector b, out float t)
    {
        t = ClosestPointToLineT(p, a, b, out Vector dir);
        Vector closest = MulAdd(a, dir, t);
        return LengthSqr(p - closest);
    }

    /// <summary><c>CalcClosestPointOnLineSegment</c>: the closest point, t clamped to the segment.</summary>
    private static Vector ClosestPointOnLineSegment(in Vector p, in Vector a, in Vector b)
    {
        float t = ClosestPointToLineT(p, a, b, out Vector dir);
        t = Clamp01(t);
        return MulAdd(a, dir, t);
    }
}
