//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.ClientSystems;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Render.GlowOverlays;

/// <summary>
/// <see cref="IGlowEngine"/> as the game answers it: a material by name and
/// group through IShimViewClient's GlowMaterialFind (no reference taken, as
/// stock took none), and the solid line through the client trace
/// (CONTENTS_SOLID, the simple filter with no pass entity, which is what
/// UTIL_TraceLine( ..., NULL, COLLISION_GROUP_NONE ) builds).
/// </summary>
public sealed class ClientGlowEngine(IGlowMaterials materials, IEngineWorldQuery world) : IGlowEngine
{
    public int FindMaterial(string name, string group) => materials.Find(name, group);

    public void TraceSolid(in Vector start, in Vector end, out float fraction, out int surfaceFlags)
    {
        ClientTraceResult tr = world.ClientTraceLine(start, end, TraceMask.ContentsSolid, TraceFilter.Simple);
        fraction = tr.Fraction;
        surfaceFlags = tr.SurfaceFlags;
    }
}

// glow_overlay.cpp's ConVars, declared here since the file left client.so (C7):
// names, defaults and flags as stock declared them (GlowOverlayConVarOracleTests
// holds each to the pristine file). Its third, building_cubemaps, is the
// engine's and is read by name (PixelVisibility.BuildingCubemapsWatch).

/// <summary>cl_ShowSunVectors: declared by stock and read by nothing.</summary>
[ConVar("cl_ShowSunVectors", "0")]
public sealed class ShowSunVectorsConVar : BoolConVar { }

/// <summary>cl_sun_decay_rate: how fast an overlay's obstruction fades when the card cannot answer.</summary>
[ConVar("cl_sun_decay_rate", "0.05", Flags = ConVarFlags.Cheat)]
public sealed class SunDecayRateConVar : FloatConVar { }

/// <summary>
/// The overlays' ConVars: building_cubemaps (the engine's, watched) and
/// cl_sun_decay_rate (the declaration above); r_drawsprites and the wireframe
/// question come with each draw's view.
/// </summary>
public sealed class GlowSettings(Func<bool> buildingCubemaps, Func<float> sunDecayRate) : IGlowSettings
{
    public bool BuildingCubemaps => buildingCubemaps();

    public float SunDecayRate => sunDecayRate();

    public int DrawSprites { get; private set; } = 1;

    public bool Wireframe { get; private set; }

    /// <summary>The draw's own two facts, as native read them for it.</summary>
    public void SetDraw(int drawSprites, bool wireframe)
    {
        DrawSprites = drawSprites;
        Wireframe = wireframe;
    }
}

/// <summary>
/// What native's glow overlay ops reach (IManagedGlowClient): the view parsed,
/// the system's draw written as shell_glow_client.h's command words.
/// </summary>
public sealed class GlowHost(GlowOverlaySystem system, GlowSettings settings) : IClientGlowHost
{
    /// <summary>A quad's words: kind, material and four (position, colour, s, t) (SS_GLOW_QUAD_WORDS).</summary>
    public const int QuadWords = 2 + 4 * 8;

    /// <summary>A wireframe quad's words (SS_GLOW_WIREQUAD_WORDS).</summary>
    public const int WireQuadWords = 2 + 4 * 3;

    /// <summary>The view native hands over (SS_GLOW_VIEW_FLOATS).</summary>
    public const int ViewFloats = 29;

    private readonly Writer _writer = new();

    /// <summary>Draws asked since the load.</summary>
    public long Draws { get; private set; }

    public int DrawOverlays(ReadOnlySpan<float> view, bool cacheFullSceneState, Span<int> commands)
    {
        _writer.Words.Clear();
        if (view.Length < ViewFloats)
            return 0;
        var planes = new GlowPlane[5];
        for (int i = 0; i < 5; i++)
            planes[i] = new GlowPlane(new Vector(view[7 + i * 4], view[8 + i * 4], view[9 + i * 4]), view[10 + i * 4]);
        var v = new GlowView(new Vector(view[0], view[1], view[2]), new Vector(view[3], view[4], view[5]), view[6], planes);
        settings.SetDraw((int)view[28], view[27] != 0);
        system.DrawOverlays(v, cacheFullSceneState, _writer);
        Draws++;
        return Commands(commands);
    }

    public int Commands(Span<int> commands)
    {
        int total = _writer.Words.Count;
        if (total <= commands.Length)
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_writer.Words).CopyTo(commands);
        return total;
    }

    public void UpdateSkyOverlays(in Vector origin, float zFar, bool cacheFullSceneState)
        => system.UpdateSkyOverlays(origin, zFar, cacheFullSceneState);

    private sealed class Writer : IGlowDraw
    {
        public readonly List<int> Words = [];

        private void F(float f) => Words.Add(BitConverter.SingleToInt32Bits(f));

        public void Clip(GlowClip clip)
        {
            Words.Add((int)GlowCommand.Clip);
            Words.Add((int)clip);
        }

        public void HdrColorScale(int material, float scale)
        {
            Words.Add((int)GlowCommand.HdrScale);
            Words.Add(material);
            F(scale);
        }

        public void Quad(int material, ReadOnlySpan<GlowVertex> vertices)
        {
            Words.Add((int)GlowCommand.Quad);
            Words.Add(material);
            for (int i = 0; i < 4; i++)
            {
                GlowVertex v = i < vertices.Length ? vertices[i] : default;
                F(v.Position.X); F(v.Position.Y); F(v.Position.Z);
                F(v.Color.X); F(v.Color.Y); F(v.Color.Z);
                F(v.S); F(v.T);
            }
        }

        public void WireframeQuad(int material, ReadOnlySpan<Vector> positions)
        {
            Words.Add((int)GlowCommand.WireQuad);
            Words.Add(material);
            for (int i = 0; i < 4; i++)
            {
                Vector p = i < positions.Length ? positions[i] : default;
                F(p.X); F(p.Y); F(p.Z);
            }
        }
    }
}

/// <summary>kClientSystems[]'s "CGlowOverlaySystem" row: the level's end deletes every overlay.</summary>
public sealed class GlowOverlayClientSystem(GlowOverlaySystem system) : ManagedClientSystem
{
    public override string Name => "CGlowOverlaySystem";

    public override void LevelShutdownPostEntity() => system.LevelShutdownPostEntity();
}

/// <summary>
/// The fireball flash (c_te_explosion.cpp's C_TEExplosion: one CExplosionOverlay)
/// and the gunship impact's overlay (fx.cpp's FX_GunshipImpact: one CImpactOverlay),
/// made as their files made them and handed to the overlay list, which owns
/// them from then on.
/// </summary>
public sealed class GlowOverlayCreators(GlowOverlaySystem system) : IExplosionFireball, IFxImpactOverlay
{
    /// <summary>The explosion's: a twentieth of a unit wide, half that high, warm white.</summary>
    public void Fireball(Vector origin)
    {
        var o = new RampOverlay(RampOverlay.ExplosionGrowth) { Lifetime = 0, Pos = origin, NSprites = 1 };
        o.BaseColors[0] = new Vector(1.0f, 0.9f, 0.7f);
        o.Sprites[0].HorzSize = 0.05f;
        o.Sprites[0].VertSize = o.Sprites[0].HorzSize * 0.5f;
        system.Activate(o);
    }

    /// <summary>The impact's: a hundredth of a unit, in the colour the effect chose.</summary>
    public void ImpactOverlay(Vector position, Vector color)
    {
        var o = new RampOverlay(RampOverlay.ImpactGrowth) { Lifetime = 0, Pos = position, NSprites = 1 };
        o.BaseColors[0] = color;
        o.Sprites[0].HorzSize = 0.01f;
        o.Sprites[0].VertSize = 0.01f;
        system.Activate(o);
    }

    /// <summary>The RPG shot down's (c_smoke_trail.cpp's RPGShotDownCallback): a hundredth of a unit wide, half that high.</summary>
    public void ShotDown(Vector origin)
    {
        var o = new RampOverlay(RampOverlay.ExplosionGrowth) { Lifetime = 0, Pos = origin, NSprites = 1 };
        o.BaseColors[0] = new Vector(1.0f, 0.9f, 0.7f);
        o.Sprites[0].HorzSize = 0.01f;
        o.Sprites[0].VertSize = o.Sprites[0].HorzSize * 0.5f;
        system.Activate(o);
    }
}

/// <summary>
/// The <c>RPGShotDown</c> client effect (c_smoke_trail.cpp's callback, which this
/// build kept in shell_explosion_overlay.cpp): Missile.ShotDown at the point for
/// the local player, from the world, and the flash. Nothing in this build sends
/// it (its sender is hl2's weapon_rpg); it is ported, not dropped.
/// </summary>
public sealed class RpgShotDownEffect(IFxGameSounds sounds, GlowOverlayCreators overlays) : IClientEffectHandler
{
    public string EffectName => "RPGShotDown";

    public void Dispatch(in EffectData data)
    {
        if (sounds.Parameters("Missile.ShotDown", out GameSoundParams p))
            sounds.EmitAtPosition(p, p.Volume, p.Pitch, data.Origin);
        overlays.ShotDown(data.Origin);
    }
}
