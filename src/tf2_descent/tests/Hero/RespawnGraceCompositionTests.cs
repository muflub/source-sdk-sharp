using System.Reflection;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Session;
using TF2.Descent.Server.Lamp;

using Xunit;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// No constructor cycle between the downed state and the horde: <see cref="HeroDowned"/> holds
/// its listeners' chain, which holds <see cref="DownedHordeTap"/>, which holds the horde's
/// session. A hook chain is no constructor need: <c>Hooks&lt;T&gt;</c> collects its members the
/// first time it is asked (<c>All</c>), after the container has built its holder; so the walk
/// that decides a cycle stops at an <c>IHooks&lt;T&gt;</c> parameter, and the walk that follows
/// chains is kept as the control that such a path exists. Read over the mod's registrations by
/// constructor: an interface reaches every registered type that implements it.
/// </summary>
public sealed class RespawnGraceCompositionTests
{
    private static readonly Assembly Server = typeof(HeroDowned).Assembly;

    private static readonly Type[] Registered =
        [.. Server.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && t.GetCustomAttribute<SideServiceAttribute>() is not null)];

    /// <summary>
    /// Every mod type a constructor of <paramref name="root"/> needs, transitively; a hook chain's
    /// members only when <paramref name="throughChains"/> (they are collected after construction).
    /// </summary>
    private static HashSet<Type> Needs(Type root, bool throughChains = false)
    {
        var seen = new HashSet<Type>();
        var work = new Queue<Type>([root]);
        while (work.Count > 0)
        {
            var t = work.Dequeue();
            foreach (var ctor in t.GetConstructors())
            {
                foreach (var p in ctor.GetParameters())
                {
                    foreach (var need in Implementations(p.ParameterType, throughChains))
                        if (seen.Add(need))
                            work.Enqueue(need);
                }
            }
        }

        return seen;
    }

    private static IEnumerable<Type> Implementations(Type wanted, bool throughChains)
    {
        if (wanted.IsGenericType && wanted.GetGenericTypeDefinition() == typeof(IHooks<>))
        {
            if (!throughChains)
                return [];
            wanted = wanted.GetGenericArguments()[0];
        }

        if (wanted.Assembly != Server && !wanted.IsInterface)
            return [];
        return wanted.IsInterface ? Registered.Where(wanted.IsAssignableFrom) : wanted.Assembly == Server ? [wanted] : [];
    }

    [Fact]
    public void The_horde_session_is_built_from_nothing_that_is_built_from_the_session_or_the_downed_state()
    {
        var needs = Needs(typeof(HordeSession));

        Assert.Contains(typeof(RespawnGraces), needs);    // the stimulus: the walk reaches the board
        Assert.Contains(typeof(HeroVisibility), needs);   // and the lamp's seam
        Assert.DoesNotContain(typeof(HeroDowned), needs);
        Assert.DoesNotContain(typeof(HordeSession), needs);
    }

    [Fact]
    public void The_downed_state_is_built_from_nothing_that_is_built_from_it_or_from_the_session()
    {
        var needs = Needs(typeof(HeroDowned));

        Assert.Contains(typeof(RespawnGraces), needs);    // the stimulus
        Assert.DoesNotContain(typeof(HeroDowned), needs);
        Assert.DoesNotContain(typeof(HordeSession), needs);
    }

    [Fact]
    public void Followed_through_its_listeners_chain_the_downed_state_does_reach_the_session()
    {
        // The control: with chains followed the walk finds the path a constructor must not take.
        Assert.Contains(typeof(HordeSession), Needs(typeof(HeroDowned), throughChains: true));
    }

    [Fact]
    public void The_board_needs_nothing()
    {
        Assert.All(typeof(RespawnGraces).GetConstructors(), c => Assert.Empty(c.GetParameters()));
    }

    [Fact]
    public void The_lamps_sight_is_the_one_registered_visibility()
    {
        var visibility = Assert.Single(Registered, t => typeof(IHeroVisibility).IsAssignableFrom(t));
        Assert.Equal(typeof(HeroVisibility), visibility);
    }
}
