namespace SourceSharp.Abi;

/// <summary>
/// Source's surfaceprops "game material": one character per broad class of
/// stuff, used for footstep sounds, impact decals and — here — whether a magnet
/// will hold on to something.
///
/// <para>
/// A closed value set rather than a bit field: a surface is made of one thing,
/// and the values are ASCII letters, so ORing two of them gives a third letter
/// rather than "both". That is why this is a plain enum and must never gain
/// [Flags] — 'M' | 'W' is 'W'.
/// </para>
///
/// <para>
/// Only the ones this mod actually names are here, rather than mirroring the
/// whole set and implying support for the rest.
/// </para>
/// </summary>
public enum SurfaceMaterial
{
    /// <summary>Nothing known about the surface. Not a letter, and not silence.</summary>
    Unknown  = 0,

    /// <summary>
    /// Not a material at all: stock's explicit "make no sound", which
    /// env_shooter writes into the same field a letter goes in.
    ///
    /// Named here because the field carries it, and a type that cannot express
    /// every value its field holds is not describing that field. It is the one
    /// member that is not a kind of stuff, which is why it says so.
    /// </summary>
    Silent   = -1,

    Metal    = 'M',
    Computer = 'P',
    Vent     = 'V',
    Grate    = 'G',
    Wood     = 'W',
    Glass    = 'Y',

    /// <summary>Named for env_shooter's dropdown, which offers it as a gib kind.</summary>
    Flesh    = 'F',

    /// <summary>Likewise.</summary>
    Rocks    = 'R',
}

/// <summary>
/// Everything a shooter says about ONE piece of debris.
///
/// <para>
/// Managed data since plan_server_native_v2 T2 (SourceSharp_ShootGib and its native SourceSharpGibShot went). A struct rather than a dozen
/// arguments because the fields are only meaningful together, and these are
/// filled in a loop where an argument out of order would be a silent mis-shot.
/// </para>
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct GibShot
{
    // Vector rather than a fixed float[3]: it is three sequential floats either
    // way, and using it keeps this struct — and every caller — out of unsafe.
    // Only the ABI layer is allowed unsafe here, and a shooter is game code.
    public Vector Origin;
    public Vector Angles;

    /// <summary>Which way. The scatter is applied before this is filled.</summary>
    public Vector Direction;

    public float Speed;
    public int   Simulation;
    public float Lifetime;
    public float AngularVelocity;

    public int   Skin;
    public float Scale;
    public float GravityScale;

    /// <summary>Source's matGlass/matWood/... as an int. -1 is silent.</summary>
    public int   Material;

    public int   StrictRemove;

    /// <summary>
    /// What the gib bleeds when it bounces, as <see cref="Abi.BloodColor"/>.
    ///
    /// <para>
    /// Sent rather than defaulted because the two shooters disagree and the
    /// default is neither answer. <c>game/server/effects.cpp</c> sets it in both
    /// factories and sets it OPPOSITE ways —
    /// <c>CGibShooter::CreateGib</c> says <c>BLOOD_COLOR_RED</c> and
    /// <c>CEnvShooter::CreateGib</c> says <c>DONT_BLEED</c>. Nothing in the shim
    /// said either, and a zeroed <c>CGib::m_bloodColor</c> reads as 0, which is
    /// <c>BLOOD_COLOR_RED</c>: gibshooter was accidentally right and env_shooter
    /// bled when stock says it must not.
    /// </para>
    /// </summary>
    public int   BloodColor;
}
