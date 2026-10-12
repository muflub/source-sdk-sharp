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
/// env_splash: a splash of water where a map asks for one.
///
/// <para>
/// The splash is one effect dispatch. What is a decision, and what this class
/// is, is the two questions its spawnflags ask before it: where the water
/// actually IS, and how big the splash should be given how deep it is. Both are
/// refusals as often as they are answers — an env_splash underwater makes none,
/// and one over water too deep to matter makes none either.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvSplash)]
public sealed class EnvSplash : ScriptedEntity
{
    public EnvSplash(IEngineEffects effects, IEngineWorldQuery world)
    {
        Effects = effects;
        World = world;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => (SpawnFlag)SpawnFlags;

    /// <summary>What a map may set on this entity. See <see cref="EnvSplashVars"/>.</summary>
    [ScriptVars] public EnvSplashVars Vars = new();

    /// <summary>
    /// How deep water has to be before a splash is full size.
    ///
    /// Stock's SPLASH_MAX_DEPTH. Deeper than this and the factor goes negative,
    /// which the floor below catches.
    /// </summary>
    private const float MaxDepth = 120f;

    /// <summary>
    /// Below this the splash is not worth making. Stock's, and it is a real
    /// decision rather than an epsilon: a tenth-scale splash in deep water is a
    /// speck, and stock chooses to draw nothing instead.
    /// </summary>
    private const float SmallestWorthMaking = 0.1f;

    /// <summary>How big, as the map asked. Test-visible.</summary>
    internal float Scale => Vars.Scale;

    /// <summary>
    /// env_splash's own spawnflags. Both are about how the splash meets the water:
    /// where its surface is, and how big the splash is once found.
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000,

        /// <summary>For water whose level changes: the mapper places the entity once and the splash follows the surface.</summary>
        FindSurface = 0b0000_0001,

        /// <summary>Get smaller in deeper water.</summary>
        Diminish = 0b0000_0010,
    }

    [ScriptedEvent("Splash")]
    private void OnSplash()
    {
        Vector origin = Origin;
        bool slime = false;

        if (Flags.HasFlag(SpawnFlag.FindSurface))
        {
            WaterBelow water = World.WaterSurfaceBelow(origin);

            // Underwater, so there is no surface to break. Stock's first check
            // and it is a refusal rather than a fallback: a splash from below
            // would be drawn inside the water looking up.
            if (water.Submerged)
                return;

            if (!water.Found)
                return;

            origin = water.Surface;
            slime = water.Slime;
        }

        float scale = Vars.Scale;

        if (Flags.HasFlag(SpawnFlag.Diminish))
        {
            float factor = DepthFactor(DepthBelow(origin));

            if (factor < SmallestWorthMaking)
                return;

            scale *= factor;
        }

        Effects.SplashEffect(origin, scale, slime);
    }

    /// <summary>
    /// How far it is from here to the bottom.
    ///
    /// <para>
    /// Brushes only, which is what makes this a depth rather than a distance to
    /// the nearest object: a crate floating in the pool is not the floor of it.
    /// </para>
    /// </summary>
    private float DepthBelow(Vector from)
    {
        TraceResult tr = World.TraceLine(from, from - new Vector(0f, 0f, 4096f),
                                          TraceMask.Brushes, Handle);

        return MathF.Abs(from.Z - tr.End.Z);
    }

    /// <summary>
    /// How much of a splash a given depth is worth: 1 at the surface, 0 at
    /// <see cref="MaxDepth"/>, and negative below that.
    ///
    /// <para>
    /// Not clamped, deliberately — the caller's floor is what rejects the small
    /// ones, and clamping here would turn "too deep to bother" into "as small as
    /// allowed", which is a visible splash where stock draws none.
    /// </para>
    /// <para>
    /// Static and taking the depth so it can be checked without an engine.
    /// </para>
    /// </summary>
    internal static float DepthFactor(float depth) => 1f - (depth / MaxDepth);
}

/// <summary>
/// The map keys <see cref="EnvSplash"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvSplashVars
{
    public float Scale { get; set; }
}
