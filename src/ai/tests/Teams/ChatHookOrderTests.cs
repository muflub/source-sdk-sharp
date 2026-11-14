using System.Reflection;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Ai.Hl2mp.Teams;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.Teams;

/// <summary>
/// Every <see cref="IPlayerChatHook"/> the server composes has its own
/// <see cref="IOrderedHook.HookOrder"/>: the chain refuses two at one number,
/// and it refuses at the first said line, so a clash is a game whose chat
/// stops, not a failed composition (the bots' team orders and their chat both
/// sat at 0 until integration).
/// </summary>
public class ChatHookOrderTests
{
    [Fact]
    public void EveryPlayerChatHookHasItsOwnOrder()
    {
        var hooks = new[] { typeof(TeamOrderTap).Assembly, typeof(HostSay).Assembly }
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlayerChatHook).IsAssignableFrom(t))
            .ToArray();

        Assert.True(hooks.Length >= 2, $"the bots' chat and the team-order tap are both chat hooks; found {hooks.Length}");

        var orders = hooks.Select(t => (Type: t, Order: OrderOf(t))).ToArray();
        var clashes = orders.GroupBy(o => o.Order).Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(o => o.Type.Name))}").ToArray();

        Assert.True(clashes.Length == 0, "chat hooks sharing a HookOrder: " + string.Join("; ", clashes));
    }

    private static int OrderOf(Type type)
        => typeof(IOrderedHook).IsAssignableFrom(type)
            ? ((IOrderedHook)RuntimeHelpers.GetUninitializedObject(type)).HookOrder
            : 0;
}
