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
/// func_useableladder: HL2MP's ladder (stock's <c>CFuncLadder</c>,
/// game/shared/func_ladder.cpp). Not a brush and not solid: two mount points a
/// player is slid between, climbed along by the movement loop
/// (<c>CHL2GameMovement</c>, <see cref="GameMovement.Ladders"/>), and got off
/// at the info_ladder_dismount points near it.
///
/// <para>
/// Networked so the client's prediction climbs it too: the two mount points,
/// the fake-ladder flag and whether it is disabled
/// (<see cref="UseableLadderNetState"/>). The movement reads every ladder
/// through <see cref="UseableLadders"/>, which this entity joins at spawn.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncUseableladder, ScriptedEntityKind.Effect)]
public sealed class FuncUseableLadder : ScriptedEntity
{
    public FuncUseableLadder(UseableLadders ladders, IEngineWorldQuery world, IEnginePrecache precache,
                             IEngineSound sound)
    {
        Ladders = ladders;
        World = world;
        Precache = precache;
        Sound = sound;
    }

    private readonly UseableLadders Ladders;
    private readonly IEngineWorldQuery World;
    private readonly IEnginePrecache Precache;
    private readonly IEngineSound Sound;

    /// <summary>What a map may set on this entity. See <see cref="FuncUseableLadderVars"/>.</summary>
    [ScriptVars] public FuncUseableLadderVars Vars = new();

    [NetState] public UseableLadderNetState State = new();

    /// <summary>A player got on: the activator is the player.</summary>
    public readonly Output OnPlayerGotOnLadder = new();

    /// <summary>A player got off.</summary>
    public readonly Output OnPlayerGotOffLadder = new();

    /// <summary><c>SF_LADDER_DONTGETON</c>: an automount point that is not really a ladder.</summary>
    public const int DontGetOn = 1;

    /// <summary>The script sound <c>CFuncLadder::PlayerGotOn</c> plays on the player.</summary>
    public const string StepSound = "Ladder.StepRight";

    /// <summary><c>SearchForDismountPoints</c>'s radius around each probe point.</summary>
    public const float DismountRadius = 100.0f;

    /// <summary><c>SearchForDismountPoints</c>'s step up the ladder between probes.</summary>
    public const float DismountRecheck = 40.0f;

    private readonly List<InfoLadderDismount> _dismounts = new();
    private readonly List<Vector> _dismountOrigins = new();

    /// <summary>The dismount points this ladder found at activation, in the order it found them.</summary>
    public IReadOnlyList<InfoLadderDismount> DismountPoints => _dismounts;

    /// <summary>Whether the dismount points have been searched for since this instance was made.</summary>
    private bool _searched;

    /// <summary>
    /// Where those points are, in the same order: what the movement loop reads.
    /// Searched for here if activation has not run on this instance, which is
    /// the case after a hot reload rebuilt it from its saved state.
    /// </summary>
    public IReadOnlyList<Vector> DismountOrigins
    {
        get
        {
            if (!_searched)
                SearchForDismountPoints();

            return _dismountOrigins;
        }
    }

    public override void OnSpawn()
    {
        Precache.PrecacheSound(StepSound);

        MoveConstants hull = Ladders.Hull();

        // Point0 is the top unless it is lower; SetEndPoints swaps them.
        (Vector top, Vector bottom) = SetEndPoints(Vars.Point0, Vars.Point1, hull.HullMin, hull.HullMax);

        State.Top = top;
        State.Bottom = bottom;
        State.Fake = (SpawnFlags & DontGetOn) != 0;
        State.Disabled = Vars.StartDisabled;

        // UTIL_SetSize: the box round both points, grown by the player's hull.
        // Not solid (an Effect is SOLID_NONE); it bounds what the PVS sees.
        Vector origin = Origin;
        Vector t = top - origin, b = bottom - origin;
        Vector lo = new Vector(MathF.Min(t.X, b.X), MathF.Min(t.Y, b.Y), MathF.Min(t.Z, b.Z)) + hull.HullMin;
        Vector hi = new Vector(MathF.Max(t.X, b.X), MathF.Max(t.Y, b.Y), MathF.Max(t.Z, b.Z)) + hull.HullMax;
        Entities.SetBounds(Handle, lo, hi);

        Ladders.Add(this);
    }

    /// <summary>
    /// <c>CFuncLadder::SetEndPoints</c>: the higher point is the top, and each
    /// settles onto whatever is up to four units under it (a hull trace down
    /// from four above, kept unless it starts solid).
    /// </summary>
    private (Vector Top, Vector Bottom) SetEndPoints(Vector p1, Vector p2, Vector hullMin, Vector hullMax)
    {
        Vector top = p1, bottom = p2;

        if (bottom.Z > top.Z)
            (top, bottom) = (bottom, top);

        return (Settle(top, hullMin, hullMax), Settle(bottom, hullMin, hullMax));
    }

    private Vector Settle(Vector point, Vector hullMin, Vector hullMax)
    {
        EntityHullTrace tr = World.EntityTraceHull(EntityHandle.None, point + new Vector(0f, 0f, 4f), point,
                                                   hullMin, hullMax, GameMovement.MaskPlayerSolidBrushOnly,
                                                   GameMovement.CollisionGroupPlayerMovement);
        return tr.StartSolid ? point : tr.EndPos;
    }

    /// <summary>
    /// <c>CFuncLadder::Activate</c> → <c>SearchForDismountPoints</c>: every
    /// dismount point within 100 of the top, of the bottom, and of each point
    /// 40 apart up the ladder, once each. A point with a target is this
    /// ladder's only if that target names this ladder.
    /// </summary>
    public override void OnServerActivate() => SearchForDismountPoints();

    private void SearchForDismountPoints()
    {
        _searched = true;

        Vector top = State.Top;
        Vector bottom = State.Bottom;

        Vector bottomToTop = top - bottom;
        float ladderLength = MoveVec.Normalize(ref bottomToTop);

        FindNearbyDismountPoints(top, DismountRadius);
        FindNearbyDismountPoints(bottom, DismountRadius);

        while (true)
        {
            ladderLength -= DismountRecheck;
            if (ladderLength <= 0.0f)
                break;

            bottom += DismountRecheck * bottomToTop;
            FindNearbyDismountPoints(bottom, DismountRadius);
        }
    }

    /// <summary><c>CFuncLadder::FindNearbyDismountPoints</c>, the server arm.</summary>
    private void FindNearbyDismountPoints(Vector origin, float radius)
    {
        float maxDistSqr = radius * radius;

        foreach (InfoLadderDismount spot in Ladders.AllDismounts)
        {
            Vector d = spot.State.Origin - origin;
            float distSqr = d.X * d.X + d.Y * d.Y + d.Z * d.Z;

            // FindEntityByClassnameWithin: strictly inside.
            if (!(maxDistSqr > distSqr))
                continue;

            // A spot with a target belongs only to the ladder it names
            // (GetNextTarget, the first entity by that name).
            if (spot.Vars.Target.Length > 0 && FindByName<ScriptedEntity>(spot.Vars.Target) != this)
                continue;

            if (!_dismounts.Contains(spot))
            {
                _dismounts.Add(spot);
                _dismountOrigins.Add(spot.State.Origin);
            }
        }
    }

    [ScriptedEvent("Enable")]
    private void OnEnable() => State.Disabled = false;

    [ScriptedEvent("Disable")]
    private void OnDisable() => State.Disabled = true;

    /// <summary><c>CFuncLadder::PlayerGotOn</c>: the output, and the step sound on the player.</summary>
    public void PlayerGotOn(EntityHandle player)
    {
        OnPlayerGotOnLadder.Fire(player);
        Sound.EntityEmitSound(player, StepSound);
    }

    /// <summary><c>CFuncLadder::PlayerGotOff</c>.</summary>
    public void PlayerGotOff(EntityHandle player) => OnPlayerGotOffLadder.Fire(player);

    /// <summary>A hot reload rebuilt this ladder from its saved state: it joins the new composition's ladders.</summary>
    public override void LoadState(SavedEntity state)
    {
        base.LoadState(state);
        Ladders.Add(this);
    }

    public override void OnRemoved() => Ladders.Remove(this);
}

/// <summary>The map keys <see cref="FuncUseableLadder"/> declares.</summary>
[ScriptVars]
public partial class FuncUseableLadderVars
{
    /// <summary>One mount point, in world space. The higher of the two is the top.</summary>
    [VarName("point0")]
    public Vector Point0 { get; set; }

    /// <summary>The other mount point.</summary>
    [VarName("point1")]
    public Vector Point1 { get; set; }

    public bool StartDisabled { get; set; }

    /// <summary>
    /// The surface its climbing steps sound like. Read and kept, as stock's
    /// DATADESC does; this build's ladder footsteps are the client's and use
    /// "ladder" (not ported: see docs/divergences.md).
    /// </summary>
    [VarName("ladderSurfaceProperties")]
    public string SurfaceProperties { get; set; } = string.Empty;
}
