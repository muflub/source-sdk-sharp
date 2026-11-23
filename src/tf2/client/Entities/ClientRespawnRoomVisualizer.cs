//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;

using TF2.Client.Player;
using TF2.Shared.Rooms;
using TF2.Shared.Rules;

namespace TF2.Client.Entities;

/// <summary>
/// The client's half of func_respawnroomvisualizer: stock's
/// <c>C_FuncRespawnRoomVisualizer::DrawModel</c>
/// (client/tf/c_func_respawnroom.cpp:48-64). Its two hiding arms and its drawn
/// arm answer at this entity's one draw hook each frame: nothing at all while
/// the round sits in its win state (the rooms stand open, the doors behind them
/// are the way in); nothing for a local player on the visualizer's own team —
/// a blue player walks through his own room's doorway unbothered — and, for
/// every other eye, the brush itself, drawn as any brush is. With no local
/// player yet the brush draws for everybody, stock's null-player arm.
/// </summary>
/// <remarks>
/// The drawn arm is stock's <c>BaseClass::DrawModel</c>: the brush the map
/// gave it, in the map's own material (cp_badlands' are
/// <c>overlays/no_entry</c>, whose proxies fade it out with the local player's
/// distance). Nothing of its look is the class's to choose. A teammate walking
/// through his own room is stock too: the wall is solid to movement only
/// through the other team's bit (<see cref="SourceSharp.Shared.CollisionRules"/>'s
/// pair of arms, from the server's side).
/// </remarks>
[ClientStandaloneEntity(RespawnRoomVisualizerNetState.ClassName)]
public sealed class ClientRespawnRoomVisualizer(IEngineEntityRender render, IFxRender fx, IEngineWorldQuery world,
                                                ITFRoundStateView rounds, TFClientPlayerResources resources)
    : ClientStandaloneEntity(render)
{
    private readonly IFxRender _fx = fx;
    private readonly IEngineWorldQuery _world = world;
    private readonly ITFRoundStateView _rounds = rounds;
    private readonly TFClientPlayerResources _resources = resources;

    public override string ClassName => RespawnRoomVisualizerNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public RespawnRoomVisualizerNetState State = new();

    /// <summary>
    /// The draw comes through this class so its arms can refuse it, over the
    /// brush's own box (stock's render bounds are its model's), in the
    /// translucent pass the brush's material belongs to. Asked again on each
    /// update until the model's box is known.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (!_world.ClientEntityModelBounds(Handle.EntryIndex, out Vector mins, out Vector maxs, out _))
            return;

        Render.SetCustomDraw(Handle, mins, maxs);
        Render.SetBlending(Handle, translucent: true, ignoreZ: false, alpha: 255);
    }

    /// <summary>The draw is the brush's, made at its DrawModel: there is nothing to prepare.</summary>
    public override int OnDrawBatched() => DrawsInPlace;

    /// <summary>DrawModel's three arms: hidden, hidden, the brush.</summary>
    public override int OnDraw()
    {
        // Endround: nothing draws (:51-54).
        if (_rounds.State == RoundState.TeamWin)
            return 1;

        // A teammate of the visualizer sees through the doorway (:57-61). No
        // local player yet — stock's null arm — and every eye gets the wall.
        int local = _fx.LocalPlayer();
        if (local > 0 && _resources.Live is { } live && live.State.Team[local] == State.Team)
            return 1;

        // BaseClass::DrawModel (:63).
        return DrawsModel;
    }
}
