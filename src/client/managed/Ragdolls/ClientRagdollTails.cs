using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// <see cref="IRagdollRenderTail"/> over the engine: the owner's engine block
/// by handle (one IShimStandaloneClient crossing), written in place, and the
/// shell's one store (StandaloneSetRagdoll).
/// </summary>
public sealed class ClientRagdollTails(IStandaloneBlocks blocks, IStandaloneRagdollStore store) : IRagdollRenderTail
{
    public bool Write(EntityHandle owner, Vector origin, Vector mins, Vector maxs)
    {
        Span<byte> span = blocks.Block(owner);

        if (span.Length < EngineBlock.SizeBytes)
            return false;

        ref EngineBlock block = ref MemoryMarshal.AsRef<EngineBlock>(span);

        // THE FULL HANDLE, not the entry: a dead owner whose index a live
        // entity took must not have the body's pose written into the living
        // one's tail (the native library's resolver compared the serial too).
        if (block.Head.Handle != owner.Value)
            return false;

        block.Client.RenderOrigin = origin;

        // A JOINTED BODY IS NOT ITS MODEL'S BOX: the cube of the model's
        // radius while it moves, the exact hull once it sleeps (CRagdoll).
        block.Client.RenderMins = mins;
        block.Client.RenderMaxs = maxs;

        // And the render transform, from the pose just written. The angles are
        // the tail's own: a body's rotation is in its bones, and stock's client
        // ragdoll does not turn the entity either.
        QAngle angles = block.Client.RenderAngles;
        BoneMatrix3x4 m = BoneSetupMath.AngleMatrix(new BoneAngle(angles.Pitch, angles.Yaw, angles.Roll),
                                                    new System.Numerics.Vector3(origin.X, origin.Y, origin.Z));
        block.Client.RenderTransform = new Transform(m.Row0.X, m.Row0.Y, m.Row0.Z, m.Row0.W,
                                                     m.Row1.X, m.Row1.Y, m.Row1.Z, m.Row1.W,
                                                     m.Row2.X, m.Row2.Y, m.Row2.Z, m.Row2.W);
        return true;
    }

    public void SetRagdoll(EntityHandle owner, bool isRagdoll) => store.SetRagdoll(owner, isRagdoll);
}
