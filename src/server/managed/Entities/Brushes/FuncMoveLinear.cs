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
/// A brush that slides between two points and can be parked anywhere between
/// them. Lifts and shutters, and anything a map wants to set to a fraction
/// rather than open or shut.
///
/// Water that rises and falls is the same entity: a volume of water IS a brush
/// sliding along a line, and stock says so by registering the third name below
/// to this class rather than writing another one.
/// </summary>
[ScriptedEntity(EntityNames.FuncMovelinear, ScriptedEntityKind.Brush)]
// The SDK registers this name to the same class, for maps old enough to use it.
[ScriptedEntity(EntityNames.MomentaryDoor, ScriptedEntityKind.Brush)]
// And this one, for water whose level moves. See IsWater.
[ScriptedEntity(EntityNames.FuncWaterAnalog, ScriptedEntityKind.Brush)]
public sealed class FuncMoveLinear : ScriptedEntity
{
    public readonly Output OnFullyOpen = new();
    public readonly Output OnFullyClosed = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncMoveLinearVars"/>.</summary>
    [ScriptVars] public FuncMoveLinearVars Vars = new();

    /// <summary>
    /// The two ends of the travel, in world space. Computed ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so they
    /// are <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> for both and every move after a <c>cs_sv_reload</c>
    /// headed for the world origin (FuncMoveLinearReloadTests).
    /// </summary>
    [SaveField] private Vector _closed;

    /// <inheritdoc cref="_closed"/>
    [SaveField] private Vector _open;

    /// <summary>
    /// Where it is heading, as a fraction of the travel. 0 shut, 1 open.
    /// Carried for the same reason: the arrival reports by it.
    /// </summary>
    [SaveField] private float _target;

    /// <summary>Makes the brush passable. Ignored by water, which see.</summary>
    private const int SF_NotSolid = 8;

    /// <summary>
    /// Whether this one is a body of water rather than a slab of world.
    ///
    /// The only thing the classname decides, and stock decides it the same way
    /// — <c>FClassnameIs</c>, twice, inside Spawn. What it changes is that the
    /// water is never made passable by the spawnflag: a volume of water you
    /// cannot enter is not water.
    /// </summary>
    private bool IsWater
        => string.Equals(ClassName, EntityNames.FuncWaterAnalog, StringComparison.OrdinalIgnoreCase);

    public override void OnSpawn()
    {
        SetSolidity(!IsWater && (SpawnFlags & SF_NotSolid) != 0
            ? BrushSolidity.None
            : BrushSolidity.Solid);

        SetVisible(true);

        // Same rule as a door: the direction is in the angles, and the engine
        // zeroes them once it has read them.
        // Whether the map wrote movedir, asked of the key rather than of its
        // text: comparing against "0 0 0" answered no for a map that wrote
        // exactly that on purpose.
        var direction = Vars.MoveDirIsSet
            ? Angles.Forward(Vars.MoveDir)
            : Angles.Forward(SpawnAngles);
        SetAngles(Vector.Zero);

        // StartPosition is a fraction, so the drawn position is wherever along
        // the travel the mapper parked it — not necessarily the shut end.
        var drawn = SpawnOrigin;
        _closed = drawn - direction * (Vars.MoveDistance * Vars.StartPosition);
        _open = _closed + direction * Vars.MoveDistance;

        _target = Vars.StartPosition;

        if (Vars.Speed <= 0f)
            Vars.Speed = 100f;
    }

    [ScriptedEvent("Open")] private void OnOpenInput() => MoveToFraction(1f);
    [ScriptedEvent("Close")] private void OnCloseInput() => MoveToFraction(0f);
    [ScriptedEvent] private void OnSetPosition(float fraction) => MoveToFraction(fraction);
    [ScriptedEvent] private void OnSetSpeed(float speed) => Vars.Speed = speed > 0f ? speed : Vars.Speed;

    private void MoveToFraction(float fraction)
    {
        _target = Math.Clamp(fraction, 0f, 1f);
        MoveTo(_closed + (_open - _closed) * _target, Vars.Speed);
    }

    public override void OnMoveDone()
    {
        // Only the ends are reported. A map that parks this halfway is not
        // opening or closing it, and firing either output there would be a lie.
        if (_target >= 1f)
            OnFullyOpen.Fire();
        else if (_target <= 0f)
            OnFullyClosed.Fire();
    }
}

/// <summary>
/// The map keys <see cref="FuncMoveLinear"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncMoveLinearVars
{
    public QAngle MoveDir { get; set; }

    public float MoveDistance { get; set; } = 100f;

    public float StartPosition { get; set; }

    public float Speed { get; set; } = 100f;
}
