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
/// A door on a hinge. Same states, rotation instead of translation.
/// </summary>
[ScriptedEntity(EntityNames.FuncDoorRotating, ScriptedEntityKind.Brush)]
public sealed class FuncDoorRotating : MovingBrush
{
    /// <summary>SF_DOOR_PUSE: a player's +USE opens it.</summary>
    private const int UseOpensFlag = 256;

    /// <summary>CBaseDoor::ObjectCaps: usable, and found in the radius, when use opens it.</summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | ((SpawnFlags & UseOpensFlag) != 0 ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None);

    public FuncDoorRotating(IEnginePrecache precache, IEngineSound sound)
        : base(precache, sound)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncDoorRotatingVars"/>.</summary>
    [ScriptVars] public FuncDoorRotatingVars FuncDoorRotatingVars = new();

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
        base.OnSpawn();

        _closedAngles = SpawnAngles;

        // Around yaw unless the map says otherwise, which is what a door does.
        _openAngles = new Vector(_closedAngles.X, _closedAngles.Y + FuncDoorRotatingVars.Distance, _closedAngles.Z);
    }

    // Rotation is not translation, so the position pair the base keeps is unused
    // here; the angle pair below is what Travel actually reads.
    protected override Vector ComputeOpenPosition() => SpawnOrigin;

    protected override void Travel(Vector destination)
    {
        bool opening = State == DoorState.Opening;
        RotateTo(opening ? _openAngles : _closedAngles, Vars.Speed);
    }
}

/// <summary>
/// The map keys <see cref="FuncDoorRotating"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDoorRotatingVars
{
    /// <summary>How far it swings, in degrees.</summary>
    public float Distance { get; set; } = 90f;
}
