extern alias client;

using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Descent.Server.Horde;
using Xunit;
using ClientFrameThink = client::SourceSharp.Client.IClientFrameThink;
using ClientOrderedHook = client::SourceSharp.Abi.IOrderedHook;
using ClientRobotPoses = client::TF2.Descent.Client.Robots.ClientRobotPoses;
using ClientSideServiceAttribute = client::SourceSharp.Abi.SideServiceAttribute;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's members of chains TF2 and the core fill too (a registered type
/// joins every chain it implements, and a tie throws at the first dispatch):
/// each holds an order of its own, after TF2's band. And what a robot looks
/// like is all precached.
/// </summary>
public sealed class HordeChainTests
{
    private static (string Name, int Order)[] Members(Assembly assembly, Type chain, Func<object, int> order) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && chain.IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == Side.Server))
            .Select(t => (t.Name, order(RuntimeHelpers.GetUninitializedObject(t))))
            .ToArray();

    [Theory]
    [InlineData(typeof(ITransmitRule))]
    [InlineData(typeof(IFrameLifecycle))]
    [InlineData(typeof(IGameLifecycle))]
    public void The_hordes_server_chain_members_hold_orders_no_one_else_holds(Type chain)
    {
        Func<object, int> order = chain == typeof(IFrameLifecycle) ? o => ((IFrameLifecycle)o).FrameOrder
            : chain == typeof(IGameLifecycle) ? o => ((IGameLifecycle)o).LifecycleOrder
            : o => (o as IOrderedHook)?.HookOrder ?? 0;
        var descent = typeof(HordeService).Assembly;
        var mine = Members(descent, chain, order).Where(m => m.Name.StartsWith("Horde", StringComparison.Ordinal)).ToArray();
        var others = new[] { descent, typeof(TF2.Server.Rules.TFGlowTransmit).Assembly, typeof(GameModes).Assembly }
            .SelectMany(a => Members(a, chain, order)).Where(m => !mine.Contains(m)).ToArray();

        Assert.NotEmpty(mine);
        Assert.NotEmpty(others);
        foreach (var (name, o) in mine)
        {
            Assert.True(o >= ModStages.First, $"{name} at {o} sits inside TF2's band or the core's");
            Assert.DoesNotContain(others, t => t.Order == o);
        }
    }

    [Fact]
    public void The_clients_pose_easing_holds_a_frame_order_no_one_else_holds()
    {
        int Order(Type t) => (RuntimeHelpers.GetUninitializedObject(t) as ClientOrderedHook)?.HookOrder ?? 0;
        var members = new[] { typeof(ClientRobotPoses).Assembly, typeof(client::TF2.Client.Player.TFClientBodyDress).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ClientFrameThink).IsAssignableFrom(t)
                        && t.GetCustomAttributes<ClientSideServiceAttribute>().Any())
            .ToArray();

        Assert.True(members.Length >= 2);
        Assert.Single(members, t => Order(t) == Order(typeof(ClientRobotPoses)));
    }

    [Fact]
    public void Every_template_wears_a_precached_model_and_holds_a_precached_weapon()
    {
        var bodies = typeof(RobotBody).GetCustomAttributes<PrecacheAttribute>().Select(p => p.Asset).ToHashSet();
        var weapons = typeof(RobotWeapon).GetCustomAttributes<PrecacheAttribute>().Select(p => p.Asset).ToHashSet();

        foreach (var t in HordeFixtures.TheRules.Content.Robots.Rows)
        {
            Assert.Contains(RobotLooks.BodyModel(t), bodies);
            if (RobotLooks.WeaponModel(t) is { } w)
                Assert.Contains(w, weapons);
        }
    }

    [Fact]
    public void Only_bare_fists_hold_no_weapon_model()
    {
        var bare = HordeFixtures.TheRules.Content.Robots.Rows.Where(t => RobotLooks.WeaponModel(t) is null).Select(t => t.Id);

        Assert.Equal(["sentry_buster", "captain_punch", "tank"], bare);   // HO13: the Tank holds nothing
    }

    [Fact]
    public void The_sentry_buster_wears_stocks_buster_model()
    {
        Assert.Equal(RobotLooks.SentryBusterModel, RobotLooks.BodyModel(HordeFixtures.TheRules.Robots.Template("sentry_buster")));
        Assert.Equal(RobotLooks.GiantDemomanModel, RobotLooks.BodyModel(HordeFixtures.TheRules.Robots.Template("giant_demoman")));
    }

    [Fact]
    public void The_clients_pose_moves_part_of_the_way_and_settles_on_its_value()
    {
        Assert.Equal(0.5f, ClientRobotPoses.Toward(0f, 1f, 0.5f));
        Assert.Equal(1f, ClientRobotPoses.Toward(0.995f, 1f, 0.1f));
        Assert.Equal(1f, ClientRobotPoses.Toward(0f, 1f, 3f));
    }
}
