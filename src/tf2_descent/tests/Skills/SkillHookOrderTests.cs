extern alias client;

using System.Reflection;
using System.Runtime.CompilerServices;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Skills;
using TF2.Server.Player;

using Xunit;

using ClientSkillBarState = client::TF2.Descent.Client.Skills.SkillBarState;
using ClientKeyActions = client::SourceSharp.Client.Input.KeyActions;

namespace TF2.Descent.Tests.Skills;

/// <summary>
/// The skills' chain members (HE6) have orders of their own: after TF2's
/// band, and held by no other member of the same chain in the core, TF2 or
/// TF2 Descent (a tie throws at the first dispatch, and only a boot with
/// both would see it). And the client's skill keys are TF2's list's tail.
/// </summary>
public sealed class SkillHookOrderTests
{
    private static (string Name, int Order)[] Members(Assembly assembly, Type chain) =>
        assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && chain.IsAssignableFrom(t)
                        && t.GetCustomAttributes<SideServiceAttribute>().Any(a => a.Side == Side.Server))
            .Select(t => (t.Name, (RuntimeHelpers.GetUninitializedObject(t) as IOrderedHook)?.HookOrder ?? 0))
            .ToArray();

    [Theory]
    [InlineData(typeof(IPlayerComponentSource), nameof(SkillComponents))]
    [InlineData(typeof(IHeroContributionSource), nameof(SkillContributions))]
    [InlineData(typeof(ITFPlayerAttributeSource), nameof(SkillAttributes))]
    [InlineData(typeof(IPlayerHurtHook), nameof(SkillTriggers))]
    [InlineData(typeof(IEntityHurtHook), nameof(SkillTriggers))]
    [InlineData(typeof(IPlayerDeathHook), nameof(SkillTriggers))]
    [InlineData(typeof(IEntityDeathHook), nameof(SkillTriggers))]
    [InlineData(typeof(ITFConditionHook), nameof(SkillTriggers))]
    [InlineData(typeof(IClientCommandHook), nameof(SkillClientCommands))]
    public void Each_skills_chain_member_has_an_order_no_other_member_holds(Type chain, string member)
    {
        Assembly[] assemblies = [typeof(SkillRuntime).Assembly, typeof(TFPlayerCarriers).Assembly, typeof(ServerModule).Assembly];
        var all = assemblies.SelectMany(a => Members(a, chain)).ToArray();
        var mine = Assert.Single(all, m => m.Name == member);

        Assert.True(mine.Order >= ModStages.First + 600 && mine.Order < ModStages.First + 700, $"{member} at {mine.Order}");
        Assert.Single(all, m => m.Order == mine.Order);
    }

    [Fact]
    public void The_skill_keys_follow_TF2s_list_and_bind_4_to_9_to_held_slot_commands()
    {
        var client = typeof(ClientSkillBarState).Assembly;
        var tf2 = typeof(client::TF2.Client.MoveForwardKey).Assembly;
        var actions = ClientKeyActions.Of(tf2.GetTypes().Concat(client.GetTypes()), typeof(ClientKeyActions).Assembly);

        var binds = actions.DefaultBinds.Where(b => b.Command.StartsWith("+tf2_descent_skill", StringComparison.Ordinal)).ToArray();
        Assert.Equal([("4", "+tf2_descent_skill1"), ("5", "+tf2_descent_skill2"), ("6", "+tf2_descent_skill3"),
                      ("7", "+tf2_descent_skill4"), ("8", "+tf2_descent_skill5"), ("9", "+tf2_descent_skill6")], binds);

        // TF2's own binds are still there, and each skill key's bind comes after TF2's for the same key.
        Assert.Contains(("4", "slot4"), actions.DefaultBinds);
        var order = actions.DefaultBinds.ToList();
        Assert.True(order.IndexOf(("4", "+tf2_descent_skill1")) > order.IndexOf(("4", "slot4")));
    }

    [Fact]
    public void A_skill_keys_edge_is_the_cast_line_the_server_answers()
    {
        Assert.Equal("tf2_descent_cast 3 1", client::TF2.Descent.Client.Skills.SkillKeys.CastLine(3, down: true));
        Assert.Equal("tf2_descent_cast 3 0", client::TF2.Descent.Client.Skills.SkillKeys.CastLine(3, down: false));
        Assert.Equal(SkillClientCommands.Cast, client::TF2.Descent.Client.Skills.SkillKeys.CastLine(1, true).Split(' ')[0]);
    }
}
