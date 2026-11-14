//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Rooms;
using TF2.Shared.Rules;

namespace TF2.Client.Entities;

/// <summary>
/// The client's half of func_respawnroomvisualizer: stock's
/// <c>C_FuncRespawnRoomVisualizer::DrawModel</c>
/// (client/tf/c_func_respawnroom.cpp:48-64). The shimmer is custom-drawn, so
/// its two hiding arms and its drawn arm answer at this entity's one draw hook
/// each frame: nothing at all while the round sits in its win state (the rooms
/// stand open, the doors behind them are the way in); nothing for a local
/// player on the visualizer's own team — a blue player walks through his own
/// room's doorway unbothered — and the team's own colour, translucent and
/// billboarded across the brush's box, for every other eye. With no local
/// player yet the wall draws for everybody, stock's null-player arm.
/// </summary>
/// <remarks>
/// The stock arms are the whole of it — the endround check
/// (<c>TFGameRules()->State_Get() == GR_STATE_TEAM_WIN</c>, :51-54), then the
/// own-team check over the decoded team (<c>pLocalPlayer &&
/// pLocalPlayer->GetTeamNumber() == GetTeamNumber()</c>, :57-61), then the
/// drawn branch (:63). A teammate walking through his own room is stock too:
/// the wall is solid to movement only through the other team's bit
/// (<see cref="SourceSharp.Shared.CollisionRules"/>'s pair of arms, from the
/// server's side).
/// </remarks>
[ClientStandaloneEntity(RespawnRoomVisualizerNetState.ClassName)]
public sealed class ClientRespawnRoomVisualizer(IEngineEntityRender render, IFxRender fx, IPixelVisSlots view,
                                                ITFRoundStateView rounds, TFClientPlayerResources resources)
    : ClientStandaloneEntity(render)
{
    private readonly IFxRender _fx = fx;
    private readonly IPixelVisSlots _view = view;
    private readonly ITFRoundStateView _rounds = rounds;
    private readonly TFClientPlayerResources _resources = resources;

    /// <summary>The sprite materials, the ones this content already ships for the two teams (:79-80).</summary>
    public const string SpriteBlue = ClientSniperDot.SpriteBlue;
    public const string SpriteRed = ClientSniperDot.SpriteRed;

    /// <summary>The shimmer's opacity: half-alpha, the translucent outer-sprite weight the dot uses (:83).</summary>
    public const byte ShimmerAlpha = 128;

    private int _material;

    public override string ClassName => RespawnRoomVisualizerNetState.ClassName;

    /// <summary>The same declaration the server writes through.</summary>
    [NetState] public RespawnRoomVisualizerNetState State = new();

    /// <summary>
    /// The create arm: the team picks its sprite once (:1701-1716 of stock's
    /// dot's shape — blue for BLUE, red for anything else), and the entity
    /// asks for its custom draw over a box that always holds the billboard,
    /// plus the translucent pass.
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_material == 0)
            _material = _fx.MaterialFind(State.Team == (byte)TFTeam.Blue ? SpriteBlue : SpriteRed);

        float half = MathF.Max(State.Width, State.Height) * 0.5f
                   + MathF.Max(MathF.Abs(State.CenterX), MathF.Max(MathF.Abs(State.CenterY), MathF.Abs(State.CenterZ)));
        Render.SetCustomDraw(Handle, new Vector(-half, -half, -half), new Vector(half, half, half));
        Render.SetBlending(Handle, translucent: true, ignoreZ: false, alpha: 255);
    }

    public override void OnRemoved()
    {
        if (_material != 0)
            _fx.MaterialRelease(_material);
        _material = 0;
    }

    /// <summary>DrawModel's three arms: hidden, hidden, drawn.</summary>
    public override int OnDraw()
    {
        // Endround: nothing draws (:51-54).
        if (_rounds.State == RoundState.TeamWin)
            return 0;

        // A teammate of the visualizer sees through the doorway (:57-61). No
        // local player yet — stock's null arm — and every eye gets the wall.
        int local = _fx.LocalPlayer();
        if (local > 0 && _resources.Live is { } live && live.State.Team[local] == State.Team)
            return 0;

        CurrentViewState view = _view.View();
        Vector origin = Entities.GetOrigin(Handle);
        Vector centre = origin + new Vector(State.CenterX, State.CenterY, State.CenterZ);

        // g_aTeamColors (shared/tf/tf_shareddefs.cpp:26-32): BLUE blue, RED red.
        byte r, g, b;
        if (State.Team == (byte)TFTeam.Blue)
        {
            r = 0;
            g = 0;
            b = 255;
        }
        else
        {
            r = 255;
            g = 0;
            b = 0;
        }

        Span<FxVertex> quad = stackalloc FxVertex[4];
        EffectQuads.Sprite(quad, centre, State.Width, State.Height, r, g, b, ShimmerAlpha, view);
        _fx.DrawQuads(_material, quad, byteColor: true);
        return 1;
    }
}
