namespace SourceSharp.Abi;

/// <summary>
/// The engine's SOUND_* bits — what kind of noise CSoundEnt recorded.
///
/// <para>
/// Values are soundent.h's and are written out rather than derived, because the
/// set is NOT a clean bit ladder: the plain kinds occupy the low bits, then the
/// SOUND_CONTEXT_* qualifiers start again twenty places up, and one of stock's
/// own comments admits the values are not consistently treated as bits. Nothing
/// here may be renumbered to tidy that up — the engine is the other end of these.
/// </para>
///
/// <para>
/// Only the bits this mod asks about are named, which is the same choice
/// <see cref="DamageType"/> makes: mirroring all thirty would imply the mod
/// serves scents, thumpers and antlion bugbait, and it serves none of them.
/// </para>
/// </summary>
[Flags]
public enum SoundType
{
    /// <summary>
    /// Matches nothing; a query with this mask comes back empty.
    ///
    /// Not a name C# invented for an empty [Flags] value — soundent.h already
    /// calls the zero SOUND_NONE, and this is that member under the naming this
    /// codebase uses for the rest of the set.
    /// </summary>
    None = 0b00000000_00000000_00000000_00000000,

    /// <summary>A shot, or anything else the game files under fighting.</summary>
    Combat = 0b00000000_00000000_00000000_00000001,

    /// <summary>The world itself making a noise — machinery, a door, a falling crate.</summary>
    World = 0b00000000_00000000_00000000_00000010,

    /// <summary>A player moving or acting loudly enough to be recorded.</summary>
    Player = 0b00000000_00000000_00000000_00000100,

    /// <summary>A round striking a surface, which is recorded apart from the shot that fired it.</summary>
    BulletImpact = 0b00000000_00000000_00000000_00010000,

    /// <summary>
    /// An explosion. A CONTEXT bit rather than a kind of its own, and stock adds
    /// it to <see cref="Combat"/> — which is why a microphone asking for
    /// explosions is not asking for a neighbouring bit but for one twenty places
    /// away.
    /// </summary>
    ContextExplosion = 0b00000010_00000000_00000000_00000000,  // SOUND_CONTEXT_EXPLOSION, soundent.h -- 0x02000000
}

/// <summary>
/// The <c>SOUND_</c> bits <see cref="SoundType"/> deliberately does not name.
///
/// <para>
/// plan_gamedll_after.md Phase A3, and it is a CONSTANT HOLDER rather than more
/// members on the enum above on purpose. <see cref="SoundType"/> is the set a
/// MAP can ask a microphone for and it is curated — six names, each one a
/// choice. These are the bits the LIST ITSELF reasons about now that
/// <c>CSoundEnt</c> is managed, and a bit that only the sweep tests has no
/// business appearing in a map-facing enum's IntelliSense.
/// </para>
///
/// <para>
/// Every value is <c>game/server/soundent.h</c>'s, and that header is still the
/// one owner: the shim passes these numbers straight through, so a divergence
/// would be silent. <c>SoundEntNumbersMatchTheHeader</c> is the fact that keeps
/// them equal.
/// </para>
/// </summary>
public static class SoundFlags
{
    /// <summary><c>SOUND_DANGER</c> — what <c>displaysoundlist 2</c> filters on.</summary>
    public const int Danger = 0x00000008;

    /// <summary><c>ALL_CONTEXTS</c> — the top twelve bits, which are qualifiers.</summary>
    public const int AllContexts = unchecked((int)0xFFF00000);
}

/// <summary>
/// <c>SOUNDENT_CHANNEL_*</c>: a noise's channel, which decides whether an
/// insert ALLOCATES a slot or updates one this owner already holds.
///
/// <para>
/// Only the zero is named, and that is the whole of what managed code needs to
/// know: <see cref="Unspecified"/> means "always a new sound", and every other
/// value means "reuse mine on this channel". The nine named channels are the
/// callers' vocabulary — a weapon, an injury, a footstep — and every one of
/// those callers is native and passes its own number.
/// </para>
/// </summary>
public static class SoundChannel
{
    /// <summary><c>SOUNDENT_CHANNEL_UNSPECIFIED</c>.</summary>
    public const int Unspecified = 0;
}
