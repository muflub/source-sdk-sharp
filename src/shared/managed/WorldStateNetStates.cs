using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What <c>shadow_control</c> tells the client.
///
/// <para>
/// The whole entity is this: a direction for the sun's shadows, what colour
/// they are, how far away they are still drawn, and whether they are drawn at
/// all. There is no behaviour underneath it — the client shadow manager reads
/// these four things and every shadow in the map follows.
/// </para>
///
/// <para>
/// The direction is a VECTOR on the wire and ANGLES in the map, and the
/// conversion happens on the server where the keyvalue arrives. That is stock's
/// split too, and it is the useful one: a mapper points a compass, the renderer
/// wants a forward vector, and converting once on the way in beats converting
/// per client per change.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.ShadowControl)]
public partial class ShadowControlNetState
{
    /// <summary>Which way the light comes FROM, as a forward vector.</summary>
    public partial Vector Direction { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    /// <summary>How far from the caster a shadow is still drawn.</summary>
    public partial float MaxDistance { get; set; }

    /// <summary>Whether to draw shadows at all. A map can turn the lot off.</summary>
    public partial bool Disabled { get; set; }
}

/// <summary>
/// What <c>water_lod_control</c> tells the client: the two distances at which
/// water stops being expensive.
///
/// <para>
/// Both are read by the view renderer every frame, and neither is a decision
/// anything makes at runtime — a map states them once and inputs may move them.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.WaterLodControl)]
public partial class WaterLodControlNetState
{
    /// <summary>Where water starts being drawn the cheap way.</summary>
    public partial float CheapStart { get; set; }

    /// <summary>Where it is entirely the cheap way.</summary>
    public partial float CheapEnd { get; set; }
}

/// <summary>
/// What <c>env_tonemap_controller</c> tells the client.
///
/// <para>
/// Three of these are switches and four are numbers, and they pair up: a map
/// may clamp the auto-exposure at either end, or scale the bloom, or any
/// combination, and each clamp is only applied when its switch says so. That is
/// why the switches are networked rather than inferred from the numbers — zero
/// is a legitimate exposure floor, so "unset" cannot be spelt as zero.
/// </para>
///
/// <para>
/// <c>BloomScaleMinimum</c> has no switch because it is not one: it is a floor
/// under whatever bloom scale is in force, and stock reads it unconditionally.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvTonemapController)]
public partial class TonemapControllerNetState
{
    public partial bool UseCustomAutoExposureMin { get; set; }
    public partial bool UseCustomAutoExposureMax { get; set; }
    public partial bool UseCustomBloomScale { get; set; }

    public partial float CustomAutoExposureMin { get; set; }
    public partial float CustomAutoExposureMax { get; set; }
    public partial float CustomBloomScale { get; set; }
    public partial float CustomBloomScaleMinimum { get; set; }
}

/// <summary>
/// What <c>env_detail_controller</c> tells the client.
///
/// <para>
/// Two distances, and the whole entity is them: where detail props start to
/// fade and where they are gone. Stock networks the same two floats and its
/// client detail system clamps its own convar defaults down to them — a
/// controller can only bring the fade CLOSER, never push it further out, and
/// the managed client half keeps that rule where it was.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvDetailController)]
public partial class DetailControllerNetState
{
    /// <summary>Where detail props start to fade.</summary>
    public partial float FadeStartDist { get; set; }

    /// <summary>Where they are gone entirely.</summary>
    public partial float FadeEndDist { get; set; }
}
