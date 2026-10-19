namespace Debug.Api.Client.Interfaces;

/// <summary>A position, as the client's dev API reports it.</summary>
public readonly record struct DebugClientVec3(float X, float Y, float Z);

/// <summary>
/// The client's frame, as <c>/client/{slot}/frame</c> reports it
/// (plan_client_novalve §4.4): its counters, what <c>CFPSPanel</c> showed
/// (frames and frame times over the last second) and the local pose
/// (<c>cl_showpos</c>).
/// </summary>
/// <param name="Frames">Frames since the last level init.</param>
/// <param name="Commands">User commands processed since load.</param>
/// <param name="Stage">The stage the engine last raised.</param>
/// <param name="Stages">Stage notifications since load, per stage.</param>
/// <param name="WindowFrames">Frames in the last second of wall time.</param>
/// <param name="FrameMsMin">The shortest engine frame time in that window, in ms; 0 when it is empty.</param>
/// <param name="FrameMsAvg">Their mean, in ms.</param>
/// <param name="FrameMsMax">The longest, in ms.</param>
/// <param name="Time">The client's engine clock.</param>
/// <param name="PoseKnown">Whether a view has been published since load.</param>
/// <param name="Eye">The eye position of the last published view.</param>
public sealed record DebugClientFrame(
    int Frames, int Commands, string Stage, IReadOnlyList<DebugCount> Stages,
    int WindowFrames, double FrameMsMin, double FrameMsAvg, double FrameMsMax,
    float Time, bool PoseKnown, DebugClientVec3 Eye, float Pitch, float Yaw);

/// <summary>The client's frame counters and pose (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientFrame
{
    DebugClientFrame Read();
}
