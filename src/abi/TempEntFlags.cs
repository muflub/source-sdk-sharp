namespace SourceSharp.Abi;

/// <summary>
/// <c>tempent.h</c>'s <c>FTENT_*</c> bits, which are what a pooled temp entity
/// IS — there is no type, only a flag word and a model.
///
/// <para>
/// THE WHOLE SET IS HERE, not just the ones the managed pool reads, and that is
/// deliberate: the values are wire-visible in the sense that native
/// <c>Frame()</c> and the seventeen native spawn helpers all set and test them,
/// so a managed half-list would be a second, quieter definition of the same
/// word. A name declared and unused costs nothing; a bit missing from this
/// enum is a flag C# would have to spell as a literal.
/// </para>
///
/// <para>
/// THE NAMES ARE STOCK'S WORDS, ABBREVIATIONS AND ALL — <c>SprAnimate</c> for
/// <c>FTENT_SPRANIMATE</c> and <c>PlyrAttachment</c> for
/// <c>FTENT_PLYRATTACHMENT</c>, rather than the <c>SpriteAnimate</c> and
/// <c>PlayerAttachment</c> that read better. That is what lets
/// <c>TempEntPoolTests.TheFlagBitsAreTempentHs</c> pair the two sets
/// MECHANICALLY, by name, and so notice a flag added to <c>tempent.h</c> that
/// never arrived here. An alias table would be a place for the pairing to be
/// wrong quietly, which is the whole thing that test exists to prevent.
/// </para>
/// </summary>
[Flags]
public enum TempEntFlags
{
    None = 0x00000000,
    SineWave = 0x00000001,
    Gravity = 0x00000002,
    Rotate = 0x00000004,
    SlowGravity = 0x00000008,
    SmokeTrail = 0x00000010,
    CollideWorld = 0x00000020,
    Flicker = 0x00000040,

    /// <summary>
    /// Fade out instead of vanishing when <c>die</c> passes. The managed pool's
    /// expiry reads this: it is the difference between "gone" and "start
    /// ramping the alpha down from <c>RenderAmt</c> at <c>FadeSpeed</c>".
    /// </summary>
    FadeOut = 0x00000080,

    SprAnimate = 0x00000100,
    HitSound = 0x00000200,
    Spiral = 0x00000400,
    SprCycle = 0x00000800,

    /// <summary>Collides with the world AND with slideboxes.</summary>
    CollideAll = 0x00001000,

    /// <summary>
    /// Not removed when it cannot be drawn. The managed pool's visibility pass
    /// reads this: without it, a tempent the leaf system refuses is dumped on
    /// the spot rather than kept for a frame it might be visible in.
    /// </summary>
    Persist = 0x00002000,

    CollideKill = 0x00004000,

    /// <summary>
    /// Attached to a player, whose index is <see cref="TempEntLife.ClientIndex"/>.
    /// The only bit <c>KillAttachedTents</c> looks for, and the only thing that
    /// sets it is <c>CTempEnts::AttachTentToPlayer</c>.
    /// </summary>
    PlyrAttachment = 0x00008000,

    SprAnimateLoop = 0x00010000,
    SmokeGrowAndFade = 0x00020000,
    AttachToTarget = 0x00040000,
    NoModel = 0x00080000,
    ClientCustom = 0x00100000,
    WindBlown = 0x00200000,

    /// <summary>
    /// Lives until <c>die</c> is CLEARED rather than until it passes. Stock's
    /// <c>IsActive</c> answers <c>die != 0</c> for one of these and ignores the
    /// ordinary expiry entirely.
    /// </summary>
    NeverDie = 0x00400000,

    BeOccluded = 0x00800000,
    ChangeRenderOnCollide = 0x01000000,
    CollisionGroup = 0x02000000,
    AlignToMotion = 0x04000000,
    ClientSideParticles = 0x08000000,
    UseFastCollisions = 0x10000000,
    CollideProps = 0x20000000,
}

/// <summary>
/// <c>tempent.h</c>'s <c>TENTPRIORITY_*</c>. Two values, and the whole
/// difference between them is what happens when the pool is full: a LOW
/// allocation fails, and a HIGH one gives up the first LOW tempent in the list.
/// </summary>
public static class TempEntPriority
{
    /// <summary><c>TENTPRIORITY_LOW</c> — everything except a blood sprite and a player attachment.</summary>
    public const int Low = 0;

    /// <summary><c>TENTPRIORITY_HIGH</c> — <c>BloodSprite</c> and <c>AttachTentToPlayer</c>.</summary>
    public const int High = 1;
}
