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
/// A lift. Sits at the top and comes down when something calls it, or sits at
/// the bottom and is a step up — which of those it is depends on whether the
/// mapper named it, because a named platform is one a button sends for and an
/// unnamed one is scenery you walk onto.
/// </summary>
[ScriptedEntity(EntityNames.FuncPlat, ScriptedEntityKind.Brush)]
public sealed class FuncPlat : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    /// <summary>What a map may set on this entity. See <see cref="FuncPlatVars"/>.</summary>
    [ScriptVars] public FuncPlatVars Vars = new();

    /// <summary>
    /// The two ends of the climb, measured ONCE, in <see cref="OnSpawn"/>, which
    /// a hot reload does not run again -- so they are <c>[SaveField]</c>.
    /// Uncarried, the rebuilt instance held <c>Vector.Zero</c> for both and
    /// every journey after a <c>cs_sv_reload</c> headed for the world origin
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private Vector _top;

    /// <inheritdoc cref="_top"/>
    [SaveField] private Vector _bottom;

    private bool _atTop;
    private bool _moving;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        // CreateVPhysics: a shadow that follows it, which props meet.
        InitShadowPhysics();
        SetVisible(true);

        // Angles on a platform are not a facing — the stock entity zeroes them
        // before measuring, and the measurement below depends on that.
        SetAngles(Vector.Zero);

        _top = SpawnOrigin;

        // Without an explicit height it drops just far enough to be flush,
        // leaving eight units proud. That is the stock rule, and it is what
        // makes an unnamed platform read as a step rather than a hole.
        float travel = Vars.Height != 0f ? Vars.Height : Entities.GetBoundsSize(Handle).Z - 8f;
        _bottom = new Vector(_top.X, _top.Y, _top.Z - travel);

        if (Vars.Speed <= 0f)
            Vars.Speed = 150f;

        // A named platform waits at the top for whatever calls it. An unnamed
        // one has nothing to call it, so it waits at the bottom where a player
        // can step on.
        _atTop = Name.Length > 0;

        if (!_atTop)
            MoveTo(_bottom, Vars.Speed * 100f);
    }

    [ScriptedEvent("GoUp")] private void OnGoUp() => Travel(true);
    [ScriptedEvent("GoDown")] private void OnGoDown() => Travel(false);
    [ScriptedEvent] private void OnToggle() => Travel(!_atTop);

    private void Travel(bool up)
    {
        if (_moving || up == _atTop)
            return;

        _moving = true;
        MoveTo(up ? _top : _bottom, Vars.Speed);
    }

    // OnThink WAS A CARRY TICK AND IS GONE -- plan_entity.md step 2g. This class
    // asked to think every frame while under way for one reason: to hand
    // whoever stood on it the step the platform had just taken. The integrator
    // does that for EVERY mover now, from inside the step itself
    // (SourceSharp.Server/EntityMotion.cs), so a platform under way has nothing
    // to do per frame and stops asking. Four classes carrying and the rest not
    // was the shape that let a func_movelinear drop its passengers through
    // itself.

    public override void OnMoveDone()
    {
        _moving = false;
        _atTop = !_atTop;
    }

    //-------------------------------------------------------------------------
    // A platform that meets something reverses rather than crushing it, for the
    // same reason a door does: killing whoever stands in a lift is a choice, and
    // not one to make by accident.
    //-------------------------------------------------------------------------
    public override void OnBlocked(EntityHandle other)
    {
        if (!_moving)
            return;

        _moving = false;
        Travel(!_atTop);
    }
}

/// <summary>
/// The map keys <see cref="FuncPlat"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPlatVars
{
    /// <summary>How far it travels. Zero means it works its own depth out from the brush.</summary>
    public float Height { get; set; }

    public float Speed { get; set; } = 150f;
}
