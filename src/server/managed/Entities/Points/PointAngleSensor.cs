//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Reports whether one entity is LOOKING at another.
///
/// The other two sensors in this file measure how fast something moves; this
/// one measures where it points, which is the question a map asks when it wants
/// a camera to notice you or a turret to have lined up.
///
/// <para>
/// The tolerance is the part worth reading twice. A mapper writes an angle in
/// degrees and the comparison is done on a dot product, because a dot is what a
/// facing test actually is — so the key is converted once at spawn rather than
/// a cosine being taken every frame.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointAnglesensor)]
public sealed class PointAngleSensor : ScriptedEntity
{
    public PointAngleSensor(IGameClock clock)
    {
        Clock = clock;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;
    /// <summary>What a map may set on this entity. See <see cref="PointAngleSensorVars"/>.</summary>
    [ScriptVars] public PointAngleSensorVars Vars = new();

    /// <summary>
    /// Compare against the look-at entity's own FACING rather than its
    /// position. Two entities pointing the same way, rather than one pointing
    /// at the other.
    /// </summary>
    private const int UseTargetFacingFlag = 1;

    public readonly Output OnFacingLookat = new("OnFacingLookat");
    public readonly Output OnNotFacingLookat = new("OnNotFacingLookat");
    public readonly Output TargetDir = new("TargetDir");
    public readonly Output FacingPercentage = new("FacingPercentage");

    private EntityHandle _watched = EntityHandle.None;
    private EntityHandle _lookAt = EntityHandle.None;

    /// <summary>
    /// cos(tolerance). Larger is a tighter cone, which reads backwards. Derived
    /// once from the map key in <see cref="OnSpawn"/>, which a hot reload does
    /// not run again -- so it is <c>[SaveField]</c>. Uncarried, the rebuilt
    /// instance read zero and every facing test passed (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _dotTolerance;

    /// <summary>When the facing began. Zero means it is not facing right now.</summary>
    private float _facingSince;

    /// <summary>Latches the output, so holding a stare fires once and not per tick.</summary>
    private bool _fired;

    public override void OnSpawn()
    {
        _dotTolerance = MathF.Cos(Vars.ToleranceDegrees * MathF.PI / 180f);

        // Resolved on the first think, not here: the entities it names may not
        // have spawned yet while the map is still being parsed.
        if (!Vars.Disabled)
            SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (!Resolve())
            return;

        Vector forward = Angles.Forward(Entities.GetAngles(_watched));
        TargetDir.Fire(FormatVector(forward));

        if (Entities.IsValid(_lookAt))
        {
            float dot = Dot(forward);
            Judge(dot >= _dotTolerance, Clock.Now);
            FacingPercentage.Fire(Percentage(dot, _dotTolerance));
        }

        SetNextThink(0f);
    }

    /// <summary>
    /// The latch, given whether it is facing and the time it was asked.
    ///
    /// Split out and taking the clock so a test can drive it: the whole of the
    /// interesting behaviour is the hold, and none of it needs an entity to
    /// point at anything.
    /// </summary>
    internal void Judge(bool facing, float now)
    {
        if (!facing)
        {
            // Losing the facing resets both the latch and the clock. A stare
            // interrupted halfway starts its duration again rather than
            // resuming, which is what "for two seconds" means.
            _fired = false;
            _facingSince = 0f;
            return;
        }

        if (_fired)
            return;

        if (_facingSince == 0f)
            _facingSince = now;

        if (now >= _facingSince + Vars.Duration)
        {
            OnFacingLookat.Fire();
            _fired = true;
        }
    }

    /// <summary>Configures the hold a test drives it with.</summary>
    internal void Configure(float duration) => Vars.Duration = duration;

    /// <summary>
    /// How far into the cone it is: 1 pointing straight at it, 0 at the edge of
    /// tolerance. Clamped, so something behind you does not report a negative.
    /// </summary>
    internal static float Percentage(float dot, float dotTolerance)
    {
        // A tolerance of exactly zero degrees leaves nothing to interpolate
        // across, and dividing by that span would be a NaN networked to
        // everyone reading the output.
        float span = 1f - dotTolerance;

        if (span <= 0f)
            return dot >= 1f ? 1f : 0f;

        return Math.Clamp((dot - dotTolerance) / span, 0f, 1f);
    }

    [ScriptedEvent("Test")]
    private void OnTestInput()
    {
        if (!Resolve())
            return;

        if (Entities.IsValid(_lookAt) && Dot(Angles.Forward(Entities.GetAngles(_watched))) >= _dotTolerance)
            OnFacingLookat.Fire();
        else
            OnNotFacingLookat.Fire();
    }

    [ScriptedEvent("SetTargetEntity")]
    private void OnSetTargetEntity(string name)
    {
        Vars.Target = name;
        _watched = EntityHandle.None;

        if (name.Length == 0)
        {
            StopThinking();
            return;
        }

        if (!Vars.Disabled)
            SetNextThink(0f);
    }

    [ScriptedEvent("Enable")]
    private void OnEnable()
    {
        Vars.Disabled = false;
        SetNextThink(0f);
    }

    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        Vars.Disabled = true;
        StopThinking();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle()
    {
        if (Vars.Disabled)
            OnEnable();
        else
            OnDisable();
    }

    /// <summary>
    /// The dot between where the watched entity points and where it should.
    ///
    /// Two different questions behind one flag: whether it points AT the other
    /// entity, or whether the two point the same WAY.
    /// </summary>
    private float Dot(Vector forward)
    {
        Vector wanted = (SpawnFlags & UseTargetFacingFlag) != 0
            ? Angles.Forward(Entities.GetAngles(_lookAt))
            : (Entities.GetOrigin(_lookAt) - Entities.GetOrigin(_watched)).Normalized;

        return wanted.Dot(forward);
    }

    /// <summary>
    /// Finds what the map named. Answers false while there is nothing to watch,
    /// which is not an error until it has had a chance.
    /// </summary>
    private bool Resolve()
    {
        if (!Entities.IsValid(_watched) && Vars.Target.Length > 0)
        {
            _watched = Entities.FindByName(Vars.Target);

            if (!Entities.IsValid(_watched))
            {
                Log.Warning($"[SourceSharp] point_anglesensor '{Name}': no entity named '{Vars.Target}'\n");
                return false;
            }
        }

        // A sensor with no look-at is legal and useful: it reports the target's
        // facing as a vector and tests nothing. So a missing name is silent, and
        // only a name that resolves to nothing is worth a word.
        if (!Entities.IsValid(_lookAt) && Vars.LookAtName.Length > 0)
        {
            _lookAt = Entities.FindByName(Vars.LookAtName);

            if (!Entities.IsValid(_lookAt))
                Log.Warning($"[SourceSharp] point_anglesensor '{Name}': no look-at entity named '{Vars.LookAtName}'\n");
        }

        return Entities.IsValid(_watched);
    }

    /// <summary>
    /// Three numbers in a string, which is how a map reads a vector back — the
    /// display form Vector.ToString gives has brackets and commas in it and
    /// would arrive at the far end as a zero.
    /// </summary>
    internal static string FormatVector(Vector v)
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{v.X} {v.Y} {v.Z}");
}

/// <summary>
/// The map keys <see cref="PointAngleSensor"/> declares.
/// </summary>
[ScriptVars]
public partial class PointAngleSensorVars
{
    /// <summary>Name of the entity whose facing is measured.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Name of the entity it must be pointed at.</summary>
    public string LookAtName { get; set; } = string.Empty;

    /// <summary>
    /// How far off it may be and still count, in degrees.
    ///
    /// The default is 90 and not 0, which looks wrong and is not. Stock keeps
    /// this as a DOT and leaves it at zero when the map writes no key, and a dot
    /// of zero is a right angle — so a sensor with no tolerance answers true for
    /// anything in the forward half-space. Writing that as 90 degrees says the
    /// same thing in the units the mapper uses.
    /// </summary>
    [VarName("tolerance")]
    public float ToleranceDegrees { get; set; } = 90f;

    /// <summary>Seconds it must hold before OnFacingLookat fires.</summary>
    public float Duration { get; set; }

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
