namespace SourceSharp.Abi;

/// <summary>
/// One thing that recently made a noise, as CSoundEnt recorded it. Mirrors
/// SourceSharpHeardSound.
///
/// Not a sound anybody is hearing: it is the world's note that something loud
/// happened at a place, which is what env_microphone measures. There is no
/// sound name because a CSound has never carried one.
/// </summary>
public struct HeardSound
{
    public Vector Origin;

    /// <summary>What kind of noise it was, context bits included.</summary>
    public SoundType Type;

    /// <summary>CSoundEnt's own units — roughly how far it carries, not a gain.</summary>
    public int Volume;

    /// <summary>What made it, or an invalid handle.</summary>
    public EntityHandle Owner;
}
