using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What <c>color_correction</c> tells the client.
///
/// <para>
/// The lookup FILE travels as a string, which is unusual here and is right:
/// there is no precache table for a colour-correction table, so there is no
/// index for both sides to already agree on. It is sent once and read once —
/// the client loads the lookup on the first update and holds it until the
/// entity goes.
/// </para>
///
/// <para>
/// AND ITS WIDTH SET THE TIER FOR THE WHOLE HIERARCHY UNTIL STEP 13. The
/// filename was declared <c>[NetString(260)]</c> — <c>MAX_PATH</c>, which is an
/// allowance and not a measurement — which made this class 276 bytes, made the
/// hierarchy's shared blob 288 after its round-up to sixteen, and put all 336
/// entities on the parallel hierarchy on the 2048-byte tier and its 518-prop
/// table to carry 32 bytes over the 256 ceiling that nothing used. Every entity
/// there is allocated one blob whatever its class, so the greediest class
/// decides for all of them, and this was it by 112 bytes.
/// </para>
///
/// <para>
/// 128 is <c>func_smokevolume</c>'s width for its material name, and the longest
/// colour-correction path in the 254 shipped maps on the machine this was
/// measured on is 44 characters. Narrowing further would buy nothing: the tier
/// is set by <c>func_smokevolume</c>'s 164 bytes from here on, and reaching the
/// 32-byte tier would need <c>func_breakable_surf</c>'s 68 bytes of pane
/// bitmasks and vectors to fit in 32, which no string change touches. A path
/// that does not fit is REFUSED and names itself — see
/// <c>NetFieldRange.ThrowString</c> — because a truncated path is a lookup that
/// is silently the wrong one.
/// </para>
///
/// <para>
/// The weight is the interesting field. It is the fade the SERVER is running —
/// how far into an <c>Enable</c> or a <c>Disable</c> this correction has got —
/// and the client multiplies it by its own distance falloff. Two weights that
/// mean different things: one is "how much is this correction switched on", the
/// other is "how close is the player to it", and only the first is a decision.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.ColorCorrection)]
public partial class ColorCorrectionNetState
{
    /// <summary>The lookup table to load, relative to the mod directory.</summary>
    [NetString(128)] public partial string LookupFile { get; set; }

    /// <summary>Nearer than this and the correction is fully applied.</summary>
    public partial float MinFalloff { get; set; }

    /// <summary>Further than this and it is not applied at all.</summary>
    public partial float MaxFalloff { get; set; }

    /// <summary>How far through its fade the correction currently is.</summary>
    public partial float CurrentWeight { get; set; }

    /// <summary>Whether it is switched on. Separate from the weight because a fade-out takes time.</summary>
    public partial bool Enabled { get; set; }
}
