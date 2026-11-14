extern alias client;

using SourceSharp.Abi;
using TF2.Descent.Shared;
using Xunit;

using ClientBossBarMessage = client::TF2.Descent.Shared.BossBarMessage;
using ClientBossBarSource = client::TF2.Descent.Client.Boss.BossBarSource;
using IClientBossBarHook = client::TF2.Descent.Shared.IBossBarHook;
using IClientLevelHook = client::SourceSharp.Client.IClientLevelHook;
using IClientUserMessage = client::SourceSharp.Abi.IUserMessage;
using ClientReader = client::SourceSharp.Abi.UserMessageReader;

namespace TF2.Descent.Tests.Bosses;

/// <summary>
/// The boss bar's client half (plan_descent_logic HO13, the HUD's <c>IDescentBoss</c>): the
/// server's bytes read into the live source, absent until one arrives and after an absent one or a
/// level's end; the payload fits a user message with the longest name it carries.
/// </summary>
public sealed class BossClientTests
{
    private sealed class Hooks<T>(params T[] hooks) : client::SourceSharp.Abi.IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    /// <summary>A payload as the SERVER writes it.</summary>
    private static byte[] Written(BossBar payload)
    {
        var buffer = new byte[255];
        int n = payload.Write(buffer);
        Assert.True(n >= 0, "the payload fits a user message");
        return buffer[..n];
    }

    private static void Dispatch(IClientUserMessage handler, byte[] bytes) => handler.OnReceived(new ClientReader(bytes));

    private static (ClientBossBarSource Source, ClientBossBarMessage Message) Client()
    {
        var source = new ClientBossBarSource();
        return (source, new ClientBossBarMessage(new Hooks<IClientBossBarHook>(source)));
    }

    private static BossBar Nukesalot(int life) =>
        BossBar.Of(new BossBarView(true, 30, "Sir Nukesalot", life, 4200, 1, 3));

    [Fact]
    public void The_boss_bar_is_absent_until_the_server_sends_one()
    {
        var (source, _) = Client();

        Assert.False(source.Present);
        Assert.Equal(("", 0, 0, 0, 0), (source.Name, source.Life, source.LifeMax, source.Phase, source.PhaseCount));
    }

    [Fact]
    public void A_sent_bar_shows_the_boss_s_name_life_most_and_phase()
    {
        var (source, message) = Client();

        Dispatch(message, Written(Nukesalot(2900)));

        Assert.True(source.Present);
        Assert.Equal(("Sir Nukesalot", 2900, 4200, 1, 3), (source.Name, source.Life, source.LifeMax, source.Phase, source.PhaseCount));
    }

    [Fact]
    public void An_absent_bar_takes_the_bar_down()
    {
        var (source, message) = Client();
        Dispatch(message, Written(Nukesalot(2900)));

        Dispatch(message, Written(BossBar.Of(BossBarView.Absent)));

        Assert.False(source.Present);
    }

    [Fact]
    public void A_level_s_end_takes_the_bar_down()
    {
        var (source, message) = Client();
        Dispatch(message, Written(Nukesalot(2900)));

        ((IClientLevelHook)source).OnLevelShutdown();

        Assert.False(source.Present);
    }

    [Fact]
    public void The_longest_name_fits_and_a_longer_one_is_cut()
    {
        string name = new('N', BossBar.MaxName + 10);
        var bar = BossBar.Of(new BossBarView(true, 255, name, int.MaxValue, int.MaxValue, 255, 255));

        var bytes = Written(bar);

        Assert.True(bytes.Length < 64, $"{bytes.Length} bytes");
        Assert.Equal(new string('N', BossBar.MaxName), bar.Name);
    }
}
