namespace SourceSharp.Abi;

/// <summary>
/// The one value vphysics must never be given, refused and recorded (native's
/// SourceSharpAcceptPhysicsVector and its ledger, physics_shell.h at
/// bc21e4d46; plan_physics_bones_native_zero 2.5 moved the guard with the
/// player shadow, its only caller).
///
/// <para>
/// A shadow at NaN takes vphysics' whole octree with it: every prop spawned
/// after falls through the world and the level change aborts in IVP. So a
/// non-finite or absurd vector is REFUSED rather than clamped, the shadow
/// stays where it last legitimately was, and the ledger keeps the count and
/// the FIRST refusal (site and value) -- the first is the one not yet
/// contaminated by the ones after it.
/// </para>
///
/// <para>
/// The test is on the float's bits, as native's was: the magnitude of an
/// IEEE-754 single orders as an unsigned integer below the exponent 0xFF, so
/// one compare against the limit's own bits answers "finite" and "in range"
/// with no floating-point comparison for a fast-math build to reason away.
/// The limit is two thousand times a Source map's half-extent.
/// </para>
/// </summary>
public sealed class PhysicsNanGuard
{
    /// <summary>The bound: 3.2768e7, its bits with the sign masked off.</summary>
    public static uint CoordLimitBits => BitConverter.SingleToUInt32Bits(3.2768e7f) & 0x7FFFFFFFu;

    /// <summary>The sites, native's SS_PHYSNAN_* in order; None before any refusal.</summary>
    public const int SiteNone = -1;
    public const int SiteShadowCreate = 0;
    public const int SiteShadowState = 1;
    public const int SiteShadowSnap = 2;
    public const int SiteControllerUpdate = 3;

    /// <summary>How many vectors were refused.</summary>
    public uint Refusals { get; private set; }

    /// <summary>Where the first refusal was (a Site* value), or <see cref="SiteNone"/>.</summary>
    public int FirstSite { get; private set; } = SiteNone;

    /// <summary>What the first refused vector was.</summary>
    public Vector FirstValue { get; private set; }

    /// <summary>Finite and inside the bound, by the bits.</summary>
    public static bool IsFiniteCoord(float f)
        => (BitConverter.SingleToUInt32Bits(f) & 0x7FFFFFFFu) < CoordLimitBits;

    public static bool IsFinite(Vector v) => IsFiniteCoord(v.X) && IsFiniteCoord(v.Y) && IsFiniteCoord(v.Z);

    /// <summary>True when <paramref name="v"/> may be handed to vphysics; false, recorded, when not.</summary>
    public bool Accept(Vector v, int site)
    {
        if (IsFinite(v))
            return true;

        if (FirstSite == SiteNone)
        {
            FirstSite = site;
            FirstValue = v;
        }

        Refusals++;
        return false;
    }
}
