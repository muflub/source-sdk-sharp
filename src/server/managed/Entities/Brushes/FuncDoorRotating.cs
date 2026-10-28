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
/// CRotDoor (doors.cpp): a door on a hinge. It turns its distance about yaw,
/// or roll or pitch as AxisDir reads its spawnflags, backwards with
/// SF_DOOR_ROTATE_BACKWARDS; a yaw door that is not one-way swings away from
/// whoever opened it. A door that is not solid (a passable one) makes no
/// physics shadow.
/// </summary>
[ScriptedEntity(EntityNames.FuncDoorRotating, ScriptedEntityKind.Brush)]
public sealed class FuncDoorRotating : MovingBrush
{
    public FuncDoorRotating(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage, IEngineNetworking net,
                            PhysicsEntitySolver solver)
        : base(precache, sound, damage, net, solver)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncDoorRotatingVars"/>.</summary>
    [ScriptVars] public FuncDoorRotatingVars FuncDoorRotatingVars = new();

    /// <summary>
    /// m_vecAngle1 and m_vecAngle2, and m_vecMoveAng: the facings it turns
    /// between and the axis it turns about, derived from the map's keys ONCE,
    /// in the spawn, which a hot reload does not run again -- so they are
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> and every open after a <c>cs_sv_reload</c> slammed the
    /// door flat (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _closedAngles;
    [SaveField] private Vector _openAngles;
    [SaveField] private Vector _moveAngles;

    protected override bool IsRotatingDoor => true;

    protected override QAngle MoveAngles => QAngle.FromVector(_moveAngles);

    protected override (string Move, string Arrive, string Locked) DefaultSounds
        => ("RotDoorSound.DefaultMove", "RotDoorSound.DefaultArrive", "RotDoorSound.DefaultLocked");

    /// <summary>CRotDoor::CreateVPhysics: a shadow only for a door that is solid.</summary>
    protected override bool MakesShadow => (Entities.GetSolidFlags(Handle) & SolidFlags.NotSolid) == 0;

    /// <summary>
    /// CRotDoor::Spawn: AxisDir, the backwards flag, and the two facings. A
    /// door that starts open the obsolete way swaps them and turns the other
    /// way; spawnpos 1 starts it at the open facing.
    /// </summary>
    protected override void SpawnTravel()
    {
        _moveAngles = AxisDir((RotationAxisFlag)SpawnFlags);
        if (Flags.HasFlag(DoorFlag.RotateBackwards))
            _moveAngles *= -1f;

        _closedAngles = SpawnAngles;
        _openAngles = _closedAngles + _moveAngles * FuncDoorRotatingVars.Distance;

        if (Flags.HasFlag(DoorFlag.StartOpenObsolete))
        {
            (_closedAngles, _openAngles) = (_openAngles, _closedAngles);
            _moveAngles *= -1f;
            SetAngles(_closedAngles);
            State = ToggleState.AtBottom;
        }
        else if (Vars.SpawnPosition == 1)
        {
            SetAngles(_openAngles);
            State = ToggleState.AtTop;
        }
        else
        {
            State = ToggleState.AtBottom;
        }

        // Slam the object back to solid -- if we really want it to be solid.
        if (FuncDoorRotatingVars.SolidBsp)
            SetSolidity(SolidType.Bsp, Entities.GetSolidFlags(Handle));
    }

    /// <summary>CBaseToggle::AxisDir: roll, else pitch, else yaw, as a unit turn.</summary>
    internal static Vector AxisDir(RotationAxisFlag flags)
        => flags.HasFlag(RotationAxisFlag.Roll) ? new Vector(0f, 0f, 1f)
         : flags.HasFlag(RotationAxisFlag.Pitch) ? new Vector(1f, 0f, 0f)
         : new Vector(0f, 1f, 0f);

    /// <summary>AngularMove( m_vecAngle2 * sign ): the whole facing turned by the sign, as stock writes it.</summary>
    protected override void TravelUp(float sign) => RotateTo(_openAngles * sign, Vars.Speed);

    protected override void TravelDown() => RotateTo(_closedAngles, Vars.Speed);

    /// <summary>CRotDoor::SetToggleState.</summary>
    protected override void PlaceAt(ToggleState end) => SetAngles(end == ToggleState.AtTop ? _openAngles : _closedAngles);
}

/// <summary>
/// The map keys <see cref="FuncDoorRotating"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDoorRotatingVars
{
    /// <summary>How far it swings, in degrees (CBaseToggle's m_flMoveDistance; none when the map gives none).</summary>
    public float Distance { get; set; }

    /// <summary>SOLID_BSP rather than SOLID_VPHYSICS.</summary>
    [VarName("solidbsp")]
    public bool SolidBsp { get; set; }
}
