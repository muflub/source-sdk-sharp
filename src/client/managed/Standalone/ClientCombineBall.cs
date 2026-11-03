//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// prop_combine_ball's client half (C_PropCombineBall, c_prop_combine_ball.cpp). A thrown
/// ball never draws its model: a flickering glow, and over it the body, a sprite that
/// pulses and faces the eye at the roll its spawn time gives it. In the translucent group.
/// </summary>
[ClientStandaloneEntity(EntityNames.PropCombineBall)]
public sealed class ClientCombineBall : ClientStandaloneEntity
{
    /// <summary>What the ball draws with (InitMaterials).</summary>
    public const string BodyMaterial = "effects/ar2_altfire1", FlickerMaterial = "effects/combinemuzzle1";

    private readonly IFxRender _fx;
    private readonly IPixelVisSlots _view;
    private readonly IUniformRandom _random;
    private readonly IGameClock _clock;
    private readonly IEngineEntities _entities;

    private int _body = -1, _flicker = -1;
    private float _spawnTime;

    public ClientCombineBall(IEngineEntityRender render, IFxRender fx, IPixelVisSlots view,
                             IUniformRandom random, IGameClock clock, IEngineEntities entities)
        : base(render)
    {
        _entities = entities;
        _fx = fx;
        _view = view;
        _random = random;
        _clock = clock;
    }

    public override string ClassName => EntityNames.PropCombineBall;

    [NetState] public CombineBallNetState State = new();

    /// <summary>How far the draw reaches from the ball: the flicker's largest, for the cull.</summary>
    private const float CullHalf = 32f;

    /// <summary>OnDataChanged: its spawn time once, and the translucent pass (GetRenderGroup).</summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (updateType == DataUpdateType.Created)
            _spawnTime = _clock.Now;

        Render.SetCustomDraw(Handle, new Vector(-CullHalf, -CullHalf, -CullHalf), new Vector(CullHalf, CullHalf, CullHalf));
        Render.SetBlending(Handle, translucent: true, ignoreZ: false, alpha: 255);
    }

    /// <summary>The body's half size at <paramref name="now"/>: its radius, breathing a unit each way.</summary>
    public static float BodyScale(float radius, float now) => radius + MathF.Sin(now * 25f);

    /// <summary>DrawModel, for a ball never held: nothing past the end, and never the model.</summary>
    public override int OnDraw()
    {
        if (!State.Emit)
            return 0;

        if (_body < 0)
        {
            _body = _fx.MaterialFind(BodyMaterial);
            _flicker = _fx.MaterialFind(FlickerMaterial);
        }

        Vector origin = _entities.GetOrigin(Handle);
        CurrentViewState eye = _view.View();
        Span<FxVertex> quad = stackalloc FxVertex[4];

        // DrawFlicker: the glow.
        float glow = _random.RandomFloat(0.2f, 0.3f);
        EffectQuads.Halo(quad, origin, State.Radius * _random.RandomFloat(1.5f, 2.5f), new Vector(glow, glow, glow), eye);
        _fx.DrawQuads(_flicker, quad, byteColor: false);

        // DrawHaloOriented: the body.
        EffectQuads.HaloOriented(quad, origin, BodyScale(State.Radius, _clock.Now), new Vector(1f, 1f, 1f), _spawnTime, eye);
        _fx.DrawQuads(_body, quad, byteColor: false);
        return 1;
    }
}
