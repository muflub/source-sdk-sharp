//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One pooled temp entity's spawn, as a managed spawn helper writes it. Mirrors
/// <c>SourceSharpTempEntSpawn_t</c>.
///
/// <para>
/// <b>WHY THIS IS NOT AN EXTENSION OF <see cref="TempEntLife"/>,</b> which is
/// the first thing anyone reading the two together will want to do.
/// <see cref="TempEntLife"/> is a VIEW: eight fields, every one read by the
/// expiry decision, read and written back sixty times a second per live
/// tempent. These are written ONCE, at allocation, and most are never read
/// across the boundary again — a sprite's frame rate, a gib's angular velocity,
/// the point a sine wave oscillates about. Growing the life record to carry
/// them would put thirty-eight words across the boundary per tempent per frame
/// to move values nothing asks for twice, and it would stop the life record
/// being a view, which is the one property that makes it safe for the pool to
/// hold no managed copy of it.
/// </para>
///
/// <para>
/// <b>THE OVERLAP IS REAL AND IS NOT A MISTAKE.</b> <see cref="Flags"/>,
/// <see cref="Die"/>, <see cref="RenderAmt"/>, <see cref="RenderMode"/>,
/// <see cref="FadeSpeed"/> and <see cref="ClientIndex"/> appear in both,
/// because they are set at spawn AND read every frame. The alternative was a
/// spawn that wrote the other thirty-two fields and then made a SECOND crossing
/// through <c>TempEntPoolWriteLife</c> for those six — two calls where one
/// does, and two places for a helper to forget one.
/// </para>
///
/// <para>
/// <b>START FROM <see cref="Prepared"/>, ALWAYS.</b> Every field is written
/// unconditionally by the native side, so a helper that built one of these from
/// <c>default</c> would silently hand a tempent <c>bounceFactor = 0</c>,
/// <c>fadeSpeed = 0</c>, a black render colour and <c>clientIndex = 0</c> —
/// which is a real player index. <see cref="Prepared"/> is the managed mirror of
/// <c>C_LocalTempEntity::Prepare</c> plus the constructor's white colour, and
/// <c>TempEntSpawnTests.ThePreparedDefaultsAreStocksPrepare</c> holds it against
/// the real function by reading it out of <c>c_te_legacytempents.cpp</c> off
/// disk — because a stand-in for native drifts silently unless something
/// asserts the agreement.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public partial struct TempEntSpawn
{
    /// <summary>
    /// <c>::flags</c> — the <see cref="TempEntFlags"/> set, WHOLE.
    ///
    /// <para>
    /// Stock's helpers mostly write <c>pTemp-&gt;flags |= FTENT_SINEWAVE</c>,
    /// ORing onto the <c>FTENT_NONE</c> Prepare just wrote. Here the OR happens
    /// in C# against <see cref="Prepared"/>'s zero and the result crosses whole,
    /// which is the same value by a shorter road. Two of them —
    /// <c>BloodSprite</c> and <c>ClientProjectile</c> — ASSIGN rather than OR,
    /// and their managed halves say so where they do it.
    /// </para>
    /// </summary>
    public int Flags;

    /// <summary><c>::die</c> — an absolute <c>curtime</c>, not a duration.</summary>
    public float Die;

    /// <summary><c>::m_flSpriteScale</c>.</summary>
    public float SpriteScale;

    /// <summary><c>::m_flFrame</c> — the frame it starts on.</summary>
    public float Frame;

    /// <summary><c>::m_flFrameMax</c> — nine helpers set this to <c>frameCount - 1</c>.</summary>
    public float FrameMax;

    /// <summary><c>::m_flFrameRate</c>.</summary>
    public float FrameRate;

    /// <summary>
    /// <c>::m_nBody</c>. <c>BreakModel</c> randomises this for a
    /// <c>mod_studio</c> gib and <see cref="Frame"/> for a <c>mod_sprite</c>
    /// one, which is the only place the model type is read.
    /// </summary>
    public int Body;

    /// <summary>
    /// <c>::m_nSkin</c>, which is also half of stock's <c>FindTempEntByID</c>
    /// hack — "we're using skin and hitsounds as a hacky way to store an ID and
    /// sub-ID", stock's own comment.
    /// </summary>
    public int Skin;

    /// <summary><c>::m_nRenderMode</c> — a <c>kRender*</c> value.</summary>
    public int RenderMode;

    /// <summary><c>::m_nRenderFX</c> — a <c>kRenderFx*</c> value.</summary>
    public int RenderFx;

    /// <summary>
    /// <c>::tempent_renderamt</c> — the alpha <c>FTENT_FADEOUT</c> ramps FROM.
    /// Not the current alpha; that is <see cref="ColorA"/>.
    /// </summary>
    public int RenderAmt;

    /// <summary><c>::fadeSpeed</c>.</summary>
    public float FadeSpeed;

    /// <summary><c>::m_clrRender.r</c>.</summary>
    public int ColorR;

    /// <summary><c>::m_clrRender.g</c>.</summary>
    public int ColorG;

    /// <summary><c>::m_clrRender.b</c>.</summary>
    public int ColorB;

    /// <summary><c>::m_clrRender.a</c>.</summary>
    public int ColorA;

    /// <summary><c>SetVelocity</c>.</summary>
    public Vector Velocity;

    /// <summary><c>SetAcceleration</c>.</summary>
    public Vector Acceleration;

    /// <summary>
    /// <c>::m_vecTempEntAngVelocity</c> — how fast a <c>FTENT_ROTATE</c> gib
    /// tumbles, in degrees per second, pitch-yaw-roll.
    /// </summary>
    public Vector AngularVelocity;

    /// <summary><c>SetAbsAngles</c>.</summary>
    public Vector Angles;

    /// <summary><c>::bounceFactor</c>. Prepare's default is ONE, not zero.</summary>
    public float BounceFactor;

    /// <summary>
    /// <c>::x</c> — half of <c>FTENT_SINEWAVE</c>'s axis, and NOT the origin.
    ///
    /// <para>
    /// <c>Frame()</c> oscillates a sine-wave tempent about this point in the XY
    /// plane while it rises, so a bubble spawned at its own origin has this set
    /// to that origin and then wanders around it. Leaving it at zero would send
    /// every bubble in the map circling the world origin.
    /// </para>
    /// </summary>
    public float SineX;

    /// <summary><c>::y</c> — the other half. See <see cref="SineX"/>.</summary>
    public float SineY;

    /// <summary>
    /// <c>::m_vecNormal</c> — the surface a sprite is lying on. Only
    /// <c>TempSprite</c> sets it, and only when its caller passed one.
    /// </summary>
    public Vector Normal;

    /// <summary>
    /// <c>::hitSound</c>. Two unrelated meanings in stock, and both are live:
    /// the other half of the <c>FindTempEntByID</c> hack, and the place
    /// <c>BreakModel</c> stashes its <c>BREAK_*</c> flags so that
    /// <c>PlaySound</c> knows whether a bouncing gib sounds like glass, wood or
    /// concrete.
    /// </summary>
    public int HitSound;

    /// <summary>
    /// <c>::clientIndex</c>. Two meanings, as on <see cref="TempEntLife"/>: a
    /// <c>FTENT_PLYRATTACHMENT</c> tempent's player, or a
    /// <c>FTENT_COLLIDEALL</c> one's ignored entity. Prepare's default is
    /// <b>-1</b>, and zero would be the world.
    /// </summary>
    public int ClientIndex;

    /// <summary>
    /// Join the break-model lighting group: 0 not at all, 1 as the group's
    /// master, 2 as a slave. See <see cref="BreakGroup"/>.
    ///
    /// <para>
    /// NAMED FOR WHAT IT DOES rather than folded into a kind enum with a params
    /// blob. <c>CBreakableHelper</c> is a linked list of break batches so that
    /// fifteen gibs from one crate share a single
    /// <c>ModelRenderInfo_t.pLightingOrigin</c> instead of building fifteen
    /// light caches; its only reader is that pointer, which a managed list
    /// cannot supply. Setting this also installs <c>BreakModelDrawHelper</c>,
    /// because in stock the two are one decision made on two adjacent lines.
    /// </para>
    /// </summary>
    public int BreakHelper;

    /// <summary>
    /// The entity this tempent belongs to, as an INDEX, or <b>-1</b> for none.
    ///
    /// <para>
    /// Stock's <c>SetTempEntOwner</c>, whose handle <c>Frame()</c>'s collision
    /// arm gives its trace filter as the entity to ignore — a projectile must
    /// not immediately hit whoever fired it. An index and never a handle:
    /// reading a client handle back through <c>CBaseHandle::Get()</c> is
    /// behaviour ledger row C8. Native goes the safe way round, index to entity
    /// to handle, through <c>cl_entitylist-&gt;GetEnt</c>.
    /// </para>
    /// </summary>
    public int OwnerEntityIndex;

    /// <summary>
    /// <c>::tentOffset</c>: where above its player an FTENT_PLYRATTACHMENT
    /// tempent rides (AttachTentToPlayer). Zero for every other tempent.
    /// </summary>
    public Vector TentOffset;
}
