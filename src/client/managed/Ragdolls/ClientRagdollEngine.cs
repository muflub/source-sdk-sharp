using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Shared.Bones;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// game/client/ragdoll.cpp's <c>ragdoll_sleepaftertime</c>, which moved with
/// the settle detector that reads it (name, default and help as stock spelled
/// them: docs/novalve-snapshot's client convar contract).
/// </summary>
[ConVar("ragdoll_sleepaftertime", "5.0f",
        Help = "After this many seconds of being basically stationary, the ragdoll will go to sleep.")]
public sealed class RagdollSleepAfterTimeConVar : FloatConVar { }

/// <summary>
/// <see cref="IRagdollPoseSource"/> over the client's managed skeleton: the
/// model the entity's block names, its studio header, and the pose
/// <c>BONE_USED_BY_ANYTHING</c> builds (what C_StandaloneEntity::BecomeRagdoll
/// asked SetupBones for).
/// </summary>
public sealed class ClientRagdollPoses(ClientSkeletons skeletons, IStandaloneBlocks blocks) : IRagdollPoseSource
{
    public RagdollPose Pose(EntityHandle owner)
    {
        Span<byte> span = blocks.Block(owner);

        if (span.Length < EngineBlock.SizeBytes)
            return default;

        EngineBlock block = MemoryMarshal.Read<EngineBlock>(span);

        if (block.Head.Handle != owner.Value)
            return default;

        int model = block.Head.ModelIndex;

        // The entity's own place (origin and angles, no scale): where a bone
        // no mask wrote is put from.
        BoneMatrix3x4 entityToWorld = BoneSetupMath.AngleMatrix(
            new BoneAngle(block.Head.Angles.Pitch, block.Head.Angles.Yaw, block.Head.Angles.Roll),
            new NVector(block.Head.Origin.X, block.Head.Origin.Y, block.Head.Origin.Z));

        StudioHdr? hdr = model > 0 ? skeletons.Models.OpenStudioHdr(model) : null;

        if (hdr is null)
            return new RagdollPose(model, null, null, entityToWorld);

        StandaloneBones? bones = skeletons.BoneMatrices(owner);
        BoneMatrix3x4[]? matrices = bones is null
            ? null
            : MemoryMarshal.Cast<float, BoneMatrix3x4>(bones.BoneToWorld.AsSpan()).ToArray();

        return new RagdollPose(model, hdr, matrices, entityToWorld);
    }

    public void BecameRagdoll(EntityHandle owner) => skeletons.BecameRagdoll(owner);
}

/// <summary><see cref="IRagdollClock"/> over the engine's globals.</summary>
public sealed class ClientRagdollClock(IEngineEnvironment environment) : IRagdollClock
{
    public float CurTime => environment.Globals.IsAvailable ? environment.Globals.CurTime : 0f;

    public int TickCount => environment.Globals.IsAvailable ? environment.Globals.TickCount : 0;
}

/// <summary><see cref="IRagdollWorld"/> over the client's trace, the sleep convar and <c>developer</c>.</summary>
public sealed class ClientRagdollWorld(IEngineWorldQuery world, IEngineEntities entities, IEngineConsole console,
                                       RagdollSleepAfterTimeConVar sleepAfterTime) : IRagdollWorld
{
    public bool TraceSolidHits(Vector start, Vector end, EntityHandle owner)
        => world.ClientTraceLine(start, end, TraceMask.Solid, TraceFilter.Simple, entities.GetIndex(owner)).Hit;

    public float SleepAfterTime => sleepAfterTime.Value;

    public int Developer => int.TryParse(console.FindConVarValue("developer"), out int level) ? level : 0;
}

/// <summary>
/// The model names a body's objects carry (<c>objectparams_t::pName</c>, which
/// vphysics keeps): one native copy per model, freed when no body stands.
/// </summary>
/// <remarks>
/// Nothing frees them at a module unload: a reload leaves the bodies' objects
/// holding these names, and the next build adopts them (ClientRagdolls'
/// reload section).
/// </remarks>
public sealed class ClientRagdollNames : IRagdollNames
{
    private readonly Dictionary<string, nint> _held = [];

    public IReadOnlyDictionary<string, nint> Held => _held;

    public nint NameOf(string name)
    {
        if (!_held.TryGetValue(name, out nint pointer))
            _held[name] = pointer = Marshal.StringToCoTaskMemUTF8(name);

        return pointer;
    }

    public void Adopt(string name, nint pointer) => _held[name] = pointer;

    public void ReleaseAll()
    {
        foreach (nint pointer in _held.Values)
            Marshal.FreeCoTaskMem(pointer);

        _held.Clear();
    }
}
