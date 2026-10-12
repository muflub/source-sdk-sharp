using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Moves whoever walks in by a fixed offset from the volume's centre, rather
/// than to a named destination.
/// </summary>
[ScriptedEntity(EntityNames.TriggerTeleportRelative, ScriptedEntityKind.Brush)]
public sealed class TriggerTeleportRelative : TriggerVolume
{
    public TriggerTeleportRelative(IGameClock clock)
        : base(clock)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="TriggerTeleportRelativeVars"/>.</summary>
    [ScriptVars] public TriggerTeleportRelativeVars TriggerTeleportRelativeVars = new();

    protected override void Fire(EntityHandle toucher)
    {
        // From the volume's CENTRE, not its origin: a brush entity's origin is
        // usually at the map's world origin and nowhere near the brush itself.
        var destination = Entities.GetWorldSpaceCenter(Handle) + TriggerTeleportRelativeVars.Offset;

        // Facing is kept — this is a displacement, not an arrival somewhere with
        // a direction of its own.
        Entities.Teleport(toucher, destination, angles: null, zeroVelocity: true);

        OnTrigger.Fire();
    }
}

/// <summary>
/// The map keys <see cref="TriggerTeleportRelative"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerTeleportRelativeVars
{
    [VarName("teleportoffset")]
    public Vector Offset { get; set; }
}
