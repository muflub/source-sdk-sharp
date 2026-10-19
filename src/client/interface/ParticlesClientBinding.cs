using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// IShimParticlesClient: the executor's lifetime, which managed decides
/// (material, cull radius, whether it simulates out of view).
/// </summary>
public interface IClientParticleExecutor
{
    bool Create(EntityHandle owner, int channel, string material, float cullRadius, bool alwaysSimulate);

    /// <summary>
    /// An executor for an effect with no owner (lane c5-particles), placed once
    /// at <paramref name="origin"/>, keyed by the no-owner handle and
    /// <paramref name="id"/>; Configure, SetLights and Destroy take the same pair.
    /// </summary>
    bool CreateAt(int id, string material, Vector origin, float cullRadius, bool alwaysSimulate);

    void Configure(EntityHandle owner, int channel, float cullRadius);

    void Destroy(EntityHandle owner, int channel);

    /// <summary>
    /// The sphere renderer's lights (a lit sim, the smoke stack's): base colour
    /// 0..1, then the ambient and the directional light, each position, colour
    /// and intensity: seventeen floats.
    /// </summary>
    void SetLights(EntityHandle owner, int channel, ReadOnlySpan<float> lights);

    /// <summary>
    /// One integer var of the executor's material (the lit sims ask for
    /// <c>$USINGPIXELSHADER</c>, as CParticleSphereRenderer::Init did): false
    /// when the material has no such var.
    /// </summary>
    bool MaterialVarInt(EntityHandle owner, int channel, string var, out int value);

    /// <summary>
    /// The executor's space: <see cref="ParticleSpace"/> flags over an entity's
    /// attachment (stock's <c>CLocalSpaceEmitter</c>, <c>FLE_VIEWMODEL</c>,
    /// <c>SetDrawBeforeViewModel</c>).
    /// </summary>
    /// <returns>The follow native read for an attachment's space (Followed 0 for none).</returns>
    ParticleFollow SetSpace(EntityHandle owner, int channel, uint entity, int attachment, ParticleSpace flags);
}

/// <summary>Native's executor itself, which the managed manager's executor wraps (C7 particles phase 2.3).</summary>
public interface INativeParticleExecutor : IClientParticleExecutor;

/// <summary>Where an executor's particles live and draw (IShimParticlesClient::ParticlesSetSpace).</summary>
[Flags]
public enum ParticleSpace
{
    World = 0,

    /// <summary>In the entity attachment's space, followed each frame (<c>CLocalSpaceEmitter</c>).</summary>
    Attachment = 1,

    /// <summary>Squashed for the view model's FOV (<c>FLE_VIEWMODEL</c>).</summary>
    ViewModel = 2,

    /// <summary>Drawn before the view model (<c>SetDrawBeforeViewModel</c>).</summary>
    BeforeViewModel = 4,

    /// <summary>
    /// A singleton (particles_ez.cpp's <c>InitSingleton</c>): not drawn through
    /// the leaf system but at <c>DrawParticleSingletons</c>' place in the
    /// translucent pass (the render plan's ParticleSingletons op).
    /// </summary>
    Singleton = 8,

    /// <summary>With <see cref="Singleton"/>: the 3D sky's call (<c>bInSkybox</c>).</summary>
    SingletonSky = 16,
}

/// <summary>
/// A world trace that answers the hit plane (normal and distance) as well as
/// the fraction: <c>UTIL_TraceLine</c> with no pass entity, as the smoke
/// volume's wall fade reads <c>plane.dist</c>.
/// </summary>
public interface IClientPlaneTrace
{
    (float Fraction, Vector Normal, float PlaneDist) TraceLine(Vector start, Vector end, int mask);
}

/// <summary>A precached model's path by index, as <c>modelinfo-&gt;GetModelName</c> answers it; null for none.</summary>
public interface IClientModelNames
{
    string? NameOf(int modelIndex);

    /// <summary><c>modelinfo-&gt;GetModelIndex( name )</c>: -1 for a model not loaded.</summary>
    int IndexOf(string name);
}

/// <summary>
/// The native text renderer's lifetime, which managed decides: made on the
/// first update with the font page and the local bounds managed measured,
/// re-configured on each later one, released with the entity.
/// </summary>
public interface IClientWorldTextMeshes
{
    bool Update(EntityHandle owner, string material, Vector mins, Vector maxs);

    void Destroy(EntityHandle owner);
}

/// <summary>
/// The client wind's publication (lane c2-sims): the head wind's vector, which
/// native's GetWindspeedAtTime answers to the ropes, the dust, the simple
/// particles and the temp-entity pool; the controller count; and, when a wind
/// thought this frame, the tree-sway vector for the shaders.
/// </summary>
public interface IClientWindPublisher
{
    void Publish(Vector wind, Vector? sway, int winds);
}
