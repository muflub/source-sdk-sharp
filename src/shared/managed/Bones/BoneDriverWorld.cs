//========= SourceSharp — what the bone drivers ask the engine ================//
//
// The engine-side facts ClientBoneDriver and ServerBoneDriver need and cannot
// compute (plan physics_bones_native_zero step 1.0b). Each is something the
// native shells reach through an engine interface today -- the model cache,
// the ragdoll table, the client entity list -- and each is an interface here
// so the facts can fake it. Convars and the clock are not in here: they
// arrive as arguments (BoneConVars, currentTime).
//
// The inputs a frame reads off the entity's engine block (where it is, which
// model it wears, whether it is the local viewmodel) arrive as a plain
// struct per call: ClientBonePose, ServerBonePose.
//=============================================================================//

using System;
using Vector = System.Numerics.Vector3;

namespace SourceSharp.Shared.Bones;

/// <summary>The model cache, as the bone drivers read it.</summary>
public interface IBoneModels
{
    /// <summary>
    /// Whether the model has studio data to ask at all
    /// (<c>modelinfo->GetCacheHandle(GetModel()) != MDLHANDLE_INVALID</c>).
    /// A brush model and a model-less beam answer false.
    /// </summary>
    bool HasStudioModel(int modelIndex);

    /// <summary>
    /// The model's studio header (<c>mdlcache->GetStudioHdr</c> +
    /// <c>CStudioHdr::Init</c> + <c>IsValid</c>), or null when any step
    /// fails: the header can be momentarily unreadable during a model load.
    /// </summary>
    StudioHdr? OpenStudioHdr(int modelIndex);
}

/// <summary>A bone-merge source, as the client entity list resolves it.</summary>
public readonly struct BoneMergeSourceView
{
    public BoneMergeSourceView(bool isSelf, nint model)
    {
        IsSelf = isSelf;
        Model = model;
    }

    /// <summary>The source's renderable IS the follower (a self-merge).</summary>
    public bool IsSelf { get; }

    /// <summary>The source's <c>model_t</c>, as an opaque handle; 0 when
    /// nothing networked at that index has a renderable with a model.</summary>
    public nint Model { get; }
}

/// <summary>What the client driver asks the engine beyond the model cache.</summary>
public interface IClientBoneWorld : IBoneModels
{
    /// <summary><c>RagdollLibrary_IsBody</c>: whether the ragdoll table still
    /// holds this body (a level change clears the table under a held id).</summary>
    bool IsRagdollBody(uint ragdoll);

    /// <summary>
    /// <c>RagdollLibrary_WriteBones</c>: the solver's bones written into
    /// <paramref name="bones"/>, and their indices into
    /// <paramref name="ownedBones"/>. Answers how many it owns.
    /// </summary>
    int WriteRagdollBones(uint ragdoll, BoneMatrix3x4[] bones, Span<int> ownedBones);

    /// <summary>The bone-merge source at <paramref name="entIndex"/>
    /// (the client entity at that index → renderable → model).</summary>
    BoneMergeSourceView ResolveMergeSource(int entIndex);

    /// <summary>A <c>model_t</c>'s studio header, or null (see
    /// <see cref="IBoneModels.OpenStudioHdr"/>).</summary>
    StudioHdr? OpenStudioHdrForModel(nint model);

    /// <summary>
    /// The source's <c>IClientRenderable::SetupBones</c>: its bone-to-world
    /// into <paramref name="bones"/> (MAXSTUDIOBONES long). May re-enter the
    /// follower's own build when two entities merge onto each other.
    /// </summary>
    bool SourceSetupBones(int entIndex, BoneMatrix3x4[] bones, int boneMask, float currentTime);

    /// <summary>A verbosity-gated diagnostic line (<c>Msg</c>).</summary>
    void Say(string message);
}

/// <summary>The convars bone_setup and the jiggle springs read, as data.</summary>
public readonly struct BoneConVars
{
    public BoneConVars(bool animThreeWayBlend, float jiggleFramerateCutoff, int verbosity)
    {
        AnimThreeWayBlend = animThreeWayBlend;
        JiggleFramerateCutoff = jiggleFramerateCutoff;
        Verbosity = verbosity;
    }

    /// <summary>Every convar at its stock default, verbosity 0.</summary>
    public static BoneConVars Stock => new(true, JiggleBones.DefaultFramerateCutoff, 0);

    /// <summary><c>anim_3wayblend</c> (default 1).</summary>
    public bool AnimThreeWayBlend { get; }

    /// <summary><c>cl_jiggle_bone_framerate_cutoff</c> (default 20).</summary>
    public float JiggleFramerateCutoff { get; }

    /// <summary><c>SourceSharp_GetVerbosity()</c>: above 0, the merge says what it did.</summary>
    public int Verbosity { get; }
}

/// <summary>What a client bone build reads off the entity's engine block.</summary>
public struct ClientBonePose
{
    /// <summary>Block().Head.ModelIndex.</summary>
    public int ModelIndex;

    /// <summary>Block().Client.ViewModel != 0: the local viewmodel pass.</summary>
    public bool ViewModel;

    /// <summary>Block().Head.Origin / Angles: the networked pose.</summary>
    public Vector NetOrigin;
    public BoneAngle NetAngles;

    /// <summary>Block().Client.RenderOrigin / RenderAngles: the drawn pose.</summary>
    public Vector RenderOrigin;
    public BoneAngle RenderAngles;

    /// <summary><c>gpGlobals->framecount</c>.</summary>
    public int FrameCount;

    /// <summary>
    /// Block().Head.InterpolationFrame: stock's <c>m_ubInterpolationFrame</c>,
    /// the counter a teleport bumps. The transitioner reads a move of it as
    /// <c>IsNoInterpolationFrame()</c>.
    /// </summary>
    public int InterpolationFrame;
}

/// <summary>What a server bone build reads off the entity.</summary>
public struct ServerBonePose
{
    /// <summary><c>GetModelIndex()</c>.</summary>
    public int ModelIndex;

    /// <summary><c>GetAbsOrigin()</c> / <c>GetAbsAngles()</c>.</summary>
    public Vector AbsOrigin;
    public BoneAngle AbsAngles;
}
