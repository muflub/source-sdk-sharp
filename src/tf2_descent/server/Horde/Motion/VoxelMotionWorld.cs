using SourceSharp.Abi;
using SourceSharp.Ai.TF2.Jump;

namespace TF2.Descent.Server.Horde.Motion;

/// <summary>
/// The motion's world off the game thread (plan_descent_logic 3.7, E5): a hull swept through the
/// level's nav voxel columns by <see cref="VoxelMoveWorld"/>, no engine call. It knows the
/// level's brushes as the <c>.nav3d</c> holds them (a voxel sideways, the file's floor heights)
/// and nothing that moves or that the file leaves out: no door, no prop, no body, no hero. So a
/// worker's step is accepted on the game thread against the engine (<see cref="RobotMotion"/>'s
/// accept), which stops a body at what this world does not know. Every hit is the world
/// (entity index 0). Stateless: one world serves any thread.
/// </summary>
public sealed class VoxelMotionWorld(VoxelMoveWorld voxels) : IMotionWorld
{
    public MotionTrace Sweep(in Vector start, in Vector end, in Vector mins, in Vector maxs, EntityHandle ignore)
    {
        MoveTrace t = voxels.Trace(start, end, mins, maxs, 0u, 0);
        if (!t.StartSolid && t.Fraction >= 1f)
            return MotionTrace.Clear(end);
        return new MotionTrace(true, t.Fraction, t.EndPos, t.PlaneNormal, t.StartSolid, t.AllSolid, EntityHandle.None, 0, false);
    }
}
