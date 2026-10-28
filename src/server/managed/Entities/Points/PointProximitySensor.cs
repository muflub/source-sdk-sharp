using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Reports how far away something is, as a number a map can compare.
///
/// The pair to <see cref="PointAngleSensor"/>: that one answers where a thing
/// points, this one answers how close it has got.
/// </summary>
[ScriptedEntity(EntityNames.PointProximitySensor)]
public sealed class PointProximitySensor : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="PointProximitySensorVars"/>.</summary>
    [ScriptVars] public PointProximitySensorVars Vars = new();

    /// <summary>
    /// Measure only the distance ALONG this entity's facing, rather than the
    /// straight-line distance. How a map asks "how far down the corridor is it"
    /// and ignores how far to the side.
    /// </summary>
    private const int TestAgainstAxisFlag = 1;

    public readonly Output Distance = new();

    private EntityHandle _watched = EntityHandle.None;

    public override void OnSpawn()
    {
        if (!Vars.Disabled)
            SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (!Entities.IsValid(_watched))
        {
            _watched = Entities.FindByName(Vars.Target);

            if (!Entities.IsValid(_watched))
            {
                Log.Warning($"[SourceSharp] point_proximity_sensor '{Name}': no entity named '{Vars.Target}'\n");
                return;
            }
        }

        Vector offset = Entities.GetOrigin(_watched) - Origin;

        Distance.Fire((SpawnFlags & TestAgainstAxisFlag) != 0
            ? AlongAxis(offset, Angles.Forward(Entities.GetAngles(Handle)))
            : offset.Length);

        SetNextThink(0f);
    }

    /// <summary>
    /// The distance projected onto an axis, unsigned.
    ///
    /// Unsigned because the output is a DISTANCE: something the same way behind
    /// this entity is the same far away, and a negative would compare wrongly
    /// against every threshold a map sets.
    /// </summary>
    internal static float AlongAxis(Vector offset, Vector axis)
        => MathF.Abs(offset.Dot(axis));

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
}

/// <summary>
/// The map keys <see cref="PointProximitySensor"/> declares.
/// </summary>
[ScriptVars]
public partial class PointProximitySensorVars
{
    public string Target { get; set; } = string.Empty;

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
