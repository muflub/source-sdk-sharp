using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Host.Contracts;
using SourceSharp.Host.Sdk;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Sheets;
using TF2.Descent.Server;
using TF2.Descent.Server.Hero;

using Xunit;

namespace TF2.Descent.Tests;

/// <summary>
/// The hosted hero sheet (plan_descent_logic HE2): the join decodes the leased
/// sheet, and every checkpoint PlayerService sends, the leave's included,
/// carries the sheet as it is now with the evidence it drains.
/// </summary>
public sealed partial class ServiceFlowTests
{
    private sealed record Hosted(World W, PlayerService Players, FakeClients Clients, HeroSheets Sheets, TF2DescentRules Rules);

    /// <summary>One player joined on the hub, whose character's sheet is <paramref name="leased"/>.</summary>
    private static Hosted HostedHero(Func<TF2DescentRules, CharacterSheet?> leased, IFrameScheduler? scheduler = null)
    {
        var w = Host(HostingMode.Hosted);
        w.Link.BootAnswers.Enqueue(HostResult<BootInfo>.Success(Boot("hub")));
        w.Host.OnPostInit();
        w.Host.OnLevelInit("tf2_descent_town");
        var rules = new TF2DescentRules();
        var clients = new FakeClients();
        var sheets = new HeroSheets(rules, w.Host, clients, w.Log);
        var players = new PlayerService(w.Link, w.Host, clients, new ClientMessages(new RecordingEngine()),
                                        scheduler ?? new NeverScheduler(), w.Log, sheets);
        w.Link.JoinAnswer = w.Link.JoinAnswer with { Sheet = leased(rules) };
        clients.Slots[3] = (ulong.Parse(Steam, System.Globalization.CultureInfo.InvariantCulture), "203.0.113.7:27005");
        players.OnClientActive(3);
        return new Hosted(w, players, clients, sheets, rules);
    }

    private static CharacterSheet LevelThree(TF2DescentRules r) =>
        r.SheetCodec.Write(r.Heroes.GainXp(r.Heroes.New("soldier", "Jane"), r.Heroes.XpAtLevel(3)).Sheet);

    private static HeroSheet Sent(Hosted h, int i = -1) =>
        h.Rules.SheetCodec.Read(h.W.Link.Sheets[i < 0 ? h.W.Link.Sheets.Count + i : i].Sheet!.Sheet);

    private static CheckpointProof Evidence(Hosted h, int i = -1) =>
        h.Rules.Evidence.Read(h.W.Link.Sheets[i < 0 ? h.W.Link.Sheets.Count + i : i].Sheet!.Evidence);

    [Fact]
    public void A_hosted_join_decodes_the_leased_sheet()
    {
        var h = HostedHero(LevelThree);

        var sheet = h.Sheets.Of(3);

        Assert.NotNull(sheet);
        Assert.Equal(3, sheet.Level);
        Assert.Equal("Jane", sheet.Name);
        Assert.Equal(10, sheet.AttributePoints);
    }

    [Fact]
    public void A_hosted_slot_has_no_sheet_before_its_join_answers()
    {
        var w = Host(HostingMode.Hosted);
        w.Host.OnPostInit();
        var sheets = new HeroSheets(new TF2DescentRules(), w.Host, new FakeClients(), w.Log);

        Assert.Null(sheets.Of(3));
    }

    [Fact]
    public void A_leased_sheet_the_rules_cannot_read_refuses_the_join()
    {
        var h = HostedHero(_ => new CharacterSheet("not json"u8.ToArray(), SheetCodec.Schema));

        Assert.Null(h.Players.BySteamId(Steam));
        Assert.Null(h.Sheets.Of(3));
        Assert.Contains(h.Clients.Calls, c => c.StartsWith("kick 3 Your character's sheet could not be read", StringComparison.Ordinal));
        Assert.Contains($"leave {Steam} 203.0.113.7:27005", h.W.Link.Calls);
    }

    [Fact]
    public void A_join_that_carries_no_sheet_starts_a_new_hero_of_its_class()
    {
        var h = HostedHero(_ => null);

        Assert.Equal("soldier", h.Sheets.Of(3)!.ClassId);
        Assert.Equal(1, h.Sheets.Of(3)!.Level);
    }

    [Fact]
    public async Task A_checkpoint_carries_the_current_sheet_and_the_evidence_that_explains_it()
    {
        var h = HostedHero(LevelThree);
        var leased = LevelThree(h.Rules);
        long xp = h.Rules.Heroes.XpAtLevel(4) - h.Sheets.Of(3)!.Xp;
        Assert.True(h.Sheets.Apply(3, s => h.Rules.Heroes.GainXp(s, xp).Sheet).Ok);
        h.Sheets.RecordEarned(3, xp, new Dictionary<string, int> { ["scout"] = 2 });

        Assert.True(await h.Players.Checkpoint(Steam));

        Assert.Equal(4, Sent(h).Level);
        Assert.Equal(xp, Evidence(h).XpEarned);
        Assert.Equal(2, Evidence(h).KillsOf("scout"));
        // What the host's rules make of it: the sheet is explained by its evidence.
        var payload = h.W.Link.Sheets[^1].Sheet!;
        Assert.True(h.Rules.ValidateCheckpoint(leased, payload.Sheet, payload.Evidence).Ok);
    }

    [Fact]
    public void The_minutes_checkpoint_carries_the_current_sheet()
    {
        var once = new OnceScheduler();
        var h = HostedHero(LevelThree, once);
        Assert.True(h.Sheets.Apply(3, s => h.Rules.Heroes.Spend(s, "grit", 4).Sheet).Ok);

        h.Players.OnLive();

        Assert.Contains($"checkpoint {Steam}", h.W.Link.Calls);
        Assert.Equal(19, Sent(h).Base("grit"));
    }

    [Fact]
    public async Task Pending_evidence_is_drained_by_exactly_one_checkpoint()
    {
        var h = HostedHero(LevelThree);
        h.Sheets.RecordEarned(3, 40, new Dictionary<string, int> { ["heavy"] = 1 });
        h.Sheets.RecordEarned(3, 2);

        await h.Players.Checkpoint(Steam);
        await h.Players.Checkpoint(Steam);

        Assert.Equal(42, Evidence(h, 0).XpEarned);
        Assert.Equal(1, Evidence(h, 0).KillsOf("heavy"));
        Assert.Equal(0, Evidence(h, 1).XpEarned);
        Assert.Empty(Evidence(h, 1).Kills);
    }

    [Fact]
    public async Task Evidence_a_refused_checkpoint_drained_is_owed_again_by_the_next()
    {
        var h = HostedHero(LevelThree);
        h.Sheets.RecordEarned(3, 40);
        h.W.Link.CheckpointAnswers.Enqueue(false);

        Assert.False(await h.Players.Checkpoint(Steam));
        h.Sheets.RecordEarned(3, 5);
        Assert.True(await h.Players.Checkpoint(Steam));

        Assert.Equal(40, Evidence(h, 0).XpEarned);
        Assert.Equal(45, Evidence(h, 1).XpEarned);
        Assert.Equal(0, h.Sheets.Pending(3).XpEarned);
    }

    [Fact]
    public void The_leave_carries_the_current_sheet_and_forgets_the_hero()
    {
        var h = HostedHero(LevelThree);
        Assert.True(h.Sheets.Apply(3, s => h.Rules.Heroes.Spend(s, "aim", 1).Sheet).Ok);
        h.Sheets.RecordEarned(3, 7);

        h.Players.OnClientDisconnected(3);

        Assert.Contains($"leave {Steam} 203.0.113.7:27005", h.W.Link.Calls);
        Assert.Equal(21, Sent(h).Base("aim"));
        Assert.Equal(7, Evidence(h).XpEarned);
        Assert.Null(h.Sheets.Of(3));
    }

    [Fact]
    public async Task A_player_without_a_hero_is_checkpointed_with_the_leases_own_sheet()
    {
        var h = HostedHero(LevelThree);
        h.Sheets.Left(Steam);

        await h.Players.Checkpoint(Steam);

        Assert.Null(h.W.Link.Sheets[^1].Sheet);
    }

    [Fact]
    public void A_re_lease_keeps_the_sheet_the_game_holds()
    {
        var h = HostedHero(LevelThree);
        Assert.True(h.Sheets.Apply(3, s => h.Rules.Heroes.Spend(s, "brawn", 3).Sheet).Ok);

        h.Players.OnLeaseLost(Steam, LeaseLostReason.Unreachable, "no heartbeat");
        Assert.True(h.Players.IsSuspended(Steam) || h.Players.BySteamId(Steam) is not null);

        Assert.Equal(23, h.Sheets.Of(3)!.Base("brawn"));
    }

    /// <summary>Completes the first delay at once and never the next: the checkpoint loop runs one round.</summary>
    private sealed class OnceScheduler : IFrameScheduler
    {
        private int _calls;
        public Task Delay(TimeSpan delay, CancellationToken cancellation = default) =>
            _calls++ == 0 ? Task.CompletedTask : new TaskCompletionSource().Task;
    }
}
