using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// vphysics' <c>surfacedata_t</c> (public/vphysics_interface.h), the fields the
/// physics sounds read, at their offsets: <c>surfaceaudioparams_t</c> at 20 and
/// <c>surfacesoundnames_t</c> at 44. Read in place through
/// <c>IPhysicsSurfaceProps::GetSurfaceData</c>; never written.
/// vtable_index_agreement_test's SurfaceDataLayout holds the offsets to the
/// header and VPhysicsSurfaceDataLayoutTests holds them here.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = SizeInBytes)]
public struct VPhysicsSurfaceData
{
    /// <summary><c>sizeof( surfacedata_t )</c>.</summary>
    public const int SizeInBytes = 96;

    /// <summary><c>audio.hardnessFactor</c>: how hard this surface is, for the other side's choice.</summary>
    [FieldOffset(24)] public float HardnessFactor;

    /// <summary><c>audio.roughnessFactor</c>: how rough this surface sounds to a scrape against it.</summary>
    [FieldOffset(28)] public float RoughnessFactor;

    /// <summary><c>audio.roughThreshold</c>: a hit surface rougher than this scrapes rough.</summary>
    [FieldOffset(32)] public float RoughThreshold;

    /// <summary><c>audio.hardThreshold</c>: a softer other surface plays this one's soft impact.</summary>
    [FieldOffset(36)] public float HardThreshold;

    /// <summary><c>audio.hardVelocityThreshold</c>: a slower hit plays the soft impact.</summary>
    [FieldOffset(40)] public float HardVelocityThreshold;

    /// <summary><c>sounds.impactSoft</c>: a string index, 0 for none.</summary>
    [FieldOffset(48)] public ushort ImpactSoft;

    /// <summary><c>sounds.impactHard</c>: a string index, 0 for none.</summary>
    [FieldOffset(50)] public ushort ImpactHard;

    /// <summary><c>sounds.scrapeSmooth</c>: a string index, 0 for none.</summary>
    [FieldOffset(52)] public ushort ScrapeSmooth;

    /// <summary><c>sounds.scrapeRough</c>: a string index, 0 for none.</summary>
    [FieldOffset(54)] public ushort ScrapeRough;

    /// <summary><c>sounds.bulletImpact</c>: a string index, 0 for none (a bullet impact's sound).</summary>
    [FieldOffset(56)] public ushort BulletImpact;

    /// <summary><c>sounds.breakSound</c>: a string index, 0 for none.</summary>
    [FieldOffset(60)] public ushort BreakSound;

    /// <summary><c>game.maxSpeedFactor</c>: the movement loop's speed scale on this surface.</summary>
    [FieldOffset(64)] public float MaxSpeedFactor;

    /// <summary><c>game.jumpFactor</c>: the movement loop's jump scale on this surface.</summary>
    [FieldOffset(68)] public float JumpFactor;

    /// <summary>
    /// <c>game.material</c>, the game material (CHAR_TEX_*) a surfaceprop is made
    /// of: what SurfacePropGameMaterial answered before it folded (2026-10-19).
    /// </summary>
    [FieldOffset(72)] public ushort GameMaterial;

    /// <summary><c>game.climbable</c>: nonzero for a ladder surface.</summary>
    [FieldOffset(74)] public byte Climbable;
}

/// <summary>
/// One surface's impact and break sounds, by soundscript name (null for none),
/// and the three audio thresholds the soft-or-hard choice reads.
/// </summary>
public readonly record struct SurfaceImpactSounds(string? ImpactHard, string? ImpactSoft, string? BreakSound,
                                                  float HardnessFactor, float HardThreshold,
                                                  float HardVelocityThreshold);

/// <summary>
/// <c>physprops-&gt;GetSurfaceData( index )</c>'s sounds, read through vphysics'
/// own table (plan_physics_bones_native_zero 2.3: the queued physics sounds
/// left physics.cpp).
/// </summary>
public interface IVPhysicsSurfaceSounds
{
    /// <summary>The surface's sounds, or null when vphysics answers no surface.</summary>
    SurfaceImpactSounds? Sounds(int surfaceProps);
}
