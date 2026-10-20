//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The decisions of one server user command around the movement loop
/// (plan_server_native_v2 T4g): stock's <c>CPlayerMove::RunCommand</c>
/// prepare, <c>SetupMove</c> and <c>FinishMove</c> (player_command.cpp) and
/// <c>CBasePlayer::AdjustPlayerTimeBase</c> (player.cpp), as
/// CStandalonePlayer::RunQueuedCommands, RunCommandStage and RunCommandMove
/// held them (shell_standalone_player.cpp), with the divergences those
/// recorded kept: a FIXANGLE_RELATIVE is consumed here, the body's angles are
/// the yaw alone, and the player's old forward move is never stored.
/// Pure: the caller owns the state and the engine calls.
/// </summary>
public static class ServerCommandRules
{
    /// <summary><c>k_flMaxEntityEulerAngle</c> (entity_shared_statics.cpp).</summary>
    public const float MaxEntityEulerAngle = 360.0f * 1000.0f;

    /// <summary><c>FL_BASEVELOCITY</c> (const.h, the multiplayer block this build compiles).</summary>
    public const int FlBaseVelocity = 1 << 24;

    /// <summary><c>FL_CLIENT</c> (const.h, the multiplayer block).</summary>
    public const int FlClient = 1 << 8;

    /// <summary><c>FIXANGLE_NONE</c>, <c>_ABSOLUTE</c>, <c>_RELATIVE</c> (PlayerState.h's users, edict.h).</summary>
    public const int FixAngleNone = 0, FixAngleAbsolute = 1, FixAngleRelative = 2;

    /// <summary>sv_clockcorrection_msecs' default, the bound native quoted rather than tuned.</summary>
    public const float ClockCorrectionSeconds = 0.060f;

    /// <summary>
    /// <c>TIME_TO_TICKS( 0.060 )</c>: the correction window in ticks, as
    /// RunQueuedCommands computed it, <c>(int)( 0.5f + 0.060f / interval )</c>.
    /// </summary>
    public static int ClockCorrectionTicks(float tickInterval)
        => (int)(0.5f + ClockCorrectionSeconds / tickInterval);

    /// <summary>
    /// <c>AdjustPlayerTimeBase</c> (tickbase_rule.h's TickBase_Adjust): where
    /// a frame's commands start counting from. No commands leaves the base
    /// alone; single player (or a base never placed) pins it so the last
    /// command lands on tickcount; otherwise a base whose batch would end
    /// outside the correction window either side of the ideal tick is moved to
    /// end on it.
    /// </summary>
    public static int AdjustTickBase(int tickBase, int tickCount, int simulationTicks,
                                     int simTicksThisFrame, int correctionTicks, bool singlePlayer)
    {
        if (simulationTicks <= 0)
            return tickBase;

        if (singlePlayer)
            return tickCount - simulationTicks + simTicksThisFrame;

        int idealFinalTick = tickCount + correctionTicks;
        int estimatedFinalTick = tickBase + simulationTicks;

        if (estimatedFinalTick > idealFinalTick + correctionTicks ||
            estimatedFinalTick < idealFinalTick - correctionTicks)
            return idealFinalTick - simulationTicks + simTicksThisFrame;

        return tickBase;
    }

    /// <summary>
    /// The two sanity rules RunCommand applies to the command's angles before
    /// anything reads them: a non-finite component, or one outside
    /// ±<see cref="MaxEntityEulerAngle"/> (<c>IsEntityQAngleReasonable</c>),
    /// zeroes all three.
    /// </summary>
    public static Vector SaneViewAngles(Vector angles)
    {
        bool valid = float.IsFinite(angles.X) && float.IsFinite(angles.Y) && float.IsFinite(angles.Z);
        bool reasonable =
            angles.X > -MaxEntityEulerAngle && angles.X < MaxEntityEulerAngle &&
            angles.Y > -MaxEntityEulerAngle && angles.Y < MaxEntityEulerAngle &&
            angles.Z > -MaxEntityEulerAngle && angles.Z < MaxEntityEulerAngle;

        return valid && reasonable ? angles : Vector.Zero;
    }

    /// <summary>
    /// Copy from the command to the player unless the game set the angle with
    /// fixangle: FIXANGLE_NONE takes the command's, FIXANGLE_RELATIVE adds the
    /// pending change and is consumed (native's, not stock's), FIXANGLE_ABSOLUTE
    /// keeps the angle the game set until the engine has sent it.
    /// </summary>
    public static (Vector VAngle, int FixAngle) CommandAngles(Vector vAngle, int fixAngle,
                                                              Vector angleChange, Vector commandAngles)
        => fixAngle switch
        {
            FixAngleNone => (commandAngles, FixAngleNone),
            FixAngleRelative => (commandAngles + angleChange, FixAngleNone),
            _ => (vAngle, fixAngle),
        };

    /// <summary>
    /// Base velocity becomes real velocity (stock's CheckMovingGround, its
    /// conveyor half): unless FL_BASEVELOCITY says a push was applied this
    /// frame, the base is added at <c>1 + tick/2</c> and cleared; the flag is
    /// cleared either way.
    /// </summary>
    public static void FoldBaseVelocity(ref Vector velocity, ref Vector baseVelocity, ref int flags,
                                        float tickInterval)
    {
        if ((flags & FlBaseVelocity) == 0)
        {
            velocity += (1.0f + (tickInterval * 0.5f)) * baseVelocity;
            baseVelocity = Vector.Zero;
        }

        flags &= ~FlBaseVelocity;
    }

    /// <summary>
    /// FinishMove's body pitch: the final pitch folded into ±180 and clamped
    /// to ±90.
    /// </summary>
    public static float BodyPitch(float pitch)
    {
        if (pitch > 180.0f)
            pitch -= 360.0f;

        return Math.Clamp(pitch, -90.0f, 90.0f);
    }

    /// <summary>
    /// SetupMove over a standalone player: the command's inputs and the
    /// player's own words into the loop's state. The angles are the new
    /// <c>v_angle</c>; the old angles are the one before this command's.
    /// </summary>
    public static void SetupMove(MoveState s, in UserCmd cmd, Vector viewAngles, Vector vAngle,
                                 Vector oldAngles, Vector origin, Vector previouslyPredictedOrigin,
                                 float maxSpeed, int oldButtons, float oldForwardMove)
    {
        s.FirstRunOfFunctions = true;
        s.GameCodeMovedPlayer = !Same(previouslyPredictedOrigin, origin);
        s.ImpulseCommand = (byte)cmd.Impulse;
        s.ViewAngles = viewAngles;
        s.Buttons = cmd.Buttons;
        s.ForwardMove = cmd.ForwardMove;
        s.SideMove = cmd.SideMove;
        s.UpMove = cmd.UpMove;
        s.ClientMaxSpeed = maxSpeed;
        s.MaxSpeed = maxSpeed;
        s.OldButtons = oldButtons;
        s.OldForwardMove = oldForwardMove;
        s.Angles = vAngle;
        s.OldAngles = oldAngles;
        s.Origin = origin;
        s.ConstraintCenter = Vector.Zero;
        s.ConstraintRadius = 0f;
        s.ConstraintWidth = 0f;
        s.ConstraintSpeedFactor = 0f;
        s.CommandNumber = cmd.CommandNumber;
    }

    /// <summary>Two positions, bit for bit: <c>Vector::operator!=</c>.</summary>
    private static bool Same(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
}
