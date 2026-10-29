using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A pooled temp entity's WRITE-ONCE state — what
/// <c>C_LocalTempEntity::Frame</c> reads and never writes (behaviour ledger
/// row C4).
///
/// <para>
/// <b>WHY THIS IS A THIRD STRUCT AND NOT A BIGGER TRANSFORM.</b>
/// <see cref="TempEntTransform"/> is what <c>Frame</c> INTEGRATES: it changes
/// every tick and crosses back every frame. These six never change after the
/// tempent is built — they are what the spawn helper decided, and <c>Frame</c>
/// only reads them. Carrying them in the transform would move twenty-four bytes
/// per tempent per frame that cannot have changed, and the transform's own
/// argument for a tight boundary is the argument against doing that.
/// </para>
///
/// <para>
/// <b>AND WHY THEY CANNOT COME FROM <see cref="TempEntSpawn"/> INSTEAD</b>,
/// which is the obvious alternative and does not work. That record is written by
/// a MANAGED spawn helper, and this build still has three NATIVE spawn routes:
/// <c>c_stickybolt.cpp</c> calls <c>SpawnTempModel</c>, the pool's own
/// <c>cs_cl_te_pool_attach</c> calls <c>AttachTentToPlayer</c>, and
/// <c>sdk_fx_weaponfx.cpp</c>'s <c>EjectBrass_9mm</c>/<c>_12Gauge</c> reach
/// <c>CSEjectBrass</c>, which sets <c>bounceFactor</c> and <c>hitSound</c>
/// straight onto the object. A managed cache of what C# itself wrote would
/// simulate all three with <c>Prepare</c>'s defaults — a shell casing with no
/// bounce sound, and a player attachment at the player's feet instead of forty
/// units above them.
/// </para>
///
/// <para>
/// So this is asked of the OBJECT, which is the one place all four spawn routes
/// agree. Sequential and blittable, mirroring
/// <c>SourceSharpTempEntStatics_t</c> field for field.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TempEntStatics
{
    /// <summary>
    /// <c>::bounceFactor</c> — how much speed survives a bounce.
    /// <c>Prepare</c>'s default is ONE, not zero, and a zero here is a gib that
    /// sticks where it lands.
    /// </summary>
    public float BounceFactor;

    /// <summary>
    /// <c>::hitSound</c> — a <c>BOUNCE_*</c> sound kind, zero for silence.
    /// <c>Frame</c>'s own gate is <c>if ( hitSound )</c> before it calls
    /// <c>PlaySound</c>.
    /// </summary>
    public int HitSound;

    /// <summary>
    /// <c>::m_flFrameMax</c> — what <c>FTENT_SPRANIMATE</c> and
    /// <c>FTENT_SPRCYCLE</c> advance the frame against. Zero here is an
    /// animating sprite that runs off its last frame on the first tick.
    /// </summary>
    public float FrameMax;

    /// <summary><c>::m_flFrameRate</c>.</summary>
    public float FrameRate;

    /// <summary>
    /// <c>::tentOffset</c> — where a <c>FTENT_PLYRATTACHMENT</c> tempent sits
    /// relative to the player it is stuck to. <c>AttachTentToPlayer</c>'s
    /// <c>zoffset</c> lands in Z.
    /// </summary>
    public Vector TentOffset;

    /// <summary>
    /// <c>::m_hOwnerEntity</c> as an INDEX — the entity the collision arm's
    /// <see cref="TraceFilter.Simple"/> passes through, so a projectile does not
    /// immediately hit whoever fired it. -1 for none.
    ///
    /// <para>
    /// Taken from the handle's entry index, which is bit extraction and not a
    /// lookup: dereferencing a client handle through <c>CBaseHandle::Get()</c>
    /// is behaviour ledger row C8.
    /// </para>
    /// </summary>
    public int OwnerEntityIndex;

    /// <summary>
    /// <c>::m_nFlickerFrame</c> — the 0..31 frame a <c>FTENT_FLICKER</c>
    /// tempent lights its dlight on.
    ///
    /// <para>
    /// READ RATHER THAN ASSUMED TO BE ZERO, which it always is in this build:
    /// <c>Prepare</c> sets it and nothing else ever writes it. That is exactly
    /// why it crosses — a managed constant zero would be right today and would
    /// be a silent wrong value the moment a helper set one, and it costs four
    /// bytes of a struct that is read once per tempent.
    /// </para>
    /// </summary>
    public int FlickerFrame;

    /// <summary>
    /// Whether <c>::m_pszImpactEffect</c> is set — and NOT the name itself.
    /// Behaviour ledger row C5.
    ///
    /// <para>
    /// The row's guarded code is stock's <c>if ( m_pszImpactEffect )</c> block,
    /// which dispatches an effect carrying the TEAM NUMBER of the client entity
    /// the tempent belongs to — a number this build cannot answer, because no
    /// client entity is a <c>C_BasePlayer</c> to <c>dynamic_cast</c> to. The
    /// branch is also UNREACHABLE here: <c>m_pszImpactEffect</c> is only ever
    /// set by <c>CTempEnts::ClientProjectile</c>'s optional
    /// <c>pszImpactEffect</c> parameter, and both live callers leave it at its
    /// NULL default.
    /// </para>
    ///
    /// <para>
    /// So the NAME does not cross and no managed dispatch is written: that
    /// would be fabricating a capability for a branch nothing takes. What
    /// crosses is the one bit that keeps the row's call site REAL — if anything
    /// ever does set an impact effect, the managed arm says so once per level
    /// and names the row, instead of the effect silently not happening.
    /// </para>
    /// </summary>
    public int HasImpactEffect;
}
