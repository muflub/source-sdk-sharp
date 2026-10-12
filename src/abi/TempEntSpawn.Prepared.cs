//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

public partial struct TempEntSpawn
{
    /// <summary>
    /// The record a tempent is in the instant <c>TempEntPoolCreate</c> returns:
    /// stock's <c>C_LocalTempEntity::Prepare</c>, plus the two things its
    /// constructor establishes that a zeroed block gets wrong.
    ///
    /// <para>
    /// EVERY SPAWN HELPER STARTS HERE and overwrites what it cares about, which
    /// is exactly what stock's helpers do — they run after Prepare and touch
    /// twelve fields out of thirty. The four values a <c>default</c> struct
    /// would get wrong are the ones worth naming: <see cref="BounceFactor"/> is
    /// 1 (a gib with 0 does not bounce at all), <see cref="FadeSpeed"/> is 0.5
    /// (with 0 a <c>FTENT_FADEOUT</c> tempent never finishes fading and lives
    /// until the level ends), <see cref="ClientIndex"/> is -1 (0 is a real
    /// entity index — the world), and the colour is white (a black tempent is
    /// invisible against most of a map and looks like the effect not firing).
    /// </para>
    ///
    /// <para>
    /// <paramref name="now"/> because Prepare's own default lifetime is
    /// <c>time + 0.75</c> — a relative value, and the only one here. Helpers
    /// that set their own <see cref="Die"/> overwrite it; <c>ClientProjectile</c>
    /// and <c>TempSprite</c>'s no-life arm both depend on it being right.
    /// </para>
    /// </summary>
    public static TempEntSpawn Prepared(float now) => new()
    {
        // Prepare: flags = FTENT_NONE
        Flags = (int)TempEntFlags.None,

        // Prepare: die = time + 0.75
        Die = now + 0.75f,

        // Prepare: SetRenderMode( kRenderNormal ), m_nRenderFX = kRenderFxNone
        RenderMode = (int)Abi.RenderMode.Normal,
        RenderFx = 0,

        // Prepare: m_nBody = 0, m_nSkin = 0, hitSound = 0
        Body = 0,
        Skin = 0,
        HitSound = 0,

        // Prepare: fadeSpeed = 0.5, bounceFactor = 1, clientIndex = -1
        FadeSpeed = 0.5f,
        BounceFactor = 1f,
        ClientIndex = -1,

        // Prepare: SetAbsAngles( vec3_angle ). The origin is the create call's
        // argument, so it is not here.
        Angles = default,

        // NOT Prepare's, and this is the half a reader will look for: the
        // constructor's. CClassMemoryPool::AllocZero zeroes the block and then
        // runs C_LocalTempEntity::C_LocalTempEntity, which sets m_clrRender to
        // white because zero is wrong for a colour. Prepare never touches it,
        // so a helper that does not set a colour inherits white — and eleven of
        // the seventeen do exactly that.
        ColorR = 255,
        ColorG = 255,
        ColorB = 255,
        ColorA = 255,

        // Zeroed by AllocZero and left alone by both. Spelled out rather than
        // left to `default` so that this factory reads as the whole state of a
        // freshly prepared tempent, which is what every helper diffs against.
        SpriteScale = 0f,
        Frame = 0f,
        FrameMax = 0f,
        FrameRate = 0f,
        RenderAmt = 0,
        Velocity = default,
        Acceleration = default,
        AngularVelocity = default,
        SineX = 0f,
        SineY = 0f,
        Normal = default,

        // Neither is a field of the tempent; both mean "do not".
        BreakHelper = BreakGroup.None,
        OwnerEntityIndex = -1,
    };
}
