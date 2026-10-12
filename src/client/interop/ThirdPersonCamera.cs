//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// Where the third-person camera sits — plan_entity.md §3.10, the
/// policy-player lane.
///
/// <para>
/// §3.10: <i>"CInput's remaining entity references … are the local player's eye
/// and the third-person camera, answered from the block and from
/// <c>thirdperson_camera.h</c>'s policy moved to managed."</i> This is that
/// move. The header is gone; what it held was the orbit and its ±89° pitch
/// clamp, the sign convention that back is negative forward, how much of a
/// wanted displacement survives a hull sweep, the 30-unit floor under which a
/// pulled-in camera gives up and returns to the eye, and whether the placement
/// is one the player's own model should be drawn for. Every one of those
/// decides what the frame shows.
/// </para>
///
/// <para>
/// <b>WHY THE SHELL COMPUTES THIS AT ALL</b>, rather than calling the one stock
/// has — the argument the header carried, kept because it is the reason this
/// code exists rather than a note about it. Stock's third-person camera is
/// <c>CInput</c> plus <c>CThirdPersonManager</c> plus <c>C_BasePlayer</c>, and
/// the last of those has no instances in this build. Three places make that
/// fatal rather than cosmetic:
/// </para>
///
/// <para>
/// <c>ClientModeShared::OverrideView</c> returns at <c>GetLocalPlayer()</c>
/// before it reaches its third-person branch at all — so the shell's
/// <c>OverrideView</c>, which is the one that runs, never had a third-person
/// branch. <c>CThirdPersonManager::GetDistanceFraction</c> multiplies the whole
/// offset by <c>m_flTargetFraction</c>, written ONLY by <c>PositionCamera</c>,
/// which is a no-op when its <c>CBasePlayer *</c> is NULL — and it is a global,
/// so it starts at zero, and <c>CThirdPersonManager::Init</c> is called from
/// <c>LevelShutdown</c> and therefore never before the first map. A faithful
/// copy of stock's branch multiplies the offset by zero and photographs a
/// first-person frame that looks exactly like a camera that was never moved.
/// And <c>CInput::CAM_Think</c>'s collision pass hands <c>PositionCamera</c>
/// <c>C_BasePlayer::GetLocalPlayer()</c> for the same NULL.
/// </para>
///
/// <para>
/// <b>THE CONVARS STAY STOCK'S</b> — <c>cam_idealdist</c>,
/// <c>cam_idealdistright</c>, <c>cam_idealdistup</c>, <c>cam_idealpitch</c>,
/// <c>cam_idealyaw</c>, <c>cam_collision</c> — and are read by the caller, not
/// here. They are declared <c>static</c> inside stock's <c>in_camera.cpp</c>,
/// which is where <c>cam_in</c> and the rest of the camera commands write them;
/// a second declaration of the same names would be two variables answering to
/// one word. Nothing here reads global state: a rule that does is a rule a test
/// can only ask about one configuration of.
/// </para>
///
/// <para>
/// <b>TWO PHASES, ONE CROSSING EACH, AND ONLY IN THIRD PERSON.</b> The hull
/// sweep sits between the two halves of the rule — the camera must know where
/// it wants to go before anything can trace towards it — so the slot is entered
/// twice per rendered frame while <c>thirdperson</c> is on, and not at all
/// otherwise. `thirdperson` is a developer command; this mod does not ship in
/// that mode, and the live gate never enters it.
/// </para>
/// </summary>
public static class ThirdPersonCamera
{
    // IN SourceSharp.Abi AND NOT SourceSharp.Client, for the reason Movement and
    // PlayerCommandQueue give: this is the one assembly the bootstrap can see,
    // and the slot that reaches it is a bootstrap export. It is still CLIENT
    // ONLY -- the server never registers the slot -- and nothing in it is
    // reachable from the server side by accident.

    /// <summary>
    /// How far behind the eye the camera may sit before it is not a
    /// third-person camera any more.
    ///
    /// <para>
    /// Stock's <c>CAM_MIN_DIST</c>, quoted: <c>cam_in</c> clamps to it and drops
    /// back to first person below it. It is used here for the OPPOSITE reason —
    /// as the distance under which a pulled-in camera should give up and go back
    /// to the eye rather than sit inside the player's own head, drawing the
    /// inside of a skull.
    /// </para>
    /// </summary>
    public const float MinDistance = 30.0f;

    /// <summary>
    /// The pitch a camera may not be tilted past. Stock's <c>c_maxpitch</c> /
    /// <c>c_minpitch</c>, applied here to the SUM of the view pitch and the
    /// orbit rather than to the orbit alone.
    ///
    /// <para>
    /// A camera pitched past vertical does not merely look odd:
    /// <c>AngleVectors</c> gives it an up vector that has flipped, so the roll
    /// of the whole frame inverts as the player looks up.
    /// </para>
    /// </summary>
    public const float MaxPitch = 89.0f;

    /// <summary>
    /// The camera's own angles: the view angles with the orbit added, pitch
    /// clamped and yaw wrapped.
    ///
    /// <para>
    /// Pitch is CLAMPED and yaw is NOT, which is stock's split
    /// (<c>c_minpitch</c>/<c>c_maxpitch</c> against
    /// <c>c_minyaw</c>/<c>c_maxyaw</c>, which the caller has already applied to
    /// the IDEAL angles).
    /// </para>
    /// </summary>
    public static (float Pitch, float Yaw) CameraAngles(float viewPitch, float viewYaw,
                                                  float idealPitch, float idealYaw)
    {
        float pitch = viewPitch + idealPitch;

        if (pitch > MaxPitch)
            pitch = MaxPitch;
        else if (pitch < -MaxPitch)
            pitch = -MaxPitch;

        float yaw = viewYaw + idealYaw;

        while (yaw > 180.0f)
            yaw -= 360.0f;
        while (yaw <= -180.0f)
            yaw += 360.0f;

        return (pitch, yaw);
    }

    /// <summary>
    /// Where the camera wants to sit, as a displacement from the eye.
    ///
    /// <para>
    /// <b>BACK IS NEGATIVE FORWARD</b>, and that sign is the whole reason this
    /// is a function with a test rather than three lines at the call site. A
    /// camera placed +forward from the eye is in front of the player looking
    /// away from them: the model is behind the near plane, the screen shows the
    /// room, and the frame is indistinguishable from a camera that did not move
    /// — the same false negative the zero fraction above produces, arrived at
    /// differently.
    /// </para>
    /// </summary>
    /// <param name="basis">The camera's own axes, from its angles.</param>
    /// <param name="back">Units behind the eye — stock's <c>cam_idealdist</c>.</param>
    /// <param name="right">Units to the camera's right — <c>cam_idealdistright</c>.</param>
    /// <param name="up">Units above it — <c>cam_idealdistup</c>.</param>
    public static Vector Displacement(in Angles.Basis basis, float back, float right, float up)
        => (basis.Forward * -back) + (basis.Right * right) + (basis.Up * up);

    /// <summary>
    /// How much of the wanted displacement survives what a hull sweep from the
    /// eye found.
    ///
    /// <para>
    /// A sweep that reached the far end answers 1 and the camera goes where it
    /// wanted. A sweep stopped part way answers the fraction it reached, and the
    /// camera is pulled in to there — stock's rule, the same one
    /// <c>CThirdPersonManager::PositionCamera</c> applies through
    /// <c>m_vecCameraOffset[DIST]</c>.
    /// </para>
    ///
    /// <para>
    /// <b>THE FLOOR IS THE PART THAT IS NOT STOCK'S</b>, and it is here because
    /// of what this camera is for. A camera pulled to 3 units behind the eye is
    /// inside the player model: the near plane clips the chest, the frame fills
    /// with the inside of a head, and a screenshot gate reading "the model did
    /// not draw" would be reading a camera problem. Below
    /// <see cref="MinDistance"/> the answer is 0 — go back to the eye, where at
    /// least the frame is a frame — rather than a small positive number, which
    /// is the only value that produces an unreadable one.
    /// </para>
    /// </summary>
    public static float SurvivingFraction(float traceFraction, float wishLength)
    {
        if (traceFraction >= 1.0f)
            return 1.0f;

        if (traceFraction <= 0.0f)
            return 0.0f;

        if (wishLength * traceFraction < MinDistance)
            return 0.0f;

        return traceFraction;
    }

    /// <summary>
    /// Whether a camera placed with this much of its wish is actually behind
    /// the player, or has collapsed onto the eye.
    ///
    /// <para>
    /// The caller uses it to decide whether the player's own model should draw:
    /// a camera at the eye is a first-person camera whatever the mode says, and
    /// drawing the model there fills the screen with the inside of it.
    /// </para>
    /// </summary>
    public static bool SeesTheOwner(float survivingFraction, float wishLength)
        => survivingFraction * wishLength >= MinDistance;

    /// <summary>
    /// The <c>ThirdPersonCamera</c> crossing, both phases.
    ///
    /// <para>
    /// PLACE answers the camera's angles, the displacement it wants and how long
    /// that is; the caller sweeps a hull along it. RESOLVE answers how much of
    /// the wish to keep and whether the player's own model should draw for it.
    /// </para>
    /// </summary>
    /// <returns>1 when the phase was understood, 0 otherwise.</returns>
    public static int Answer(ref ThirdPersonCameraBlob io)
    {
        switch (io.Phase)
        {
            case Place:
            {
                (float pitch, float yaw) =
                    CameraAngles(io.ViewAngles.X, io.ViewAngles.Y, io.IdealPitch, io.IdealYaw);

                // ROLL IS ZERO AND NOT THE VIEW'S. A third-person camera that
                // inherited the player's roll would tilt the whole world when
                // they took a punch, which is a view effect meant for the eye.
                io.CameraAngles = new Vector(pitch, yaw, 0f);

                Vector wish = Displacement(Angles.Vectors(io.CameraAngles),
                                           io.IdealDist, io.IdealDistRight, io.IdealDistUp);

                io.Wish = wish;
                io.WishLength = wish.Length;
                return 1;
            }

            case Resolve:
            {
                float surviving = SurvivingFraction(io.TraceFraction, io.WishLength);

                io.SurvivingFraction = surviving;
                io.SeesOwner = SeesTheOwner(surviving, io.WishLength) ? 1 : 0;
                return 1;
            }
        }

        return 0;
    }

    /// <summary><c>SOURCESHARP_THIRDPERSON_PHASE_PLACE</c>.</summary>
    public const int Place = 0;

    /// <summary><c>SOURCESHARP_THIRDPERSON_PHASE_RESOLVE</c>.</summary>
    public const int Resolve = 1;
}
