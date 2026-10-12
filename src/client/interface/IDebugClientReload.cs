namespace SourceSharp.Client.Interface;

/// <summary>One reload-store section the old build carried, with its size.</summary>
public sealed record DebugClientReloadSection(string Name, int Bytes, int Count);

/// <summary>
/// The client's last managed reload, as <c>/client/{slot}/reload</c> reports
/// it (plan_client_novalve §3.8 and §4.4): what crossed, who carried it, and
/// whether the old build's context went away.
/// </summary>
/// <param name="Sequence">Reloads this process has made of the client; 0 before the first.</param>
/// <param name="SavedAt">The engine clock at the save; 0 before the first reload.</param>
/// <param name="Carried">Every section the old build left, at the end of its save.</param>
/// <param name="Orphans">Carried participant sections no service here claimed.</param>
/// <param name="Failed">Participant sections that would not read back.</param>
/// <param name="Missing">Participants that found nothing carried for them.</param>
/// <param name="CachesDropped">Cached types the ABI forgot at the unload; -1 when no unload was seen.</param>
/// <param name="OldContext">"collected", "alive" or "unknown": the old build's load context after the swap.</param>
/// <param name="Collections">Full collections this build ran before the old context was gone.</param>
/// <param name="TriggerPending">A reload/trigger was accepted and has not happened yet.</param>
/// <param name="Milliseconds">Wall clock from the old build's save to the new build's restore, before its collections; 0 before the first.</param>
/// <param name="Ui">The UI's own reloads and the last deploy it classified; null before either.</param>
public sealed record DebugClientReload(
    int Sequence, float SavedAt, float RestoredAt, IReadOnlyList<DebugClientReloadSection> Carried,
    int ParticipantsSaved, int ParticipantsRestored, IReadOnlyList<string> Orphans,
    IReadOnlyList<string> Failed, IReadOnlyList<string> Missing, int EntitiesSaved, int EntitiesRestored,
    int CachesDropped, string OldContext, int Collections, bool TriggerPending, DebugClientUiReload? Ui = null,
    double Milliseconds = 0);

/// <summary>
/// The UI's own reload (plan_client_ui U5): the toolkit, the screens and the UI
/// host in their own load context, swapped without the core.
/// </summary>
/// <param name="Kind">"ui-only" for a swap of the UI's context; "none" before the first.</param>
/// <param name="Sequence">UI-only reloads this client has made.</param>
/// <param name="Trigger">What asked: "deploy" (the watcher), "command" (cs_ui_reload).</param>
/// <param name="Milliseconds">Carry, unload, load and restore, wall clock.</param>
/// <param name="CarriedBytes">What the old runtime carried.</param>
/// <param name="Reopened">Screens open again after the swap.</param>
/// <param name="OldContext">"pending" until the next frame judges it, then "collected" or "alive" (after up to ten collections).</param>
/// <param name="Collections">Full collections run before it was gone.</param>
/// <param name="CollectMilliseconds">What those collections took, apart from the swap.</param>
/// <param name="Deploy">The last deploy the watcher classified: "ui-only", "full" or "none".</param>
/// <param name="DeployChanged">The assemblies that deploy changed.</param>
public sealed record DebugClientUiReload(
    string Kind, int Sequence, string Trigger, double Milliseconds, int CarriedBytes, int Reopened, string OldContext,
    int Collections, double CollectMilliseconds, string Deploy, IReadOnlyList<string> DeployChanged);

/// <summary>The answer to <c>reload/trigger</c>: whether a reload was queued, and why not.</summary>
public sealed record DebugClientReloadRequest(bool Accepted, string Reason, int Sequence);

/// <summary>The client's managed reload: the last one, and a request for the next (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientReload
{
    DebugClientReload Read();

    /// <summary>Queues a client reload at the next frame boundary, as cs_cl_reload does. Cheat-gated.</summary>
    DebugClientReloadRequest Trigger();
}
