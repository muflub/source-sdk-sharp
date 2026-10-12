//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// How the six flames of a plasma fire are arranged, and how fast each one
/// animates.
///
/// <para>
/// Stock asks the engine's random number generator six times in
/// <c>C_Plasma::Start</c>: a yaw for each flame so the ring is not a hexagon, a
/// starting frame so the six sprites are not the same picture, and a frame rate
/// between fifteen and twenty so they do not pulse together. This answers the
/// same three questions from the entity's index instead.
/// </para>
///
/// <para>
/// WHY NOT THE ENGINE'S RANDOM, which is what the port reached for first.
/// <c>RandomFloat</c> is a SERVER-ONLY ABI slot: on the client the pointer is
/// null and the managed wrapper answers a silent zero. Six flames all given a
/// yaw of zero stack on one axis and a frame rate of zero never animates, so
/// the fire would have drawn as a single motionless sprite — and nothing would
/// have said why. The side audit caught it before it ran; this is the fix rather
/// than a seventh ABI slot.
/// </para>
///
/// <para>
/// DERIVED RATHER THAN DRAWN, and that is better here than merely equivalent. A
/// ring computed from the entity's index is the SAME ring every time that
/// entity is looked at, so a fire that left the view and came back does not
/// silently reshuffle itself — which stock does, because re-entering the PVS
/// runs <c>Start()</c> again. It also needs no state, no allocation and no
/// seeding, and it can be tested.
/// </para>
/// </summary>
public static class PlasmaRing
{
    /// <summary>Stock's <c>NUM_CHILD_FLAMES</c>.</summary>
    public const int FlameCount = 6;

    /// <summary>Stock's frame rate range: fast enough to read as fire.</summary>
    public const float MinFrameRate = 15f;
    public const float MaxFrameRate = 20f;

    /// <summary>
    /// A well-mixed 32 bits from two small integers.
    ///
    /// <para>
    /// SplitMix32's finaliser. It is here because the inputs are TINY and
    /// ADJACENT — an entity index in the low hundreds and a flame index from
    /// zero to five — and a weak mix over inputs like those produces neighbours
    /// that are still neighbours, which for a ring of six means six flames
    /// bunched into one quadrant. Unchecked because the wrapping is the
    /// algorithm.
    /// </para>
    /// </summary>
    private static uint Mix(int entityIndex, int flameIndex)
    {
        unchecked
        {
            uint x = (uint)entityIndex * 0x9E3779B9u + (uint)flameIndex * 0x85EBCA6Bu + 0x165667B1u;

            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;

            return x;
        }
    }

    /// <summary>The mix as a fraction in [0, 1).</summary>
    private static float Unit(int entityIndex, int flameIndex)
        => Mix(entityIndex, flameIndex) / 4294967296f;

    /// <summary>
    /// Which way out from the centre one flame sits, in radians.
    ///
    /// Flat, because stock's is: <c>AngleVectors( 0, yaw, 0 )</c> has a pitch of
    /// zero, so the ring lies around the vertical and every flame points up
    /// whatever the entity's angles say.
    /// </summary>
    public static float Yaw(int entityIndex, int flameIndex)
    {
        // STRATIFIED, where stock draws each yaw uniformly over the whole
        // circle. Each flame owns a sixth of the ring and is placed somewhere
        // inside its own sector.
        //
        // The difference is only visible when stock is unlucky, and it is
        // unlucky more often than one would guess: six independent uniform
        // angles land close together often enough that measuring this build's
        // own hash over four thousand entity indices found rings whose six
        // flames pointed within a few degrees of one direction. Such a fire
        // draws as one sprite, which is the failure this class exists to make
        // impossible. A sector apiece costs nothing and removes the case.
        float sector = MathF.Tau / FlameCount;

        return (flameIndex + Unit(entityIndex, flameIndex)) * sector;
    }

    /// <summary>
    /// Where one flame's animation starts, in frames.
    ///
    /// <para>
    /// Zero when the sprite has no frames to be on, which is the same refusal
    /// <see cref="SpriteFrameCursor"/> makes and for the same reason: the count
    /// comes from a loaded model and can be zero.
    /// </para>
    /// </summary>
    public static float StartFrame(int entityIndex, int flameIndex, int frameCount)
    {
        if (frameCount <= 1)
            return 0f;

        return Unit(entityIndex, flameIndex + 64) * frameCount;
    }

    /// <summary>How fast one flame animates, in frames a second.</summary>
    public static float FrameRate(int entityIndex, int flameIndex)
        => MinFrameRate + Unit(entityIndex, flameIndex + 128) * (MaxFrameRate - MinFrameRate);

    /// <summary>
    /// What fraction of the fire's full size one flame is.
    ///
    /// <para>
    /// Stock's <c>m_entFlameScales</c>, and the one value here that is not
    /// random: the first flame is the full size and the rest taper evenly, which
    /// is what stops the ring reading as six copies of one sprite.
    /// </para>
    /// </summary>
    public static float SizeFraction(int flameIndex)
        => flameIndex == 0 ? 1f : 1f - ((float)flameIndex / FlameCount);
}
