//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// A weapon's kick on the view, per shot: stock's AddViewKick and the ViewPunch in
/// each HL2MP weapon's attack. A range is a punch drawn from it; a machine gun's
/// (<see cref="SlideLimit"/> above zero) grows the longer the trigger is held.
/// </summary>
/// <param name="ResetFirst">The pistol's ViewPunchReset before each shot: quick fire does not drift the aim.</param>
/// <param name="MaxVertical">DoMachineGunKick's maxVerticleKickAngle.</param>
/// <param name="SlideLimit">DoMachineGunKick's slideLimitTime, seconds.</param>
/// <param name="Disorients">The .357's "disorient the player": the aim itself is knocked, on the predicting client.</param>
public readonly record struct WeaponKick(float PitchMin = 0f, float PitchMax = 0f, float YawMin = 0f, float YawMax = 0f,
                                         bool ResetFirst = false, float MaxVertical = 0f, float SlideLimit = 0f,
                                         bool Disorients = false)
{
    /// <summary>KICK_MIN_X, KICK_MIN_Y and KICK_MIN_Z, degrees.</summary>
    private const float MinX = 0.2f, MinY = 0.2f, MinZ = 0.1f;

    /// <summary>DoMachineGunKick's clip on the punch: QAngle( 24, 3, 1 ).</summary>
    private const float ClipX = 24f, ClipY = 3f, ClipZ = 1f;

    /// <summary>CBasePlayer::ViewPunch: the punch's velocity, twenty times the kick.</summary>
    private const float VelocityPerDegree = 20f;

    /// <summary>DoMachineGunKick's "clear out anything under 10 degrees punch".</summary>
    private const float MachineGunResetTolerance = 10f;

    /// <summary>
    /// GetAutoaimVector's angles: a bullet goes where the punched view points, so a
    /// spray climbs with the crosshair.
    /// </summary>
    public static Vector Aim(Vector eyeAngles, PlayerNetState? carrier)
        => carrier is null ? eyeAngles : eyeAngles + carrier.PunchAngle.AsVector();

    /// <summary>What comes before the shot: the pistol's ViewPunchReset.</summary>
    public void BeforeShot(PlayerNetState carrier)
    {
        if (ResetFirst)
            Reset(carrier, 0f);
    }

    /// <summary>
    /// The kick onto <paramref name="carrier"/>'s punch, after the shot, from the command's seed, the
    /// trigger held for <paramref name="fireDuration"/> seconds.
    /// </summary>
    public void Apply(PlayerNetState carrier, SeededUniformStream random, int seed, float fireDuration)
    {
        Vector kick;
        if (SlideLimit > 0f)
        {
            Reset(carrier, MachineGunResetTolerance);
            kick = MachineGun(carrier.PunchAngle.AsVector(), random, seed, fireDuration);
        }
        else
        {
            random.SetSeed(ShotSpread.FirstSeed(seed));
            kick = new Vector(random.RandomFloat(PitchMin, PitchMax), random.RandomFloat(YawMin, YawMax), 0f);
        }

        Vector velocity = carrier.PunchAngleVel.AsVector() + kick * VelocityPerDegree;
        carrier.PunchAngleVel = QAngle.FromVector(velocity);
    }

    /// <summary>CHL2MPMachineGun::DoMachineGunKick's kick, before ViewPunch.</summary>
    private Vector MachineGun(Vector punch, SeededUniformStream random, int seed, float fireDuration)
    {
        float kickPerc = MathF.Min(fireDuration, SlideLimit) / SlideLimit;
        float x = -(MinX + MaxVertical * kickPerc);
        float y = -(MinY + MaxVertical * kickPerc) / 3f;
        float z = MinZ + MaxVertical * kickPerc / 8f;

        // RandomInt( -1, 1 ) >= 0, two in three: wibble left and right, wobble up and down.
        random.SetSeed(ShotSpread.FirstSeed(seed));
        if (random.RandomFloat(0f, 3f) >= 1f)
            y = -y;
        if (random.RandomFloat(0f, 3f) >= 1f)
            z = -z;

        // UTIL_ClipPunchAngleOffset, then "0.5 is just tuned to match the old effect".
        return new Vector(Clip(x, punch.X, ClipX), Clip(y, punch.Y, ClipY), Clip(z, punch.Z, ClipZ)) * 0.5f;
    }

    /// <summary>What of <paramref name="kick"/> keeps the punch inside <paramref name="clip"/>.</summary>
    private static float Clip(float kick, float punch, float clip) => Math.Clamp(kick + punch, -clip, clip) - punch;

    /// <summary>CBasePlayer::ViewPunchReset: to nothing, unless over a non-zero tolerance.</summary>
    private static void Reset(PlayerNetState carrier, float tolerance)
    {
        if (tolerance != 0f)
        {
            Vector a = carrier.PunchAngle.AsVector(), v = carrier.PunchAngleVel.AsVector();
            if (v.Dot(v) + a.Dot(a) > tolerance * tolerance)
                return;
        }
        carrier.PunchAngle = QAngle.FromVector(Vector.Zero);
        carrier.PunchAngleVel = QAngle.FromVector(Vector.Zero);
    }
}
