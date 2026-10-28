using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One pooled temp entity's lifetime. Mirrors <c>SourceSharpTempEntLife_t</c>.
///
/// <para>
/// <c>c_te_legacytempents.cpp</c>'s <c>CTempEnts</c> is a pool of 500
/// <c>C_LocalTempEntity</c> — gibs, shell casings, sprites and debris.
/// plan_tmp.md Part 5 moves the POOL to C#: the allocation, the free list, the
/// priority classes, the expiry and the update loop. What stays native is the
/// OBJECT, because a <c>C_LocalTempEntity</c> is already the draw shell —
/// de-entified onto <c>CShellRenderable</c>.
/// </para>
///
/// <para>
/// <b>SO THIS IS A VIEW, NOT A SECOND COPY OF THE STATE.</b> It is filled from
/// the live object on every read and written straight back into it on every
/// write; nothing here is stored between frames. The managed pool holds the
/// LIST and the POLICY, never the fields — native <c>Frame()</c> mutates
/// <see cref="Die"/> and <see cref="Flags"/> every tick (a gib that lands
/// clears <c>FTENT_ROTATE</c>; a <c>FTENT_COLLIDEKILL</c> tempent sets
/// <c>die = curtime</c>), and a mirror would go stale within one frame of a
/// bounce.
/// </para>
///
/// <para>
/// EIGHT FIELDS, AND EACH IS READ BY THE EXPIRY DECISION.
/// <c>C_LocalTempEntity</c> has thirty-odd members; these are the ones
/// <c>IsActive</c>, <c>FreeLowPriorityTempEnt</c> and <c>KillAttachedTents</c>
/// touch. Anything a managed spawn helper needs later is a separate slot named
/// for what it sets, never an extension of this one — the moment this struct
/// grows a field nothing here reads, it has stopped being a view.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TempEntLife
{
    /// <summary>
    /// <c>C_LocalTempEntity::die</c> — the <c>curtime</c> this tempent expires
    /// at. Zero has a meaning of its own under <c>FTENT_NEVERDIE</c>: stock's
    /// <c>IsActive</c> reads <c>active = (die != 0)</c> there, so a never-die
    /// tempent is killed by CLEARING this rather than by setting it.
    /// </summary>
    public float Die;

    /// <summary><c>::flags</c> — the <see cref="TempEntFlags"/> set.</summary>
    public int Flags;

    /// <summary><c>::fadeSpeed</c> — how fast the <c>FTENT_FADEOUT</c> ramp runs.</summary>
    public float FadeSpeed;

    /// <summary>
    /// <c>::tempent_renderamt</c> — the alpha the fade ramp is a fraction OF.
    /// Not the current alpha; that is <see cref="Alpha"/>.
    /// </summary>
    public int RenderAmt;

    /// <summary><c>::priority</c> — <see cref="TempEntPriority"/>.</summary>
    public int Priority;

    /// <summary>
    /// <c>::clientIndex</c>. Two meanings, and stock's own comment says so: for
    /// a <c>FTENT_PLYRATTACHMENT</c> tempent it is the player it is stuck to,
    /// and for a <c>FTENT_COLLIDEALL</c> one it is the entity the collision
    /// ignores.
    /// </summary>
    public int ClientIndex;

    /// <summary>
    /// <c>::m_nRenderMode</c> — a <c>kRender*</c> value. The fade ramp flips
    /// <c>kRenderNormal</c> to <c>kRenderTransTexture</c> the moment it starts,
    /// because a normal-mode renderable draws opaque whatever its alpha says.
    /// </summary>
    public int RenderMode;

    /// <summary><c>::m_clrRender.a</c> — what the fade ramp writes.</summary>
    public int Alpha;
}
