extern alias client;

using TF2.Descent.Shared;

using Xunit;

using Catalog = client::TF2.Descent.Client.Content.DescentCatalog;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;
using IClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using IGameClock = client::SourceSharp.Abi.IGameClock;
using IBestiaryHook = client::TF2.Descent.Shared.IBestiaryHook;
using IRobotViewHook = client::TF2.Descent.Shared.IRobotViewHook;
using Knowledge = client::TF2.Descent.Client.Bestiary.RobotKnowledgeCache;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// What the local hero's client knows of the robots (plan_descent_logic UI6): his kill counts and
/// the robot views, fed through the client's own handlers from the bytes the server's payloads
/// write; a view of an old lend never outlives the next; a content mismatch is refused; a level's
/// end forgets everything.
/// </summary>
public sealed class RobotKnowledgeTests
{
    private static readonly Catalog Content = new();

    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 5f;
    }

    private static byte[] Bytes<T>(T payload) where T : SourceSharp.Abi.IUserMessagePayload<T>
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits");
        return buffer[..n];
    }

    private static void Deliver(IClientUserMessage handler, byte[] bytes) => handler.OnReceived(new ClientReader(bytes));

    private sealed class Wire
    {
        public readonly Clock Clock = new();
        public readonly Knowledge Cache;
        private readonly client::TF2.Descent.Shared.BestiaryMessage _counts;
        private readonly client::TF2.Descent.Shared.RobotViewMessage _view;
        private readonly client::TF2.Descent.Shared.RobotImmuneMessage _immune;

        public Wire()
        {
            Cache = new Knowledge(Content, Clock);
            _counts = new(new Hooks<IBestiaryHook>(Cache));
            var views = new Hooks<IRobotViewHook>(Cache);
            _view = new(views);
            _immune = new(views);
        }

        public void Counts(BestiaryPhase phase, params (byte Template, int Kills)[] rows) =>
            Deliver(_counts, Bytes(new BestiaryCounts
            {
                Fingerprint = Content.RobotFingerprint, Phase = (byte)phase,
                Templates = [.. rows.Select(r => r.Template)], Kills = [.. rows.Select(r => r.Kills)],
            }));

        public void View(int entity, int serial, int template, int[]? life = null, uint? fingerprint = null) =>
            Deliver(_view, Bytes(new RobotViewWire
            {
                Entity = (short)entity, Serial = serial, Template = (byte)template, Fingerprint = fingerprint ?? Content.RobotFingerprint,
                Reveal = (byte)(life is null ? RevealBits.Count : RevealBits.Count | RevealBits.Life),
                Life = life ?? [], Resist = [], Immune = [], Affixes = [], Style = "",
            }));

        public void Immune(int entity, int type) => Deliver(_immune, Bytes(new RobotImmune { Entity = (short)entity, DamageType = (byte)type }));
    }

    [Fact]
    public void Counts_are_held_by_template_row()
    {
        var w = new Wire();

        w.Counts(BestiaryPhase.Update, (1, 12), (0, 3));

        Assert.Equal((12, 3, 0), (w.Cache.KillsOf(1), w.Cache.KillsOf(0), w.Cache.KillsOf(2)));
    }

    [Fact]
    public void A_resync_forgets_the_counts_it_does_not_name()
    {
        var w = new Wire();
        w.Counts(BestiaryPhase.Update, (1, 12), (0, 3));

        w.Counts(BestiaryPhase.Resync, (1, 13));

        Assert.Equal((13, 0), (w.Cache.KillsOf(1), w.Cache.KillsOf(0)));
    }

    [Fact]
    public void A_view_is_held_by_entity_with_what_it_carried()
    {
        var w = new Wire();

        w.View(40, 1, 2, life: [180, 200]);

        var view = w.Cache.ViewOf(40);
        Assert.NotNull(view);
        Assert.Equal((2, 180, 200), (view!.Template, view.Life, view.MaxLife));
    }

    [Fact]
    public void A_gone_body_is_forgotten()
    {
        var w = new Wire();
        w.View(40, 1, 2);

        w.View(40, 1, 0);

        Assert.Null(w.Cache.ViewOf(40));
    }

    [Fact]
    public void A_gone_for_an_old_lend_leaves_the_next_lends_view()
    {
        var w = new Wire();
        w.View(40, 2, 3);                               // lent again before the old gone arrived

        w.View(40, 1, 0);

        Assert.Equal(2, w.Cache.ViewOf(40)!.Serial);
    }

    [Fact]
    public void A_view_named_by_other_content_is_refused()
    {
        var w = new Wire();

        w.View(40, 1, 2, fingerprint: Content.RobotFingerprint ^ 1);

        Assert.Null(w.Cache.ViewOf(40));
        Assert.True(w.Cache.ContentMismatch);
    }

    [Fact]
    public void Immune_is_held_with_the_clocks_time_and_the_type()
    {
        var w = new Wire();
        w.Clock.Now = 12.5f;

        w.Immune(40, 0);

        Assert.Equal((12.5f, 0), w.Cache.ImmuneOf(40));
    }

    [Fact]
    public void The_end_of_a_level_forgets_counts_views_and_immunes()
    {
        var w = new Wire();
        w.Counts(BestiaryPhase.Update, (1, 12));
        w.View(40, 1, 2);
        w.Immune(40, 0);

        ((client::SourceSharp.Client.IClientLevelHook)w.Cache).OnLevelShutdown();

        Assert.Equal((0, (client::TF2.Descent.Shared.RobotView?)null, ((float, int)?)null), (w.Cache.KillsOf(1), w.Cache.ViewOf(40), w.Cache.ImmuneOf(40)));
    }
}
