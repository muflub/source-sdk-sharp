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
/// A door that slides. The map gives a direction as angles and a distance
/// worked out from how big the brush is, which is why the open position is
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
    public FuncDoor(IEnginePrecache precache, IEngineSound sound)
        : base(precache, sound)
    {
    }

    /// <summary>What a map may set on this entity. See <see cref="FuncDoorVars"/>.</summary>
    [ScriptVars] public FuncDoorVars FuncDoorVars = new();

    protected override Vector ComputeOpenPosition()
    {
        // A door's direction comes from its ANGLES, not from a movedir key.
        // That is how the FGD works and how every map is authored: the mapper
        // points the door and the engine converts that to a vector, then zeroes
        // the angles so the brush is not left rotated. Nothing writes "movedir",
        // which is why reading it gave every door the same default direction.
        //
        // "Did the map write movedir" used to be asked by comparing the key's
        // text against "0 0 0", which answered no for a map that wrote exactly
        // that on purpose. IsSet asks the question the code actually means.
        var direction = FuncDoorVars.MoveDirIsSet
            ? Angles.Forward(FuncDoorVars.MoveDir)
            : Angles.Forward(SpawnAngles);

        SetAngles(Vector.Zero);

        // Without an explicit distance the door travels its own depth along the
        // move direction, less the lip. That is the stock rule, and it is why a
        // door drawn wider slides further without the mapper saying so.
        float distance = FuncDoorVars.MoveDistance > 0f ? FuncDoorVars.MoveDistance : DefaultTravel(direction) - FuncDoorVars.Lip;

        return ClosedPosition + direction * distance;
    }

    /// <summary>
    /// How far the brush must travel to clear its own opening. Approximated
    /// from the collision bounds, which is what the engine measures too.
    /// </summary>
    private float DefaultTravel(Vector direction)
    {
        var size = Entities.GetBoundsSize(Handle);

        return MathF.Abs(direction.X * size.X)
             + MathF.Abs(direction.Y * size.Y)
             + MathF.Abs(direction.Z * size.Z);
    }

    protected override void Travel(Vector destination) => MoveTo(destination, Vars.Speed);
}

/// <summary>
/// The map keys <see cref="FuncDoor"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncDoorVars
{
    /// <summary>Which way it slides, written by the map as angles.</summary>
    public QAngle MoveDir { get; set; }

    /// <summary>How far it slides. Zero means the map wants the brush's own size used.</summary>
    public float MoveDistance { get; set; }

    /// <summary>How much of the brush stays behind when open.</summary>
    public float Lip { get; set; }
}
