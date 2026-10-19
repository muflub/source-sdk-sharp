namespace SourceSharp.Client.Interop;

/// <summary>
/// The leaf system's per-view decisions (C7 cluster 12, L1 of
/// docs/plans/c7_leaf_system.md), asked through IManagedRenderClient by
/// shell_view_executor_client.cpp: which renderables a view draws, the leaf
/// each translucent one renders in and their order, and the view models'
/// split. Each call is one buffer; src/client/native/leaf_view_gather.h's
/// header gives both layouts.
/// </summary>
public interface IClientLeafLists
{
    /// <summary>
    /// One view's render list over the buffer <paramref name="view"/>: the
    /// answer written into <paramref name="answer"/> and its word count, or -1
    /// when the buffer is malformed or the answer does not fit (native then
    /// builds the view with stock's BuildRenderablesList).
    /// </summary>
    int BuildRenderableList(ReadOnlySpan<int> view, Span<int> answer);

    /// <summary>
    /// The view models' opaque/translucent split: <paramref name="groups"/>
    /// is the count and each view model's render group; <paramref name="answer"/>
    /// gets 0 (opaque) or 1 (translucent) for each. The count, or -1 when the
    /// buffer is malformed.
    /// </summary>
    int SplitViewModels(ReadOnlySpan<int> groups, Span<int> answer);
}
