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
/// A door that slides. The map gives a direction (movedir) and the distance
/// is worked out from how big the brush is, which is why the open position is
/// computed rather than given.
///
/// Water whose level moves is the same entity, and stock says so the same way:
/// one extra <c>LINK_ENTITY_TO_CLASS</c> onto CBaseDoor and nothing else, because
/// raising a body of water IS sliding a brush. What the classname changes in
/// stock is that the volume's contents count as water — see IsWater on
/// FuncMoveLinear, which has the same pair and the same limitation.
/// </summary>
[ScriptedEntity(EntityNames.FuncDoor, ScriptedEntityKind.Brush)]
// The SDK registers this name to the same class, for water that rises and falls.
[ScriptedEntity(EntityNames.FuncWater, ScriptedEntityKind.Brush)]
public sealed class FuncDoor : MovingBrush
{
    public FuncDoor(IEnginePrecache precache, IEngineSound sound, IEngineDamage damage, IEngineNetworking net,
                    PhysicsEntitySolver solver)
        : base(precache, sound, damage, net, solver)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncDoorVars"/>.</summary>
    [ScriptVars] public FuncDoorVars FuncDoorVars = new();

    /// <summary>
    /// m_vecPosition1 and m_vecPosition2: where it sits shut and open, worked
    /// out ONCE, at spawn, from the map's keys, which a hot reload does not
    /// read again -- so they are carried. Uncarried, the rebuilt instance
    /// held Vector.Zero and every close after a cs_sv_reload headed for the
    /// world origin (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] internal Vector ClosedPosition { get; private set; }

    [SaveField] internal Vector OpenPosition { get; private set; }

    protected override bool IsRotatingDoor => false;

    protected override (string Move, string Arrive, string Locked) DefaultSounds
        => ("DoorSound.DefaultMove", "DoorSound.DefaultArrive", "DoorSound.DefaultLocked");

    /// <summary>
    /// CBaseDoor::Spawn: the travel is the movedir key (the FGD's "Move
    /// Direction"), 0 0 0 -- +x -- when the map gives none, the brush's own
    /// depth along it less the lip. The angles are the brush's rotation and
    /// stay: a map that points a door with its angles has a door turned on its
    /// side, in stock and here (MoverDirectionTests). spawnpos 1 or the
    /// obsolete start-open flag put it at the open end.
    /// </summary>
    protected override void SpawnTravel()
    {
        ClosedPosition = SpawnOrigin;
        var direction = Angles.Forward(Vars.MoveDir);
        OpenPosition = ClosedPosition + direction * (DepthAlong(Entities.GetBoundsSize(Handle), direction) - FuncDoorVars.Lip);

        if (Vars.SpawnPosition == 1 || Flags.HasFlag(DoorFlag.StartOpenObsolete))
        {
            Origin = OpenPosition;
            State = ToggleState.AtTop;
        }
        else
        {
            State = ToggleState.AtBottom;
        }
    }

    /// <summary>
    /// How deep a brush is along a direction, from its collision size: stock's
    /// <c>DotProductAbs( movedir, OBBSize - (2,2,2) )</c>, the 2 being the unit
    /// the engine grows a brush's bounds by on every side.
    /// </summary>
    internal static float DepthAlong(Vector boundsSize, Vector direction)
        => MathF.Abs(direction.X * (boundsSize.X - 2f))
         + MathF.Abs(direction.Y * (boundsSize.Y - 2f))
         + MathF.Abs(direction.Z * (boundsSize.Z - 2f));

    protected override void TravelUp(float sign) => MoveTo(OpenPosition, Vars.Speed);

    protected override void TravelDown() => MoveTo(ClosedPosition, Vars.Speed);

    /// <summary>CBaseDoor::SetToggleState.</summary>
    protected override void PlaceAt(ToggleState end) => Origin = end == ToggleState.AtTop ? OpenPosition : ClosedPosition;
}

/// <summary>
/// The map keys <see cref="FuncDoor"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDoorVars
{
    /// <summary>How much of the brush stays behind when open.</summary>
    public float Lip { get; set; }
}
