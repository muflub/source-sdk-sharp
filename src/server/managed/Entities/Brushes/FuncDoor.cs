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
    /// <summary>SF_DOOR_PUSE: a player's +USE opens it.</summary>
    private const int UseOpensFlag = 256;

    /// <summary>CBaseDoor::ObjectCaps: usable, and found in the radius, when use opens it.</summary>
    public override UseCaps ObjectCaps
        => base.ObjectCaps | ((SpawnFlags & UseOpensFlag) != 0 ? UseCaps.ImpulseUse | UseCaps.UseInRadius : UseCaps.None);

    public FuncDoor(IEnginePrecache precache, IEngineSound sound)
        : base(precache, sound)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncDoorVars"/>.</summary>
    [ScriptVars] public FuncDoorVars FuncDoorVars = new();

    protected override Vector ComputeOpenPosition()
    {
        // Stock's CBaseDoor::Spawn: the direction is the movedir key (the
        // FGD's "Move Direction"), 0 0 0 -- +x -- when the map gives none.
        // The angles are the brush's rotation and stay: a map that points a
        // door with its angles has a door turned on its side, in stock and
        // here (MoverDirectionTests).
        var direction = Angles.Forward(FuncDoorVars.MoveDir);

        // The door travels its own depth along the direction, less the lip.
        // It has no distance key: that is a rotating door's angle.
        return ClosedPosition + direction * (DepthAlong(Entities.GetBoundsSize(Handle), direction) - FuncDoorVars.Lip);
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

    protected override void Travel(Vector destination) => MoveTo(destination, Vars.Speed);
}

/// <summary>
/// The map keys <see cref="FuncDoor"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDoorVars
{
    /// <summary>Which way it slides: the movedir key, written as angles.</summary>
    public QAngle MoveDir { get; set; }

    /// <summary>How much of the brush stays behind when open.</summary>
    public float Lip { get; set; }
}
