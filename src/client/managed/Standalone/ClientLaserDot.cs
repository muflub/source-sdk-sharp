//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of env_laserdot: stock's C_LaserDot::DrawModel
/// (shared/hl2mp/weapon_rpg.cpp) — sprites/redglow1 drawn as a camera-facing
/// quad sixteen units across (give or take four, a flutter every frame), white,
/// in the translucent pass, whenever the dot exists.
///
/// <para>
/// WHERE. For the local player's own dot it is re-traced every frame from the
/// view being drawn along its forward, MASK_SHOT, past the owner, and backed
/// four units off the face it stopped on — so the dot sits under the
/// crosshair rather than a network update behind it, as stock's does. For
/// anybody else's dot it is drawn where the server put it, which is stock's
/// own fallback for an owner it cannot predict: stock re-traces those from
/// the owner's eye along GetAnimEyeAngles, and this client has no other
/// player's eye pitch (see docs/divergences.md).
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.EnvLaserdot)]
public sealed class ClientLaserDot : ClientStandaloneEntity
{
    public ClientLaserDot(IEngineEntityRender render, IFxRender fx, IPixelVisSlots view,
                          IEngineWorldQuery world, IUniformRandom random)
        : base(render)
    {
        _fx = fx;
        _view = view;
        _world = world;
        _random = random;
    }

    private readonly IFxRender _fx;
    private readonly IPixelVisSlots _view;
    private readonly IEngineWorldQuery _world;
    private readonly IUniformRandom _random;

    /// <summary>RPG_LASER_SPRITE, as OnDataChanged's m_hSpriteMaterial.Init binds it.</summary>
    internal const string SpriteMaterial = "sprites/redglow1";

    /// <summary>MAX_TRACE_LENGTH.</summary>
    private const float MaxTrace = 56756f;

    /// <summary>
    /// The renderer's cull box about the networked origin: the drawn point is
    /// the local re-trace, which can lead the networked one, so the box is
    /// generous rather than the dot's own eight units.
    /// </summary>
    private const float CullHalf = 64f;

    private int _material;

    public override string ClassName => EntityNames.EnvLaserdot;

    [NetState] public LaserDotNetState State = new();

    /// <summary>
    /// OnDataChanged: the material once, and the translucent pass
    /// (RENDER_GROUP_TRANSLUCENT_ENTITY, IsTransparent).
    /// </summary>
    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        if (_material == 0)
            _material = _fx.MaterialFind(SpriteMaterial);

        Render.SetCustomDraw(Handle, new Vector(-CullHalf, -CullHalf, -CullHalf), new Vector(CullHalf, CullHalf, CullHalf));
        Render.SetBlending(Handle, translucent: true, ignoreZ: false, alpha: 255);
    }

    public override void OnRemoved()
    {
        if (_material != 0)
            _fx.MaterialRelease(_material);
        _material = 0;
    }

    /// <summary>DrawModel: the dot where the owner's aim ends, and the flutter.</summary>
    public override int OnDraw()
    {
        if (_material == 0)
            return 0;

        CurrentViewState view = _view.View();
        int owner = State.OwnerIndex;
        Vector end;

        if (owner > 0 && owner == _fx.LocalPlayer())
        {
            ClientTraceResult tr = _world.ClientTraceLine(view.Origin, view.Origin + view.Forward * MaxTrace,
                                                          TraceMask.Shot, TraceFilter.Simple, owner);
            end = tr.End + tr.Normal * 4f;
        }
        else
        {
            end = Entities.GetOrigin(Handle);
        }

        float scale = 16f + _random.RandomFloat(-4f, 4f);

        Span<FxVertex> quad = stackalloc FxVertex[4];
        SpriteQuad(quad, end, scale, scale, view.Origin, view.Up, view.Right);
        _fx.DrawQuads(_material, quad, byteColor: true);
        return 1;
    }

    /// <summary>
    /// beamdraw.cpp's DrawSprite: a quad <paramref name="width"/> by
    /// <paramref name="height"/> about <paramref name="origin"/>, turned to
    /// face the view (its right is the view's up crossed with the way back to
    /// the eye), white and opaque.
    /// </summary>
    internal static void SpriteQuad(Span<FxVertex> quad, Vector origin, float width, float height,
                                    Vector viewOrigin, Vector viewUp, Vector viewRight)
    {
        width *= 0.5f;
        height *= 0.5f;

        Vector right = new(1, 0, 0), up = new(0, 1, 0);
        Vector toEye = viewOrigin - origin;

        if (toEye.Length >= 1e-3f)
        {
            Vector fwd = toEye.Normalized;
            Vector r = viewUp.Cross(fwd);

            if (r.Length >= 1e-3f)
            {
                right = r.Normalized;
                up = fwd.Cross(right);
            }
            else
            {
                up = fwd.Cross(viewRight).Normalized;
                right = up.Cross(fwd);
            }
        }

        Vector n = Vector.Zero;
        quad[0] = new FxVertex(origin - up * height - right * width, 0f, 1f, 255f, 255f, 255f, 255f, n);
        quad[1] = new FxVertex(origin + up * height - right * width, 0f, 0f, 255f, 255f, 255f, 255f, n);
        quad[2] = new FxVertex(origin + up * height + right * width, 1f, 0f, 255f, 255f, 255f, 255f, n);
        quad[3] = new FxVertex(origin - up * height + right * width, 1f, 1f, 255f, 255f, 255f, 255f, n);
    }
}
