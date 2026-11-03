extern alias client;

using SourceSharp.Abi;

using Xunit;

using ClientDriver = client::TF2.Client.Player.TFClientConditionDriver;
using ClientTFPlayerData = client::TF2.Client.Player.ClientTFPlayerData;
using ClientWords = client::TF2.Shared.Player.TFPlayerNetStateViews;
using Hook = client::TF2.Client.Player.ITFClientConditionHook;
using Mask = client::TF2.Shared.Conditions.TFCondMask;
using Cond = client::TF2.Shared.TFCond;

namespace TF2.Tests.Client;

/// <summary>
/// The client condition driver's diff
/// (<c>CTFPlayerShared::SyncConditions</c>, tf_player_shared.cpp:1504-1538):
/// a decode whose words moved runs the arms for exactly the bits that
/// moved, adds before removes, bits and words ascending.
/// </summary>
public sealed class TFClientConditionSyncTests
{
    internal static readonly EntityHandle Player = new(0x0002_0007u);

    private static (RecordingHook, ClientDriver) Driven()
    {
        var calls = new RecordingHook();
        return (calls, new ClientDriver(new HookList([calls])));
    }

    [Fact]
    public void A_bit_turned_on_fires_OnAdded_once()
    {
        var (calls, driver) = Driven();

        driver.Sync(Player, default, Mask.Of(Cond.Burning));

        Assert.Equal([$"+{Cond.Burning} @{Player.EntryIndex}"], calls.Calls);
    }

    [Fact]
    public void A_bit_turned_off_fires_OnRemoved_once()
    {
        var (calls, driver) = Driven();

        driver.Sync(Player, Mask.Of(Cond.Burning), default);

        Assert.Equal([$"-{Cond.Burning} @{Player.EntryIndex}"], calls.Calls);
    }

    [Fact]
    public void An_unchanged_word_fires_nothing()
    {
        var (calls, driver) = Driven();
        var words = Mask.Of([Cond.Burning, Cond.Critboosted]);

        driver.Sync(Player, words, words);

        Assert.Empty(calls.Calls);
    }

    [Fact]
    public void The_first_sync_of_set_words_fires_the_adds_ascending()
    {
        var (calls, driver) = Driven();

        driver.Sync(Player, default, Mask.Of([Cond.Critboosted, Cond.Burning]));

        Assert.Equal([$"+{Cond.Critboosted} @{Player.EntryIndex}", $"+{Cond.Burning} @{Player.EntryIndex}"],
                     calls.Calls);
    }

    /// <summary>Stock's per-bit order: the added arm is asked first for a bit,
    /// and the bits ascend — a removal below an addition stays in bit order
    /// (:1527-1534).</summary>
    [Fact]
    public void Within_a_word_a_removal_below_an_addition_stays_in_bit_order()
    {
        var (calls, driver) = Driven();
        var was = Mask.Of(Cond.Critboosted);   // bit 11
        var now = Mask.Of(Cond.Burning);       // bit 22

        driver.Sync(Player, was, now);

        Assert.Equal([$"-{Cond.Critboosted} @{Player.EntryIndex}", $"+{Cond.Burning} @{Player.EntryIndex}"],
                     calls.Calls);
    }

    [Fact]
    public void A_later_words_bit_names_its_condition()
    {
        var (calls, driver) = Driven();

        driver.Sync(Player, default, Mask.Of(Cond.SpeedBoost));   // 32: word 1, bit 0

        Assert.Equal([$"+{Cond.SpeedBoost} @{Player.EntryIndex}"], calls.Calls);
    }
}

/// <summary>
/// The decode's wire: the carrier's OnDataChanged runs the driver between
/// the old words and the new, and names the player by its shell's handle.
/// </summary>
public sealed class TFClientConditionCarrierTests
{
    [Fact]
    public void A_decode_fires_the_arms_for_the_changed_bits_and_the_shells_handle()
    {
        var (data, calls) = Rig();

        data.OnDataChanged(default, DataUpdateType.Created);
        Assert.Equal([$"+{Cond.Burning} @3"], calls.Calls);

        ClientWords.SetConditions(data.State, Mask.Of([Cond.Burning, Cond.Critboosted]));
        data.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal([$"+{Cond.Burning} @3", $"+{Cond.Critboosted} @3"], calls.Calls);

        // An unchanged decode: nothing further.
        data.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal(2, calls.Calls.Count);
    }

    [Fact]
    public void A_condition_leaving_fires_OnRemoved_on_the_next_decode()
    {
        var (data, calls) = Rig();

        data.OnDataChanged(default, DataUpdateType.Created);
        ClientWords.SetConditions(data.State, default);
        data.OnDataChanged(default, DataUpdateType.DataTableChanged);

        Assert.Equal([$"+{Cond.Burning} @3", $"-{Cond.Burning} @3"], calls.Calls);
    }

    [Fact]
    public void A_decode_before_the_players_shell_holds_the_words_for_the_first_that_has_one()
    {
        var shell = new LateShell();
        var (data, calls) = Rig(shell);

        // The join's first decode: the carrier's words are set, the player's
        // own entity has not arrived — the arms would name a default handle.
        data.OnDataChanged(default, DataUpdateType.Created);
        Assert.Empty(calls.Calls);

        // The shell lands; the next decode -- even an unchanged one -- reports
        // the held adds once, against the real handle.
        shell.Arrive();
        data.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal([$"+{Cond.Burning} @3"], calls.Calls);
    }

    /// <summary>A carrier over one shell at index 3, its words opening on the burning bit.</summary>
    private static (ClientTFPlayerData data, RecordingHook calls) Rig(client::SourceSharp.Client.IClientStandalones? shell = null)
    {
        var engine = new client::SourceSharp.Abi.RecordingEngine();
        var standalones = shell ?? new OneShell();
        var resources = new client::TF2.Client.Player.TFClientPlayerResources();
        var log = new client::SourceSharp.Abi.RecordingLog();
        var classes = new client::TF2.Shared.TFClassTable(log, new ClientFiles(TFClassTableTests.Files.Shipped()));
        var calls = new RecordingHook();
        var data = new ClientTFPlayerData(engine, new client::TF2.Client.Player.TFClientCarriers(),
                                          standalones, resources, engine, classes,
                                          new ClientDriver(new HookList([calls])), log);
        data.State.BindLocal();
        data.State.OwnerIndex = 3;
        ClientWords.SetConditions(data.State, Mask.Of(Cond.Burning));
        return (data, calls);
    }

    /// <summary>One player shell at entity index 3, enough directory for the carrier's lookups.</summary>
    private sealed class OneShell : client::SourceSharp.Client.IClientStandalones
    {
        private readonly client::SourceSharp.Client.Standalone.ClientSsPlayer _player =
            new(new client::SourceSharp.Abi.RecordingEngine());

        public OneShell()
        {
            _player.BindIndex(3);
            _player.Handle = new EntityHandle(0x0002_0003u);
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live() => [_player];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _player.Index == entityIndex ? _player : null;
    }

    /// <summary>A directory whose player shell has not arrived; <see cref="Arrive"/> lands it.</summary>
    private sealed class LateShell : client::SourceSharp.Client.IClientStandalones
    {
        private client::SourceSharp.Client.Standalone.ClientSsPlayer? _player;

        public void Arrive()
        {
            _player = new(new client::SourceSharp.Abi.RecordingEngine());
            _player.BindIndex(3);
            _player.Handle = new EntityHandle(0x0002_0003u);
        }

        public IReadOnlyList<client::SourceSharp.Client.Standalone.ClientStandaloneEntity> Live()
            => _player is { } player ? [player] : [];

        public client::SourceSharp.Client.Standalone.ClientStandaloneEntity? GetByIndex(int entityIndex)
            => _player is { } player && player.Index == entityIndex ? player : null;
    }

    /// <summary>The shipped class scripts, as the client's file seam sees them.</summary>
    private sealed class ClientFiles(TFClassTableTests.Files files) : client::SourceSharp.Abi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);
    }
}

/// <summary>The arms a sync ran, in order, with the player they named.</summary>
internal sealed class RecordingHook : Hook
{
    public List<string> Calls { get; } = [];

    public void OnAdded(EntityHandle player, Cond cond) => Calls.Add($"+{cond} @{player.EntryIndex}");

    public void OnRemoved(EntityHandle player, Cond cond) => Calls.Add($"-{cond} @{player.EntryIndex}");
}

/// <summary>A one-arm chain, for the driver.</summary>
internal sealed class HookList(IReadOnlyList<Hook> hooks) : client::SourceSharp.Abi.IHooks<Hook>
{
    public IReadOnlyList<Hook> All => hooks;

    public void Each(string what, Action<Hook> call)
    {
        foreach (var hook in hooks)
            call(hook);
    }
}
