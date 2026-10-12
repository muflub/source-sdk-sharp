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
/// Holds something the right way up — a barrel that rolls back upright, a crate
/// that will not tip.
///
/// The map orients the entity and its UP axis becomes the goal, which is how a
/// mapper aims it at something other than vertical without a key for it.
/// </summary>
[ScriptedEntity(EntityNames.PhysKeepupright)]
public sealed class PhysKeepUpright : PhysicsForceEntity
{
    public PhysKeepUpright(IEngineMotionControllers motion)
        : base(motion)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="PhysKeepUprightVars"/>.</summary>
    [ScriptVars] public PhysKeepUprightVars PhysKeepUprightVars = new();

    private const int StartInactiveFlag = 1;

    /// <summary>
    /// Set once from the start-inactive spawnflag in <see cref="OnSpawn"/>,
    /// which a hot reload does not run again -- so it is <c>[SaveField]</c>.
    /// Uncarried, the rebuilt instance read false and a running keeper went
    /// slack after a <c>cs_sv_reload</c> (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private bool _active;

    protected override MotionControllerKind Kind => MotionControllerKind.KeepUpright;

    public override void OnSpawn()
    {
        _active = (SpawnFlags & StartInactiveFlag) == 0;
        base.OnSpawn();
    }

    protected override void OnAttached() => Apply();

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn()
    {
        _active = true;
        Apply();
    }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff()
    {
        _active = false;
        Apply();
    }

    [ScriptedEvent("SetAngularLimit")]
    private void OnSetAngularLimit(float limit)
    {
        PhysKeepUprightVars.AngularLimit = limit;
        Apply();
    }

    /// <summary>
    /// The goal is this entity's own up axis, so a mapper aims it by turning it.
    /// Damping all rotation is stock's second mode and is not reachable from a
    /// map key — it exists for code that creates one directly — so it is left
    /// off here rather than invented.
    /// </summary>
    private void Apply()
        => Controller?.SetUpright(Angles.Vectors(SpawnAngles).Up, PhysKeepUprightVars.AngularLimit,
                                  dampAllRotation: false, active: _active);
}

/// <summary>
/// The map keys <see cref="PhysKeepUpright"/> declares.
/// </summary>
[ScriptVars]
public partial class PhysKeepUprightVars
{
    /// <summary>
    /// Caps how fast the righting happens. Without a limit a barrel knocked
    /// over snaps upright, which reads as a bug rather than as a barrel.
    /// </summary>
    public float AngularLimit { get; set; } = 15f;
}
