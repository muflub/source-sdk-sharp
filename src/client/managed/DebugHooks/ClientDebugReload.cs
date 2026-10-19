using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientReload"/> over the reload ledger and the requester
/// (plan_client_novalve §3.8, §4.4). The trigger is a write: it answers only
/// with sv_cheats on, and says so otherwise.
/// </summary>
public sealed class ClientDebugReload(ClientReloadLedger ledger, ClientReloadRequester requester, IEngineConsole console,
                                     Vgui.UiLoader? ui = null)
    : IDebugClientReload
{
    public DebugClientReload Read()
    {
        var last = ledger.Last;
        if (last is null)
            return new DebugClientReload(0, 0, 0, [], 0, 0, [], [], [], 0, 0, -1, "unknown", 0, requester.Pending, ui?.Report());

        var restored = last.Restored;
        return new DebugClientReload(
            last.Sequence, last.SavedAt, last.RestoredAt,
            last.Carried.Select(c => new DebugClientReloadSection(c.Name, c.Bytes, c.Count)).ToArray(),
            last.ParticipantsSaved, restored?.Sections.Count ?? 0,
            restored?.Orphans ?? [], restored?.Failed ?? [], restored?.Missing ?? [],
            last.EntitiesSaved, last.EntitiesRestored, last.CachesDropped, last.OldContext, last.Collections,
            requester.Pending, ui?.Report(), last.WallMilliseconds);
    }

    public DebugClientReloadRequest Trigger()
    {
        if (console.FindConVarValue("sv_cheats") is not "1")
            return new DebugClientReloadRequest(false, "a write: needs sv_cheats 1", ledger.Sequence);

        if (requester.Pending)
            return new DebugClientReloadRequest(true, "already queued", ledger.Sequence);

        return requester.Request()
            ? new DebugClientReloadRequest(true, "queued for the next frame boundary", ledger.Sequence)
            : new DebugClientReloadRequest(false, "no engine client to queue it through", ledger.Sequence);
    }
}
