using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// One vertex of a client-side effect's quad, as stock's <c>CMeshBuilder</c>
/// calls handed it over: position, texture coordinate, colour (as the floats
/// <c>Color4fv</c> takes, or the byte values <c>Color4ub</c> takes, per draw),
/// normal. Thirteen floats, blittable, crossed by pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FxVertex(Vector position, float s, float t, float r, float g, float b, float a, Vector normal)
{
    public Vector Position = position;
    public float S = s, T = t;
    public float R = r, G = g, B = b, A = a;
    public Vector Normal = normal;
}

/// <summary>What a client-side effect's draw reads about the view it is drawn into.</summary>
public readonly record struct FxView(Vector Origin, Vector Forward, int ScreenWidth);

/// <summary>
/// The client-side effects' native carry-out (plan_client_novalve §2 cluster 3):
/// materials by integer id (native holds the <c>IMaterial</c>), one quad-mesh
/// draw, and the reads the decisions need. Nothing behind it decides.
/// </summary>
public interface IFxRender
{
    /// <summary><c>materials-&gt;FindMaterial( name, TEXTURE_GROUP_CLIENT_EFFECTS )</c> and one reference; the id is native's.</summary>
    int MaterialFind(string name);

    /// <summary><c>IncrementReferenceCount</c> on a material id.</summary>
    void MaterialAddRef(int material);

    /// <summary><c>DecrementReferenceCount</c> on a material id.</summary>
    void MaterialRelease(int material);

    /// <summary>The material's name (<c>IMaterial::GetName</c>), or empty.</summary>
    string MaterialName(int material);

    /// <summary>
    /// One dynamic mesh of <c>MATERIAL_QUADS</c>, four vertices a quad, drawn
    /// with the material. <paramref name="byteColor"/> says whether each colour
    /// goes through <c>Color4ub</c> (the values are bytes) or <c>Color4fv</c>.
    /// </summary>
    void DrawQuads(int material, ReadOnlySpan<FxVertex> vertices, bool byteColor);

    /// <summary><c>CurrentViewOrigin()</c>, <c>CurrentViewForward()</c> and <c>ScreenWidth()</c> now.</summary>
    FxView View();

    /// <summary><c>MainViewOrigin()</c>: where the listener is.</summary>
    Vector MainViewOrigin();


    /// <summary><c>engine-&gt;GetLocalPlayer()</c>: the viewer's slot, or 0 and below before there is one.</summary>
    int LocalPlayer();

    /// <summary><c>engine-&gt;GetViewAngles()</c>.</summary>
    QAngle ViewAngles();

    /// <summary>
    /// An entity's attachment, as <c>IClientRenderable::GetAttachment</c> answers
    /// it: -1 when the entity has no renderable, 0 when the attachment is not on
    /// its model (<paramref name="model"/> names the model, and
    /// <paramref name="origin"/> is what <c>GetAttachment</c> wrote for the
    /// miss, the entity's own origin), 1 with <paramref name="origin"/> the
    /// attachment's.
    /// </summary>
    int EntityAttachment(int entIndex, int attachment, out Vector origin, out string model);

    /// <summary>
    /// <c>DispatchParticleEffect( systemIndex, start, end, angles, entity )</c>:
    /// a new-style particle system by its precache index, from start to end,
    /// owned by the entity at <paramref name="entIndex"/> (-1 for none).
    /// </summary>
    void DispatchParticleEffect(int systemIndex, Vector start, Vector end, QAngle angles, int entIndex);

    /// <summary>
    /// <c>GetRenderAngles()</c> of the entity at <paramref name="entIndex"/>'s
    /// renderable; false (and no angles) when it has none.
    /// </summary>
    bool EntityRenderAngles(int entIndex, out QAngle angles);

    /// <summary><c>tempents-&gt;EjectBrass( origin, angles, gunAngles, type )</c>.</summary>
    void EjectBrass(Vector origin, QAngle angles, QAngle gunAngles, int type);
}

/// <summary>A tracer's near-miss sound (FX_TracerSound's emit), played by the managed client sound emitter.</summary>
public interface IFxTracerSound
{
    /// <summary>
    /// A tracer's near-miss: <c>GameSound_GetParameters( soundName )</c> and, if
    /// it has parameters, <c>EmitSound</c> to the local player from the world at
    /// <paramref name="start"/> along <paramref name="direction"/>. Answers whether
    /// the sound had parameters.
    /// </summary>
    bool EmitTracerSound(string soundName, Vector start, Vector direction);
}
