//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The arithmetic of <c>game/shared/gamemovement.cpp</c>, in managed code.
///
/// <para>
/// This is docs/plan-audit.md's decision <b>E1</b> — "managed code does" —
/// carried out for the part of the file that can be carried out safely: every
/// method here is a stock <c>CGameMovement</c> method whose body reads and
/// writes NOTHING but its arguments. No trace, no entity, no convar lookup, no
/// globals. That is the whole selection rule, and it is what makes each one
/// testable offline and provably identical on the two sides.
/// </para>
///
/// <para>
/// <b>Why "identical on the two sides" is the point.</b> Movement is predicted:
/// the client replays this same code over every command the server has not
/// acknowledged, and the server runs it once for real. A per-field comparison
/// with declared tolerances then reports the difference (PlayerNetState —
/// Origin 0.0625, Velocity 0.5, BaseVelocity 0.05, ViewOffset 0.25, PunchAngle
/// 0.125). Stock has TWO compiled copies of this arithmetic, one in client.so
/// and one in server.so, and the jump-misprediction bug is what a disagreement
/// between two copies looks like from the outside. There is ONE copy here,
/// called by both, so an entire class of divergence stops being possible rather
/// than being tested for.
/// </para>
///
/// <para>
/// <b>Stock's quirks are reproduced, not fixed, and each is named at its
/// site.</b> A quirk "fixed" here is a misprediction invented here, because the
/// rest of the loop is still stock's and still expects stock's answer. Where a
/// line looks wrong, the comment says why it is kept.
/// </para>
///
/// <para>
/// Everything is <c>static</c> and takes its state in arguments: these run from
/// an unmanaged callback on both sides of a two-side runtime, so any field held
/// here would be one side writing over the other's movement.
/// </para>
///
/// <para>
/// <b>IT IS IN SourceSharp.Abi, AND THAT IS LOAD-BEARING RATHER THAN
/// TIDINESS.</b> Each side's game assemblies load into their own COLLECTIBLE
/// AssemblyLoadContext so they can be swapped without restarting
/// (GameLoadContext) — so a class in SourceSharp.Shared is loaded TWICE, once
/// per side, and "one copy of the arithmetic" would have been a claim rather
/// than a fact. SourceSharp.Abi is the single exception: GameLoadContext
/// resolves it to the DEFAULT context by name, so there is exactly one of it
/// for both sides and for every reload. Moving this file to Shared would
/// compile, pass every test here, and quietly restore the two-copies problem
/// the port exists to remove.
/// </para>
/// </summary>
public static class MoveMath
{
    /// <summary>
    /// gamemovement.cpp:1228 — bigger is more damped.
    /// </summary>
    public const float PunchDamping = 9.0f;

    /// <summary>
    /// gamemovement.cpp:1230 — bigger corrects the view faster.
    /// </summary>
    public const float PunchSpringConstant = 65.0f;

    /// <summary>
    /// <c>PLAYER_MAX_SAFE_FALL_SPEED</c> — shareddefs.h:410, the HL2 arm,
    /// which is the arm stock HL2MP compiles (server_hl2mp.vpc defines
    /// <c>HL2_DLL</c>, client_hl2mp.vpc <c>HL2_CLIENT_DLL</c>): approx. 20 feet,
    /// sqrt( 2 * 600 * 20 * 12 ). Until 2026-10-26 this was the non-HL2 arm's
    /// 580, which is what the fork's own build defines; HL2MP is held to here.
    ///
    /// <para>
    /// <b>THE ONE COPY.</b> Before the policy-player lane this number existed
    /// three times in the shim — <c>CStandaloneMoveHelper::PlayerFallingDamage</c>
    /// armed on it, <c>CHalfLife2::FlPlayerFallDamage</c> scaled by it, and
    /// <see cref="GameMovement.PlayerMaxSafeFallSpeed"/> decided from it — and
    /// each read a DIFFERENT fall velocity: the two native ones read the
    /// player's pre-move member, the managed one the value the loop had just
    /// computed. Two copies of one constant over two copies of one input is a
    /// fall that is safe in one place and lethal in another; there is one of
    /// each now and <see cref="GameMovement.PlayerMaxSafeFallSpeed"/> is this.
    /// </para>
    /// </summary>
    public const float PlayerMaxSafeFallSpeed = 526.5f;

    /// <summary>
    /// <c>PLAYER_FATAL_FALL_SPEED</c> — shareddefs.h:409, the same arm. The
    /// speed at which a landing does exactly 100 damage, which is what makes
    /// the slope below "100 health over the survivable range".
    /// </summary>
    public const float PlayerFatalFallSpeed = 922.5f;

    /// <summary>
    /// How much a landing hurts — stock's
    /// <c>( m_flFallVelocity - PLAYER_MAX_SAFE_FALL_SPEED ) * DAMAGE_FOR_FALL_SPEED</c>
    /// (multiplay_gamerules.cpp:647, singleplay_gamerules.cpp:295, and the
    /// shim's own copy in <c>CStandaloneMoveHelper::PlayerFallingDamage</c>).
    ///
    /// <para>
    /// <b>THE MULTIPLY COMES BEFORE THE DIVIDE, AND THAT IS NOT A STYLE
    /// CHOICE.</b> <c>DAMAGE_FOR_FALL_SPEED</c> is an unparenthesised macro —
    /// <c>100.0f / ( PLAYER_FATAL_FALL_SPEED - PLAYER_MAX_SAFE_FALL_SPEED )</c>
    /// — so at every one of its call sites the expansion is
    /// <c>(v - 580) * 100.0f / 444</c> and not <c>(v - 580) * (100.0f/444)</c>.
    /// In IEEE-754 single precision those are different numbers. Folding the
    /// constant here would make managed and stock disagree in the last bits for
    /// most inputs, which is exactly the kind of silent drift a port is
    /// supposed to remove rather than introduce.
    /// </para>
    ///
    /// <para>
    /// Answers 0 at or under <see cref="PlayerMaxSafeFallSpeed"/>. The caller
    /// still decides WHETHER to ask — stock arms the ask on the same threshold
    /// — but a rule that answers a negative number below its own boundary is a
    /// rule that heals a player who jumped off a kerb, so the floor is here as
    /// well as at the call site.
    /// </para>
    /// </summary>
    /// <param name="fallVelocity">
    /// <c>m_Local.m_flFallVelocity</c> at the moment of landing, in units per
    /// second and POSITIVE downwards.
    /// </param>
    public static float FallDamage(float fallVelocity)
    {
        if (fallVelocity <= PlayerMaxSafeFallSpeed)
            return 0f;

        return (fallVelocity - PlayerMaxSafeFallSpeed) * 100.0f
             / (PlayerFatalFallSpeed - PlayerMaxSafeFallSpeed);
    }

    /// <summary>
    /// The bit <see cref="ClipVelocity"/> sets for a floor.
    /// </summary>
    public const int BlockedFloor = 0x01;

    /// <summary>
    /// The bit <see cref="ClipVelocity"/> sets for a wall or a step.
    /// </summary>
    public const int BlockedWall = 0x02;

    /// <summary>
    /// Slide off a plane — <c>CGameMovement::ClipVelocity</c>
    /// (gamemovement.cpp:3268).
    ///
    /// <para>
    /// The hottest thing in this file: <c>TryPlayerMove</c> calls it once per
    /// blocking plane per bump, up to four bumps deep, and every solved
    /// collision the player ever has goes through it.
    /// </para>
    ///
    /// <para>
    /// Returns stock's blocking flags: 1 when the plane faces even slightly
    /// upward (a floor), 2 when it has no Z at all (a wall). Both, never — the
    /// two tests are mutually exclusive — but stock ORs them and so does this,
    /// because the caller tests bits.
    /// </para>
    /// </summary>
    /// <returns>Stock's blocked mask.</returns>
    public static int ClipVelocity(in Vector input, in Vector normal, out Vector output,
                                   float overbounce, float redirectCoeff)
    {
        float angle = normal.Z;

        int blocked = 0x00;             // Assume unblocked.
        if (angle > 0f)                 // A plane with a positive Z is a floor.
            blocked |= BlockedFloor;
        if (angle == 0f)                // No Z at all is vertical: wall or step.
            blocked |= BlockedWall;

        // How far along the plane to slide, from the incoming direction.
        float flBlocked = input.Dot(normal);
        float backoff = flBlocked * overbounce;

        output = new Vector(
            input.X - normal.X * backoff,
            input.Y - normal.Y * backoff,
            input.Z - normal.Z * backoff);

        // Stock iterates ONCE to make sure we are not still moving through the
        // plane, and once only. It is not a loop to convergence and making it
        // one would move the player somewhere stock does not.
        float adjust = output.Dot(normal);
        if (adjust < 0.0f)
        {
            output -= normal * adjust;
        }

        if (redirectCoeff > 0.0f)
        {
            // STOCK QUIRK, KEPT: no guard on a zero length. A clipped velocity
            // of exactly zero divides by zero here and stock hands the caller
            // a NaN, which CheckVelocity then zeroes on the next call. Adding
            // the guard would change the answer in the one case where stock
            // produces a number this port would have to invent.
            //
            // Unreachable in this build, and checked rather than assumed: the
            // coefficient arrives from TryPlayerMove's flSlideMultiplier, which
            // defaults to 0.f (gamemovement.h:175) and which all EIGHT of its
            // call sites in gamemovement.cpp take the default of. It is still
            // on the path, and it is cheaper to be faithful than to be sure.
            float flLen = output.Length;
            output *= (-1.0f * flBlocked * redirectCoeff + flLen) / flLen;
        }

        return blocked;
    }

    /// <summary>
    /// The view punch spring — <c>CGameMovement::DecayPunchAngle</c>
    /// (gamemovement.cpp:1236). Runs once per command from
    /// <c>CheckParameters</c>.
    ///
    /// <para>
    /// PunchAngle is a PREDICTED field with a 0.125 tolerance, so this is one
    /// of the five things the misprediction meter actually watches.
    /// </para>
    /// </summary>
    public static void DecayPunchAngle(ref Vector punchAngle, ref Vector punchAngleVel,
                                       float frameTime)
    {
        // STOCK QUIRK, KEPT: the threshold is 0.001 as a DOUBLE compared against
        // a float length-squared, and the test is on either vector — a punch
        // that has stopped moving but not yet returned to centre keeps
        // springing. Writing it as `>` on both, in this order, is stock's.
        if (punchAngle.LengthSquared > 0.001 || punchAngleVel.LengthSquared > 0.001)
        {
            punchAngle += punchAngleVel * frameTime;

            float damping = 1f - (PunchDamping * frameTime);
            if (damping < 0f)
            {
                damping = 0f;
            }

            punchAngleVel *= damping;

            // Torsional spring. Stock's comment asks whether this wants a
            // per-axis constant; it still does not have one.
            float springForceMagnitude = PunchSpringConstant * frameTime;
            springForceMagnitude = Math.Clamp(springForceMagnitude, 0f, 2f);
            punchAngleVel -= punchAngle * springForceMagnitude;

            // Don't wrap around. Stock's limits are asymmetric between the
            // axes — 89 on pitch and roll, 179 on yaw — and they are the ones
            // an angle can hold rather than the ones a punch should reach.
            punchAngle = new Vector(
                Math.Clamp(punchAngle.X, -89f, 89f),
                Math.Clamp(punchAngle.Y, -179f, 179f),
                Math.Clamp(punchAngle.Z, -89f, 89f));
        }
        else
        {
            punchAngle = Vector.Zero;
            punchAngleVel = Vector.Zero;
        }
    }

    /// <summary>
    /// Bound velocity and origin — <c>CGameMovement::CheckVelocity</c>
    /// (gamemovement.cpp:3172).
    ///
    /// <para>
    /// Called from the three gravity methods and from several places in the
    /// walk path, so it is the one method here with more than one caller in
    /// stock and the reason the gravity methods below fold it in rather than
    /// crossing again.
    /// </para>
    ///
    /// <para>
    /// Returns how many components it had to fix, which stock does not — stock
    /// prints a <c>DevMsg</c> per fix and returns nothing. A count is the same
    /// information in a form a test and a gate can read, and it is the only
    /// addition in this file.
    /// </para>
    /// </summary>
    /// <returns>The number of NaN or out-of-range components corrected.</returns>
    public static int CheckVelocity(ref Vector velocity, ref Vector origin, float maxVelocity)
    {
        int fixups = 0;

        // STOCK QUIRK, KEPT: stock snapshots the origin into a local BEFORE the
        // loop and writes the whole local back through SetAbsOrigin inside it,
        // per bad component. With `ref` there is no snapshot to get wrong, and
        // the result is identical because nothing else writes the origin during
        // the loop — but the shape is worth naming, because a reader comparing
        // the two files will notice the local is gone.
        for (int i = 0; i < 3; i++)
        {
            float v = Component(velocity, i);
            if (float.IsNaN(v))
            {
                SetComponent(ref velocity, i, 0f);
                fixups++;
                v = 0f;
            }

            if (float.IsNaN(Component(origin, i)))
            {
                SetComponent(ref origin, i, 0f);
                fixups++;
            }

            // Bound it.
            if (v > maxVelocity)
            {
                SetComponent(ref velocity, i, maxVelocity);
                fixups++;
            }
            else if (v < -maxVelocity)
            {
                SetComponent(ref velocity, i, -maxVelocity);
                fixups++;
            }
        }

        return fixups;
    }

    /// <summary>
    /// Which half of the frame's gravity is being applied — the three stock
    /// methods differ only in this and in the water-jump test.
    /// </summary>
    public enum GravityPhase
    {
        /// <summary><c>StartGravity</c> (gamemovement.cpp:1271): half a frame, no water-jump test.</summary>
        Start = 0,

        /// <summary><c>FinishGravity</c> (gamemovement.cpp:1728): the other half, and no base velocity.</summary>
        Finish = 1,

        /// <summary><c>AddGravity</c> (gamemovement.cpp:3215): a whole frame, for the non-walking move types.</summary>
        Add = 2,
    }

    /// <summary>
    /// Gravity — <c>StartGravity</c>, <c>FinishGravity</c> and <c>AddGravity</c>
    /// as one method, because the three differ in two lines and having three
    /// copies of the base-velocity handling is how the copies drift.
    ///
    /// <para>
    /// <c>CheckVelocity</c> is folded in exactly where stock calls it, at the
    /// end of all three.
    /// </para>
    /// </summary>
    /// <param name="entityGravity">
    /// The player's own gravity multiplier. STOCK QUIRK, KEPT: stock tests it
    /// for being non-zero rather than for having been set, so a player with
    /// gravity deliberately set to 0 falls at NORMAL speed rather than
    /// floating. Both the caller and this method would have to change to fix
    /// it, and the fix would be a gameplay change.
    /// </param>
    /// <returns>The number of components <see cref="CheckVelocity"/> corrected.</returns>
    public static int Gravity(GravityPhase phase, ref Vector velocity, ref Vector baseVelocity,
                              ref Vector origin, float entityGravity, float currentGravity,
                              float frameTime, float maxVelocity, bool waterJumping)
    {
        // StartGravity does NOT test the water-jump timer and the other two do.
        // That asymmetry is stock's and it is load-bearing: WaterJump() runs
        // between the two halves of the frame's gravity, so refusing the second
        // half without refusing the first is what gives a water jump its lift.
        if (phase != GravityPhase.Start && waterJumping)
            return 0;

        float entGravity = entityGravity != 0f ? entityGravity : 1.0f;

        // Stock's own comment on the 0.5: "yes, this 0.5 looks wrong, but it's
        // not." Half the frame's gravity before the move and half after is a
        // midpoint integration; a whole frame in one place is not.
        //
        // STOCK QUIRK, KEPT, AND IT IS A ONE-ULP QUIRK. The three methods write
        // the SAME product in three different orders, and the 0.5 is an
        // unsuffixed C literal — a DOUBLE. So StartGravity promotes to double
        // BEFORE multiplying by the frame time and rounds once at the store,
        // while FinishGravity rounds the float product first and only then
        // widens. Written as one expression the two agree to within an ulp and
        // disagree with stock; written as stock wrote them they are stock.
        // Naming it here because a reader will otherwise "simplify" it back.
        switch (phase)
        {
            case GravityPhase.Start:
                // ent_gravity * GetCurrentGravity() * 0.5 * gpGlobals->frametime
                velocity.Z = (float)(velocity.Z - (double)(entGravity * currentGravity) * 0.5 * frameTime);
                break;

            case GravityPhase.Finish:
                // ent_gravity * GetCurrentGravity() * gpGlobals->frametime * 0.5
                velocity.Z = (float)(velocity.Z - (double)(entGravity * currentGravity * frameTime) * 0.5);
                break;

            default:
                // AddGravity is the only one of the three that is float
                // throughout — a whole frame, no 0.5 to widen it.
                velocity.Z -= entGravity * currentGravity * frameTime;
                break;
        }

        if (phase != GravityPhase.Finish)
        {
            // The base velocity's vertical part is SPENT here and zeroed, so a
            // conveyor or a lift contributes once. FinishGravity does neither,
            // which is why it is not simply the same call with a flag.
            velocity.Z += baseVelocity.Z * frameTime;
            baseVelocity.Z = 0f;
        }

        return CheckVelocity(ref velocity, ref origin, maxVelocity);
    }

    /// <summary>
    /// Ground acceleration — <c>CGameMovement::Accelerate</c>
    /// (gamemovement.cpp:1880).
    ///
    /// <para>
    /// <paramref name="canAccelerate"/> is stock's <c>CanAccelerate()</c>
    /// hoisted out: it asks the player whether they are dead and whether they
    /// are in a water jump, and both are entity state this method deliberately
    /// does not reach for. Stock notes the virtual exists so a game can let
    /// dead observers move.
    /// </para>
    /// </summary>
    public static void Accelerate(ref Vector velocity, in Vector wishDir, float wishSpeed,
                                  float accel, float frameTime, float surfaceFriction,
                                  bool canAccelerate)
    {
        if (!canAccelerate)
            return;

        // See if we are changing direction a bit.
        float currentSpeed = velocity.Dot(wishDir);

        // Reduce wishspeed by the amount of veer.
        float addSpeed = wishSpeed - currentSpeed;

        if (addSpeed <= 0f)
            return;

        float accelSpeed = accel * frameTime * wishSpeed * surfaceFriction;

        if (accelSpeed > addSpeed)
            accelSpeed = addSpeed;

        // STOCK QUIRK, KEPT: the ground path does NOT add to m_outWishVel and
        // the air path does. m_outWishVel drives the player's animation and the
        // fall-damage-free "you meant to go there" signal, so the two paths
        // report different things — but changing it changes what the animation
        // sees, on both sides, for no movement reason.
        velocity += wishDir * accelSpeed;
    }

    /// <summary>
    /// Air acceleration — <c>CGameMovement::AirAccelerate</c>
    /// (gamemovement.cpp:1765). This is bunny-hopping.
    /// </summary>
    /// <param name="airSpeedCap">
    /// <c>GetAirSpeedCap()</c>, which is already a managed parameter
    /// (<see cref="Movement"/>'s AirSpeedCap; stock hardcodes 30 in a virtual
    /// nobody overrides).
    /// </param>
    public static void AirAccelerate(ref Vector velocity, ref Vector outWishVel,
                                     in Vector wishDir, float wishSpeed, float accel,
                                     float frameTime, float surfaceFriction, float airSpeedCap,
                                     bool canAccelerate)
    {
        if (!canAccelerate)
            return;

        // Cap speed.
        float wishSpd = wishSpeed;
        if (wishSpd > airSpeedCap)
            wishSpd = airSpeedCap;

        // Determine veer amount.
        float currentSpeed = velocity.Dot(wishDir);

        // See how much to add — against the CAPPED speed.
        float addSpeed = wishSpd - currentSpeed;

        if (addSpeed <= 0f)
            return;

        // THE QUIRK THAT IS THE GAME. The cap above is applied to the
        // ROOM LEFT (wishSpd) and NOT to the acceleration (wishSpeed), so a
        // player who holds strafe and turns keeps earning the full
        // acceleration against a budget that never grows. That asymmetry —
        // capped in one line, uncapped in the next — is where air strafing and
        // bunny-hopping come from. Making the two agree would be tidier and
        // would delete a movement technique the game has had since Quake.
        float accelSpeed = accel * wishSpeed * frameTime * surfaceFriction;

        if (accelSpeed > addSpeed)
            accelSpeed = addSpeed;

        velocity += wishDir * accelSpeed;
        outWishVel += wishDir * accelSpeed;
    }

    /// <summary>
    /// Ground friction — <c>CGameMovement::Friction</c> (gamemovement.cpp:1646).
    ///
    /// <para>
    /// <paramref name="friction"/> arrives ALREADY scaled — stock computes
    /// <c>sv_friction * m_surfaceFriction</c> and then asks the side's
    /// <see cref="SourceSharp.Shared.IMovementRules"/> hook (through the
    /// resolved pack), which is a managed decision that already existed.
    /// Passing the finished number keeps this method's rule (it touches only
    /// its arguments) and means the port REMOVES a boundary
    /// crossing rather than adding one: the friction rule is now asked on the
    /// managed side of a call that was going to happen anyway.
    /// </para>
    ///
    /// <para>
    /// The <c>IsX360()</c> branch stock carries is not reproduced and cannot
    /// change an answer here: it is a compile-time constant false on this
    /// platform, and its two arms differ only under <c>TF_DLL</c>.
    /// </para>
    /// </summary>
    public static void Friction(ref Vector velocity, ref Vector outWishVel, bool onGround,
                                float friction, float stopSpeed, float frameTime,
                                bool waterJumping)
    {
        // In a water jump cycle, no friction at all.
        if (waterJumping)
            return;

        float speed = velocity.Length;

        // If too slow, return. Note this leaves outWishVel alone, which the
        // line at the bottom does not.
        if (speed < 0.1f)
        {
            return;
        }

        float drop = 0f;

        if (onGround)
        {
            // Bleed off some speed, but below the bleed threshold bleed the
            // threshold amount instead.
            float control = (speed < stopSpeed) ? stopSpeed : speed;
            drop += control * friction * frameTime;
        }

        float newSpeed = speed - drop;
        if (newSpeed < 0f)
            newSpeed = 0f;

        if (newSpeed != speed)
        {
            // Proportion of the old speed we are keeping.
            newSpeed /= speed;
            velocity *= newSpeed;
        }

        // STOCK BUG, KEPT, AND IT IS A REAL ONE. `newSpeed` is a RATIO only
        // inside the branch above; when nothing was dropped — every airborne
        // frame, since `drop` stays 0 off the ground — it is still a SPEED, so
        // this subtracts (1 - 400) * velocity from the wish velocity and
        // m_outWishVel ends up ~400x the velocity, pointing backwards.
        //
        // It is kept because m_outWishVel is read by the player animation and
        // the two sides both read stock's answer today. Fixing it here fixes it
        // on one side of a predicted loop first, which is the exact shape of
        // the bug this lane exists to avoid.
        outWishVel -= velocity * (1f - newSpeed);
    }

    /// <summary>
    /// The duck transition curve — <c>CGameMovement::SplineFraction</c>
    /// (gamemovement.cpp:4178). An ease-in/ease-out over a 0..1 fraction.
    /// </summary>
    public static float SplineFraction(float value, float scale)
    {
        value = scale * value;
        float valueSquared = value * value;

        // 3v² − 2v³, written by stock as 3*v² − 2*v²*v.
        return 3f * valueSquared - 2f * valueSquared * value;
    }

    /// <summary>
    /// View roll from sideways motion — <c>CGameMovement::CalcRoll</c>
    /// (gamemovement.cpp:3321).
    ///
    /// <para>
    /// <paramref name="right"/> is <c>AngleVectors</c>' right vector for the
    /// view angles; the caller has it, and deriving it here would mean
    /// reproducing the engine's SinCos rather than the movement code.
    /// </para>
    ///
    /// <para>
    /// <c>sv_rollangle</c> defaults to <b>0</b> in this build
    /// (movevars_shared.cpp:75), so stock's answer is a constant zero unless a
    /// server operator changes it. Ported anyway, because "does nothing by
    /// default" and "is not on the path" are different claims.
    /// </para>
    /// </summary>
    public static float CalcRoll(in Vector right, in Vector velocity,
                                 float rollAngle, float rollSpeed)
    {
        float side = velocity.Dot(right);

        // STOCK QUIRK, KEPT: zero rolls POSITIVE. `side < 0 ? -1 : 1` puts the
        // boundary on the negative side, so a player moving exactly along their
        // own facing gets +rollangle rather than 0 — invisible at the default,
        // and a discontinuity a "tidier" sign function would remove.
        float sign = side < 0f ? -1f : 1f;
        side = MathF.Abs(side);

        float value = rollAngle;
        if (side < rollSpeed)
        {
            side = side * value / rollSpeed;
        }
        else
        {
            side = value;
        }

        return side * sign;
    }

    /// <summary>
    /// Count one frame off the duck timers — <c>CGameMovement::ReduceTimers</c>
    /// (gamemovement.cpp:1113).
    ///
    /// <para>
    /// All four are PREDICTED fields compared exactly (no tolerance attribute
    /// on DuckTime, DuckJumpTime or JumpTime in PlayerNetState), so this is
    /// arithmetic the meter checks to the bit.
    /// </para>
    ///
    /// <para>
    /// STOCK QUIRK, KEPT: the timers are in MILLISECONDS and clamp at zero
    /// rather than wrapping negative, but only when they were already positive
    /// — a timer sitting at exactly 0 is not touched, and a negative one stays
    /// negative forever. Stock's four blocks are identical and are collapsed
    /// into one helper here; the collapse is the only difference.
    /// </para>
    /// </summary>
    public static void ReduceTimers(ref float duckTime, ref float duckJumpTime,
                                    ref float jumpTime, ref float swimSoundTime,
                                    float frameTime)
    {
        float frameMsec = 1000.0f * frameTime;

        CountDown(ref duckTime, frameMsec);
        CountDown(ref duckJumpTime, frameMsec);
        CountDown(ref jumpTime, frameMsec);
        CountDown(ref swimSoundTime, frameMsec);
    }

    private static void CountDown(ref float timer, float frameMsec)
    {
        if (timer > 0f)
        {
            timer -= frameMsec;
            if (timer < 0f)
            {
                timer = 0f;
            }
        }
    }

    //=========================================================================
    // FLIGHT -- plan_entity.md step 2f.
    //
    // The arithmetic of CStandaloneEntity::UpdateFlight, moved here under the
    // same selection rule the rest of this file is chosen by: every line below
    // reads and writes nothing but its arguments. The TRACE ANSWER is an
    // argument for exactly that reason -- the trace itself is an engine call
    // and stays with the caller (SourceSharp.Server/EntityMotion.cs), and what
    // comes back is data.
    //
    // That split is what makes the stock fixture a real comparison rather than
    // a replay of this code against itself: the recorded rows carry the trace
    // answers the NATIVE integrator was given, so feeding them back in here
    // compares two implementations rather than one.
    //=========================================================================

    /// <summary>
    /// Gravity, then the step it wants to take — the first half of one frame of
    /// flight.
    ///
    /// <para>
    /// The gravity term is skipped when the scale is exactly zero rather than
    /// added as a zero. That is stock's split between <c>MOVETYPE_FLY</c> and
    /// <c>MOVETYPE_FLYGRAVITY</c> (<c>physics_main_shared.cpp:1775</c>) and it
    /// is what <c>UpdateFlight</c> wrote — <b>and it is arithmetically
    /// inert</b>, which is worth saying rather than leaving for the next reader
    /// to wonder about: a scale of exactly zero makes the term zero, and
    /// subtracting zero changes nothing. Mutating the guard to <c>if (true)</c>
    /// goes red on NOTHING, which is how it was measured. So the guard is kept
    /// because it says what the two movetypes are, not because removing it
    /// would break anything, and no fact in <c>EntityMotionTests</c> claims
    /// otherwise: what is asserted is the BEHAVIOUR (a zero scale leaves the
    /// velocity alone), which an inverted guard does break.
    /// </para>
    ///
    /// <para>
    /// SEMI-IMPLICIT: gravity lands on the velocity BEFORE the velocity is used
    /// for the step, so a frame of falling is <c>(v - g·dt)·dt</c> and not
    /// <c>v·dt</c>. Reversing the two is a different trajectory that still
    /// looks like a trajectory, which is exactly the kind of drift the fixture
    /// exists to catch.
    /// </para>
    /// </summary>
    public static FlightStep AdvanceFlight(in FlightInput input)
    {
        Vector velocity = input.Velocity;

        if (input.GravityScale != 0f)
            velocity.Z -= input.Gravity * input.GravityScale * input.FrameTime;

        return new FlightStep(velocity, input.Origin + velocity * input.FrameTime);
    }

    /// <summary>
    /// What the trace's answer means — the second half of one frame of flight.
    ///
    /// <para>
    /// The branch is on the FRACTION and not on "did it hit something", because
    /// that is the test <c>UpdateFlight</c> makes: a trace that named no entity
    /// still stopped the step if it ran into the world, and the world is most
    /// of what a projectile hits.
    /// </para>
    ///
    /// <para>
    /// A blocked step stops WHERE IT STRUCK rather than at the destination, so
    /// an explosion happens against the surface instead of inside it; a bounce
    /// reflects about the surface and is then LIFTED CLEAR OF IT by one unit
    /// along the normal, because resuming exactly on the surface starts the
    /// next frame's trace inside it, which reports a second hit at the same
    /// place and eats every bounce in one tick.
    /// </para>
    /// </summary>
    /// <param name="fraction"><c>trace_t::fraction</c>.</param>
    /// <param name="endPosition"><c>trace_t::endpos</c>.</param>
    /// <param name="normal"><c>trace_t::plane.normal</c>.</param>
    public static FlightResult ResolveFlight(in FlightInput input, in FlightStep step,
                                             float fraction, in Vector endPosition,
                                             in Vector normal)
    {
        if (fraction >= 1.0f)
            return new FlightResult(step.Destination, step.Velocity, true, input.BouncesLeft, false);

        if (input.BouncesLeft > 0)
        {
            Vector bounced = step.Velocity - 2.0f * step.Velocity.Dot(normal) * normal;

            return new FlightResult(endPosition + normal, bounced, true,
                                    input.BouncesLeft - 1, true);
        }

        return new FlightResult(endPosition, step.Velocity, false, input.BouncesLeft, true);
    }

    /// <summary>
    /// <c>CBaseEntity::PhysicsNoclip</c> (<c>physics_main.cpp:1638</c>): the
    /// velocity applied to the origin and NOTHING asked about what is there.
    ///
    /// <para>
    /// Its own method rather than <see cref="AdvanceFlight"/> with the trace
    /// skipped, because "no gravity, no collisions" is a statement about the
    /// movetype and not a special case of flying. Stock writes it as
    /// <c>VectorMA(origin, frametime, velocity, origin)</c>, which is this.
    /// </para>
    /// </summary>
    public static Vector NoClipStep(in Vector origin, in Vector velocity, float frameTime)
        => origin + velocity * frameTime;

    private static float Component(in Vector v, int i) => i switch
    {
        0 => v.X,
        1 => v.Y,
        _ => v.Z,
    };

    private static void SetComponent(ref Vector v, int i, float value)
    {
        switch (i)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }
}

/// <summary>
/// Which of Source's movetypes an entity is integrated as —
/// <c>plan_entity.md</c> step 2f.
///
/// <para>
/// <b>Not <see cref="MoveType"/>, and the difference is the point.</b>
/// <see cref="MoveType"/> mirrors the engine's <c>MOVETYPE_*</c> numbers so
/// that <c>EntityGetState</c> can report what the engine thinks an entity is.
/// This says what the MANAGED integrator does with it each frame, and the two
/// are not the same list: stock has no <c>MOVETYPE_TOSS</c> at all (it is
/// <c>MOVETYPE_FLYGRAVITY</c>) and no <c>MOVETYPE_FOLLOW</c> at all (following
/// is <c>MOVETYPE_NONE</c> plus a move parent, dispatched to
/// <c>PhysicsRigidChild</c>). Giving these members engine numbers would have
/// invented two.
/// </para>
/// </summary>
public enum MotionKind
{
    /// <summary>Nothing integrates it. Stock's <c>MOVETYPE_NONE</c> with no parent.</summary>
    None = 0,

    /// <summary>
    /// Straight-line flight that collides — stock's <c>MOVETYPE_FLY</c>, which
    /// <c>physics_main_shared.cpp:1985</c> dispatches to <c>PhysicsToss</c>
    /// with the gravity arm not taken.
    /// </summary>
    Fly = 1,

    /// <summary>
    /// The same flight WITH gravity — stock's <c>MOVETYPE_FLYGRAVITY</c>,
    /// which is the same <c>PhysicsToss</c> with
    /// <c>PhysicsAddGravityMove</c> in front of it. The difference between this
    /// and <see cref="Fly"/> is one number, so it is carried as one:
    /// <see cref="FlightInput.GravityScale"/>.
    /// </summary>
    Toss = 2,

    /// <summary>
    /// Velocity applied with NO trace at all — stock's <c>MOVETYPE_NOCLIP</c>
    /// and <c>CBaseEntity::PhysicsNoclip</c> (<c>physics_main.cpp:1638</c>).
    /// </summary>
    NoClip = 3,

    /// <summary>
    /// Its position is somebody else's problem — stock's <c>MOVETYPE_NONE</c>
    /// with a move parent, which <c>PhysicsSimulate</c> sends to
    /// <c>PhysicsRigidChild</c> and which does not integrate a velocity of its
    /// own. The integrator's job here is to NOT move it, so that the
    /// hierarchy's composition is the only thing writing its origin.
    /// </summary>
    Follow = 4,
}

/// <summary>
/// Everything one frame of flight is decided from —
/// <c>CStandaloneEntity::UpdateFlight</c>'s inputs, as an argument list.
///
/// <para>
/// A struct rather than six parameters because it is also the row shape of the
/// recorded stock fixture (<c>Fixtures/flight-trajectory-stock.tsv</c>): the
/// test replays real frames of the native integrator through
/// <see cref="MoveMath.AdvanceFlight"/> and <see cref="MoveMath.ResolveFlight"/>
/// and compares the answers, so what the fixture carries and what these take
/// are deliberately the same list.
/// </para>
/// </summary>
/// <param name="FrameTime"><c>gpGlobals-&gt;frametime</c>.</param>
/// <param name="Gravity"><c>GetCurrentGravity()</c> — <c>sv_gravity</c>.</param>
/// <param name="GravityScale">
/// Per-entity, and a SCALE rather than on or off: a rocket sets it to zero and
/// flies flat, a bolt leaves a little on and drops over distance. Exactly zero
/// skips the gravity term rather than adding a zero, which is stock's own
/// <c>MOVETYPE_FLY</c> / <c>MOVETYPE_FLYGRAVITY</c> split.
/// </param>
public readonly record struct FlightInput(
    float FrameTime,
    float Gravity,
    float GravityScale,
    Vector Origin,
    Vector Velocity,
    int BouncesLeft);

/// <summary>
/// The step a frame of flight WANTS to take, before anything is asked whether
/// it may: the velocity gravity has already been added to, and where that
/// velocity would put the entity.
/// </summary>
/// <param name="Destination">
/// Where the trace is aimed. It is not where the entity ends up unless the
/// trace comes back clear — <c>UpdateFlight</c>'s comment on tracing rather
/// than teleporting is the whole reason this is two calls: "a projectile moves
/// further per frame than it is wide … so a position test at the destination
/// misses every wall it went through on the way".
/// </param>
public readonly record struct FlightStep(Vector Velocity, Vector Destination);

/// <summary>What one frame of flight leaves behind.</summary>
/// <param name="Struck">
/// Whether the step was stopped. The caller reports the impact from this;
/// <see cref="MoveMath.ResolveFlight"/> does not, because it has no entity to
/// report it to and no way to be told one without taking an engine.
/// </param>
public readonly record struct FlightResult(
    Vector Origin,
    Vector Velocity,
    bool Flying,
    int BouncesLeft,
    bool Struck);
