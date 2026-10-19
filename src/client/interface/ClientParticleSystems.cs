using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>One CNewParticleEffect the client started, with its system's name.</summary>
public readonly record struct ClientParticleSystemRow(string Name, ParticleSystemInfo Info);

/// <summary>
/// The CNewParticleEffect instances the client started, read through
/// IShimParticlesClient (plan_client_novalve §4.4, the particles route; the
/// particles shell, shell_particles_client.cpp). Game thread.
/// </summary>
public interface IClientParticleSystems
{
    /// <summary>Every instance the particles shell watches, in the order it started them.</summary>
    IReadOnlyList<ClientParticleSystemRow> Read();

    /// <summary>Full reads of the particle manifest's definitions since the process started.</summary>
    int DefinitionReads();

    /// <summary>
    /// The particle query's calls that found no managed answer, per method:
    /// collision group, lighting, trace, hit-box points, hit-box info. Empty
    /// when the read is not wired.
    /// </summary>
    IReadOnlyList<int> QueryUnanswered();
}

/// <summary>
/// The particles shell's starts for a dispatched particle effect (lane
/// c5-particles): a system's name by its string-table index, and a one-shot
/// started at a world point with the control points the dispatch named.
/// </summary>
public interface IClientParticleOneShots
{
    /// <summary><c>GetParticleSystemNameFromIndex</c>: the ParticleEffectNames table's string, null with no table.</summary>
    string? NameFromIndex(int index);

    /// <summary>
    /// <c>CNewParticleEffect::Create( NULL, name )</c>, its sort origin, the
    /// control points in order, and control point 0's orientation; false when
    /// no definition answers the name.
    /// </summary>
    bool Start(string name, Vector sortOrigin, ReadOnlySpan<ParticleControlPoint> points,
               Vector forward, Vector right, Vector up);
}

/// <summary>One control point a dispatch sets: its number and position.</summary>
public readonly record struct ParticleControlPoint(int Point, Vector Position);

/// <summary>A world trace's answer, as much of <c>trace_t</c> as the particle collision reads.</summary>
public struct ParticleTrace
{
    public float Fraction;
    public Vector EndPos;
    public Vector Normal;
    public float Dist;
    public bool AllSolid;
    public bool StartSolid;
}

/// <summary>
/// <c>UTIL_TraceLine( start, end, MASK_SOLID_BRUSHONLY, NULL,
/// COLLISION_GROUP_NONE )</c>: the only trace the particle collision makes.
/// </summary>
public interface IParticleWorldTrace
{
    ParticleTrace TraceLine(Vector start, Vector end);
}

/// <summary>What a masked line found: fraction, end, the plane's normal, and stock's startsolid / fractionleftsolid.</summary>
public readonly record struct MaskTrace(float Fraction, Vector End, Vector Normal, bool StartSolid, float FractionLeftSolid);

/// <summary><c>UTIL_TraceLine( start, end, mask, NULL, COLLISION_GROUP_NONE )</c> with the contents mask the caller names.</summary>
public interface IParticleMaskTrace
{
    /// <summary><c>CONTENTS_SOLID</c>.</summary>
    public const uint ContentsSolid = 0x1;

    /// <summary><c>MASK_WATER</c>: <c>CONTENTS_WATER | CONTENTS_MOVEABLE | CONTENTS_SLIME</c>.</summary>
    public const uint MaskWater = 0x20 | 0x4000 | 0x10;

    MaskTrace Trace(Vector start, Vector end, uint mask);
}

/// <summary>
/// The view the particle manager is drawing (lane c5-particles): its
/// model-view matrix's three rows, row major (<c>ParticleMgr()-&gt;GetModelView()</c>),
/// <c>CurrentViewID()</c>, and the view's origin and forward.
/// </summary>
public readonly struct ParticleView
{
    /// <summary>The model-view matrix's three rows, row major: twelve floats. Null for no view.</summary>
    public float[]? ModelView { get; init; }

    /// <summary><c>CurrentViewID()</c>.</summary>
    public int ViewId { get; init; }

    /// <summary><c>CurrentViewOrigin()</c>.</summary>
    public Vector Origin { get; init; }

    /// <summary><c>CurrentViewForward()</c>.</summary>
    public Vector Forward { get; init; }
}

/// <summary>Reads the view the particle manager is drawing (IShimParticlesClient::ParticlesView).</summary>
public interface IParticleViewSource
{
    ParticleView Read();
}

/// <summary><c>PixelVisibility_FractionVisible</c> at a point through the caller's query handle.</summary>
public interface IParticlePixelVisibility
{
    float FractionVisible(Vector origin, ref int queryHandle);
}

/// <summary><c>g_pParticleSystemMgr-&gt;ParticleThrottleScaling()</c>, asked of the particle library: 1 unrestricted .. 0 fully.</summary>
public interface IParticleThrottle
{
    float ThrottleScaling();
}

/// <summary>
/// The contents at a point inside a managed brush entity's own brush
/// (IShimParticlesClient::ParticlesBrushPointContents): the engine's point test
/// over the owner as stock's SOLID_BSP dust volume (lane c5-particles: the dust
/// motes' spawn test). 0 when nothing answers.
/// </summary>
public interface IClientBrushContents
{
    int PointContents(EntityHandle owner, Vector point);
}
