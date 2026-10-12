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
/// A belt. The brush stays where the mapper drew it and carries whoever stands
/// on it, in the direction the map asked for, until something reverses it.
///
/// <para>
/// The decisions are which way, how fast, and who counts as riding. The first
/// two are a signed speed along one direction — a negative speed IS the belt
/// running backwards, which is why <c>ToggleDirection</c> is a sign flip and
/// nothing else — and the third is the part that had to be rewritten rather
/// than moved.
/// </para>
///
/// <para>
/// Stock asks the engine: the brush wears <c>FL_CONVEYOR</c>, and the movement
/// code carries anything whose GROUND ENTITY is wearing it. Neither half of that
/// is reachable from here. A managed entity is not a <c>CBaseEntity</c>, so it
/// never becomes anybody's ground entity — the trace that would name it answers
/// "the world" — and there is no managed way to set an engine entity flag. So a
/// rider is found by position instead: standing on the belt means feet at the
/// top face, within the reach of the same downward trace the movement code makes
/// to decide it is standing on anything at all. What is carried is then said the
/// same way stock says it, through base velocity, which is the engine's channel
/// for "moved by something else" and the only one the movement code does not
/// overwrite from the player's input each tick.
/// </para>
///
/// <para>
/// Every tick, and that is not a choice either: base velocity is consumed and
/// cleared per command, and a belt that renewed it ten times a second would
/// deliver ten kicks a second instead of a steady ride. It is the point_push
/// mistake in a different dress, and the fix is to run at the rate the thing
/// being fed runs at.
/// </para>
///
/// <para>
/// So a running belt costs one spatial-partition query per tick whether anybody
/// is on it or not, where stock cost nothing until somebody stood on one. That
/// is the price of not being told, and it is left as it is deliberately: the
/// obvious saving — look around slowly until a rider appears, then speed up — is
/// what makes a rider at the edge of the belt, found on some ticks and not
/// others, get flung by the leftover base velocity each time the belt loses
/// track of them.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncConveyor, ScriptedEntityKind.Brush)]
public sealed class FuncConveyor : ScriptedEntity
{
    public FuncConveyor(IEngineNetworking net, IEngineWorldQuery world)
    {
        Net = net;
        World = world;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="FuncConveyorVars"/>.</summary>
    [ScriptVars] public FuncConveyorVars Vars = new();

    /// <summary>
    /// func_conveyor's own spawnflags.
    ///
    /// <para>
    /// Both are ways of being less than a belt, and they are not the same way: one
    /// still carries and cannot be walked through, the other can be walked through
    /// and still carries. A mapper reaching for "the belt does nothing" needs both,
    /// which is easier to see when they are one type.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>Scrolls, but carries nothing. A painted belt.</summary>
        VisualOnly = 0b0000_0001,

        /// <summary>Something walks through rather than along. Stock calls it a hack and it is one.</summary>
        NotSolid = 0b0000_0010,
    }

    /// <summary>
    /// Which way the belt runs, turned from the map's angles ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance held
    /// <c>Vector.Zero</c> and every rider after a <c>cs_sv_reload</c> was shoved
    /// nowhere (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _direction;

    /// <summary>Exposed for the tests, which have no map to read keys from.</summary>
    internal float Speed { get => Vars.Speed; set => Vars.Speed = value; }
    internal Vector Direction => _direction;

    public override void OnSpawn()
    {
        // The map writes a facing and means a direction. Stock converts the
        // angles in place during Spawn and then treats the field as a vector,
        // which is why its "movedir" reads as one everywhere afterwards.
        _direction = Angles.Forward(Vars.MoveDir);

        if (Vars.Speed == 0f)
            Vars.Speed = 100f;

        SetSolidity(Flags.HasFlag(SpawnFlag.NotSolid) ? BrushSolidity.None : BrushSolidity.Solid);
        SetVisible(true);

        if (!Flags.HasFlag(SpawnFlag.VisualOnly))
            SetNextThink(0f);
    }

    /// <summary>
    /// Runs it the other way. The whole of reversing a belt is the sign of its
    /// speed, so nothing else here has to know a direction changed.
    /// </summary>
    [ScriptedEvent]
    private void OnToggleDirection() => Vars.Speed = -Vars.Speed;

    /// <summary>
    /// The use key does the same thing, which is what stock's Use does and the
    /// only way a map made before the input existed can reverse one.
    /// </summary>
    [ScriptedEvent("Use")]
    private void OnUsed() => Vars.Speed = -Vars.Speed;

    [ScriptedEvent("SetSpeed")]
    private void OnSpeedSet(float speed) => Vars.Speed = speed;

    public override void OnThink()
    {
        Carry();
        SetNextThink(0f);
    }

    /// <summary>Exposed for the tests, which have no engine to think for them.</summary>
    internal void Carry()
    {
        // A stopped belt is not the same as a belt pushing nothing: leaving base
        // velocity unrenewed is what lets the movement code hand a rider the
        // remainder as an impulse, which is how stepping off the end of a belt
        // throws you. Renewing a zero would take that away.
        if (Vars.Speed == 0f)
            return;

        // The box test, the movetype test and the crowd cap all live in
        // Riders now, because the lifts carry the same way this belt does —
        // see plan_baseentity.md Phase 3c. Moving there also retired this
        // class's old assumption that its box is centred on its origin:
        // Riders rebuilds the box from GetBounds, which answers as offsets
        // from the origin for both hierarchies, so a conveyor drawn without an
        // origin key now carries its own patch of floor rather than the map
        // origin's.
        Riders.Ride(World, Net, Entities, Handle, Origin, _direction * Vars.Speed,
                     $"{ClassName} '{Name}'", Log);
    }
}

/// <summary>
/// The map keys <see cref="FuncConveyor"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncConveyorVars
{
    /// <summary>Which way it runs, written by the map as angles rather than a vector.</summary>
    public QAngle MoveDir { get; set; }

    /// <summary>Units per second, signed: negative is the same belt run backwards.</summary>
    public float Speed { get; set; } = 100f;
}
