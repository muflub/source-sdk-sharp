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
/// A door made of a model rather than of brushes. The same states as
/// func_door_rotating and the same reversing-when-blocked rule; what differs is
/// only what it is made of, which the managed side never sees.
///
/// It is a prop, so it is solid through its model's own collision rather than
/// through a bounding box — but it is moved by the door logic, not by physics,
/// so it is static as far as the simulation is concerned.
/// </summary>
[ScriptedEntity(EntityNames.PropDoorRotating, ScriptedEntityKind.Prop)]
public sealed class PropDoorRotating : AnimatingEntity
{
    /// <summary>SF_DOOR_IGNORE_USE: the door ignores a player's +USE altogether.</summary>
    private const int IgnoreUseFlag = 32768;

    /// <summary>CBasePropDoor::ObjectCaps: usable, and found in the radius, unless it ignores use.</summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | ((SpawnFlags & IgnoreUseFlag) == 0 ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None);

    public readonly Output OnOpen = new();
    public readonly Output OnClose = new();
    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();
    public readonly Output OnBlockedClosing = new();
    public readonly Output OnBlockedOpening = new();

    /// <summary>What a map may set on this entity. See <see cref="PropDoorRotatingVars"/>.</summary>
    [ScriptVars] public PropDoorRotatingVars Vars = new();

    private enum DoorState { Closed, Opening, Open, Closing }

    private DoorState _state = DoorState.Closed;

    private Vector _closedAngles;

    /// <summary>
    /// The facing it turns to when open, derived from the map's keys ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> and every open after a <c>cs_sv_reload</c> slammed the
    /// door flat (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _openAngles;

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);

        // CBasePropDoor::Spawn: SOLID_VPHYSICS and a shadow, moved by this
        // logic rather than by the simulation, which props meet as it swings.
        SetSolidity(SolidType.VPhysics, SolidFlags.None);
        InitShadowPhysics();

        _closedAngles = SpawnAngles;
        _openAngles = new Vector(_closedAngles.X, _closedAngles.Y + Vars.Distance, _closedAngles.Z);

        if (Vars.Speed <= 0f)
            Vars.Speed = 100f;

        // A door the mapper drew open starts open, and the angles it was drawn
        // at are the OPEN ones — so closed has to be worked out backwards.
        if (Vars.SpawnPosition != 0)
        {
            _openAngles = _closedAngles;
            _closedAngles = new Vector(_openAngles.X, _openAngles.Y - Vars.Distance, _openAngles.Z);
            _state = DoorState.Open;
        }
    }

    [ScriptedEvent("Open")] private void OnOpenInput() => Open();
    [ScriptedEvent("Close")] private void OnCloseInput() => Close();
    [ScriptedEvent("Use")] private void OnUsed() => Toggle();
    [ScriptedEvent("Toggle")] private void OnToggled() => Toggle();

    [ScriptedEvent] private void OnSetSpeed(float speed)
    {
        if (speed > 0f)
            Vars.Speed = speed;
    }

    [ScriptedEvent] private void OnSetRotationDistance(float degrees)
    {
        Vars.Distance = degrees;
        _openAngles = new Vector(_closedAngles.X, _closedAngles.Y + Vars.Distance, _closedAngles.Z);
    }

    private void Toggle()
    {
        if (_state is DoorState.Open or DoorState.Opening)
            Close();
        else
            Open();
    }

    private void Open()
    {
        if (_state is DoorState.Open or DoorState.Opening)
            return;

        _state = DoorState.Opening;
        OnOpen.Fire();
        RotateTo(_openAngles, Vars.Speed);
    }

    private void Close()
    {
        if (_state is DoorState.Closed or DoorState.Closing)
            return;

        _state = DoorState.Closing;
        OnClose.Fire();
        RotateTo(_closedAngles, Vars.Speed);
    }

    public override void OnMoveDone()
    {
        if (_state == DoorState.Opening)
        {
            _state = DoorState.Open;
            OnFullyOpen.Fire();

            if (Vars.ReturnDelay >= 0f)
                SetNextThink(Vars.ReturnDelay);

            return;
        }

        if (_state == DoorState.Closing)
        {
            _state = DoorState.Closed;
            OnFullyClosed.Fire();
        }
    }

    public override void OnThink()
    {
        if (_state == DoorState.Open)
            Close();
    }

    // Reversing rather than crushing, for the same reason the brush doors do it.
    public override void OnBlocked(EntityHandle other)
    {
        if (_state == DoorState.Closing)
        {
            OnBlockedClosing.Fire();
            _state = DoorState.Closed;
            Open();
            return;
        }

        if (_state == DoorState.Opening)
        {
            OnBlockedOpening.Fire();
            _state = DoorState.Open;
            Close();
        }
    }
}

/// <summary>
/// The map keys <see cref="PropDoorRotating"/> declares.
/// </summary>
[ScriptVars]
public partial class PropDoorRotatingVars
{
    public string Model { get; set; } = string.Empty;

    /// <summary>How far it swings, in degrees.</summary>
    public float Distance { get; set; } = 90f;

    public float Speed { get; set; } = 100f;

    /// <summary>Seconds open before closing again. Negative keeps it open.</summary>
    public float ReturnDelay { get; set; } = -1f;

    /// <summary>0 closed, 1 open forward, 2 open back. Where the mapper drew it.</summary>
    [VarName("spawnpos")]
    public int SpawnPosition { get; set; }
}
