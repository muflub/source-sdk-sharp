using System.Runtime.CompilerServices;
using System.Text.Json;

using SourceSharp.Abi;
using SourceSharp.Client;
using Debug.Api.Client.Interfaces;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's reload ledger and its routes (plan_client_novalve §3.8, §4.4):
/// what one build leaves, what the next reads, and the leak arm.
/// </summary>
public class ClientReloadRouteTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    // Allocated in a frame of its own, so nothing on the test's stack roots it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SaveOverAGoneSubject(ClientReloadLedger ledger)
        => ledger.Save(participantsSaved: 1, entitiesSaved: 5, subject: new object());

    private static (ClientReloadLedger Old, ClientReloadLedger New, ReloadStore Store, Clock Clock) Pair()
    {
        var store = new ReloadStore(Side.Client);
        var clock = new Clock { Now = 12.5f };
        return (new ClientReloadLedger(store, clock, new RecordingLog()),
                new ClientReloadLedger(store, clock, new RecordingLog()), store, clock);
    }

    [Fact]
    public void A_reload_whose_old_build_went_away_reads_collected()
    {
        var (old, fresh, store, clock) = Pair();
        store.Put("standalone-mirror", new byte[40], 3);
        store.NoteCachesDropped(17);

        SaveOverAGoneSubject(old);
        clock.Now = 13f;

        Assert.True(fresh.BeginRestore());
        var entry = fresh.FinishRestore(null, entitiesRestored: 5);

        Assert.NotNull(entry);
        Assert.Equal(1, fresh.Sequence);
        Assert.Equal("collected", entry!.OldContext);
        Assert.Equal(17, entry.CachesDropped);
        Assert.Equal((12.5f, 13f), (entry.SavedAt, entry.RestoredAt));
        Assert.Equal([("standalone-mirror", 40, 3)], entry.Carried);
        Assert.Equal((5, 5), (entry.EntitiesSaved, entry.EntitiesRestored));
    }

    [Fact]
    public void A_reload_whose_old_build_is_pinned_reads_alive()
    {
        var (old, fresh, _, _) = Pair();
        var pinned = new object();

        old.Save(0, 0, pinned);
        Assert.True(fresh.BeginRestore());
        var entry = fresh.FinishRestore(null, 0);

        Assert.Equal("alive", entry!.OldContext);
        Assert.Equal(ClientReloadLedger.MaxCollections, entry.Collections);
        GC.KeepAlive(pinned);
    }

    [Fact]
    public void A_fresh_load_has_no_ledger_to_read()
    {
        var (_, fresh, _, _) = Pair();
        Assert.False(fresh.BeginRestore());
        Assert.Null(fresh.FinishRestore(null, 0));
        Assert.Equal(0, fresh.Sequence);
    }

    [Fact]
    public void The_sequence_counts_reloads_across_builds()
    {
        var (first, second, store, clock) = Pair();
        SaveOverAGoneSubject(first);
        second.BeginRestore();
        second.FinishRestore(null, 0);

        var third = new ClientReloadLedger(store, clock, new RecordingLog());
        SaveOverAGoneSubject(second);
        third.BeginRestore();
        third.FinishRestore(null, 0);

        Assert.Equal(2, third.Sequence);
    }

    private sealed class Console(string? cheats) : IEngineConsole
    {
        public void ExecuteServerCommand(string command) => throw new NotSupportedException();
        public string? FindConVarValue(string name) => name == "sv_cheats" ? cheats : null;
        public string GetConVarValue(int id) => throw new NotSupportedException();
        public void Msg(string message) { }
        public int RegisterConCommand(string name, string help, ConVarFlags flags) => throw new NotSupportedException();
        public int RegisterConVar(string name, string defaultValue, string help, int flags, float? min = null, float? max = null) => throw new NotSupportedException();
        public void SendClientCommand(EntityHandle player, string command) => throw new NotSupportedException();
        public void SetConVarValue(int id, string value) => throw new NotSupportedException();
        public void SetConVarValueByName(string name, string value) => throw new NotSupportedException();
        public int Snapshot() => 0;
        public bool SnapshotEntry(int index, out NativeConsoleEntry entry) { entry = default; return false; }
        public void Warning(string message) { }
    }

    private static (ClientDebugReload Route, ClientReloadRequester Requester) Route(string? cheats, List<string>? sent = null)
    {
        var (_, fresh, _, _) = Pair();
        var requester = new ClientReloadRequester(new ChatFeedTests.RecordingEngineClient(sent ?? []));
        return (new ClientDebugReload(fresh, requester, new Console(cheats)), requester);
    }

    [Fact]
    public void The_trigger_is_refused_without_cheats()
    {
        var (route, requester) = Route(cheats: "0");
        var answer = route.Trigger();

        Assert.False(answer.Accepted);
        Assert.Contains("sv_cheats", answer.Reason, StringComparison.Ordinal);
        Assert.False(requester.Pending);
    }

    [Fact]
    public void With_cheats_the_trigger_asks_the_engine_and_is_queued()
    {
        // The positive arm of the gate: past the cheat check, to the engine.
        var sent = new List<string>();
        var (route, requester) = Route(cheats: "1", sent);
        var answer = route.Trigger();

        Assert.True(answer.Accepted);
        Assert.Equal(["reload"], sent);
        Assert.True(requester.Pending);
    }

    private sealed class FixedReload(DebugClientReload reload) : IDebugClientReload
    {
        public DebugClientReload Read() => reload;
        public DebugClientReloadRequest Trigger() => new(true, "queued for the next frame boundary", reload.Sequence);
    }

    [Fact]
    public void The_reload_answers_render_exactly()
    {
        var reload = new DebugClientReload(
            Sequence: 2, SavedAt: 12.5f, RestoredAt: 13f,
            Carried: [new DebugClientReloadSection("client.service:SourceSharp.Client.IHudMessages", 61, 1)],
            ParticipantsSaved: 1, ParticipantsRestored: 1, Orphans: [], Failed: [], Missing: [],
            EntitiesSaved: 120, EntitiesRestored: 120, CachesDropped: 17, OldContext: "collected", Collections: 0,
            TriggerPending: false);

        using var client = DebugApiClientGoldenTests.Channel(TestDebugHooks.Client(reload: new FixedReload(reload)));
        Assert.Equal(DebugApiClientGoldenTests.Golden("reload"), client.Answer(DebugChannelQuery.Reload));
        Assert.Equal(DebugApiClientGoldenTests.Golden("reload-trigger"), client.Answer(DebugChannelQuery.ReloadTrigger));
    }
}
