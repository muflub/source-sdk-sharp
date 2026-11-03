namespace Debug.Api.Client.Interfaces;

/// <summary>
/// One kept GameUI call: its place in the session's calls, the method, its
/// arguments as text (null for none), how it ended ("handled", "inert" for a
/// method the host answers inertly by ruling, "unhosted" when no GameUI host
/// took it), and how many times in a row it came the same.
/// </summary>
/// <summary>
/// The last finished level load: whether the engine asked for the progress
/// dialog, the progress updates, how many answered yes (a repaint), and the
/// menu frames drawn during it.
/// </summary>
public sealed record DebugClientGameUiLoad(bool Dialog, int Updates, int Repaints, long Frames);

public sealed record DebugClientGameUiCall(long Sequence, string Method, string? Arguments, string Outcome, int Repeats);

/// <summary>
/// The engine's GameUI011 and GameConsole004 calls as <c>/client/{slot}/gameui</c>
/// reports them (plan_gameui_managed.md §4): every call, the calls no host
/// took (none answered, none ruled), each method's count, and the last 64
/// (RunFrame, every frame, is counted and never kept); and the loads
/// finished, the last one's paint count (null before the first).
/// </summary>
public sealed record DebugClientGameUi(long Calls, long Unruled, IReadOnlyList<DebugCount> Counts,
                                       IReadOnlyList<DebugClientGameUiCall> Recent, int Loads = 0,
                                       DebugClientGameUiLoad? LastLoad = null);

/// <summary>The engine's GameUI calls (plan_gameui_managed.md §4). Game thread.</summary>
public interface IDebugClientGameUi
{
    DebugClientGameUi Read();
}
