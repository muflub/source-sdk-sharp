using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A core service that takes a hook chain (<c>IHooks&lt;T&gt;</c>) by
/// constructor gets null when the server's composition never registered that
/// chain: the parameter is optional, so nothing throws and the hook is dead.
/// <c>IPlayerWeaponEventHook</c> and <c>IPlayerHurtHook</c> were: the
/// arsenal and the player were built with no chain, so no mod ever heard a
/// shot, a reload or a hit live, while their facts (which hand the chain in)
/// passed. Held over <c>ServerModule.Initialize</c>'s registrations, as
/// <see cref="ServerModuleWiringOrderTests"/> holds its order.
/// </summary>
public class ServerHookRegistrationTests
{
    private static readonly Regex Registered =
        new(@"_services\.AddFactory<\s*IHooks<\s*([A-Za-z_][\w.]*)\s*>\s*>", RegexOptions.Compiled);

    private static string Simple(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    /// <summary>Each hook a server type takes as an <c>IHooks&lt;T&gt;</c> constructor parameter, by the type that takes it.</summary>
    private static List<(string Hook, string Taker)> Taken()
    {
        var taken = new List<(string, string)>();
        foreach (Type t in typeof(ServerModule).Assembly.GetTypes())
        {
            foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (ParameterInfo p in c.GetParameters())
                {
                    if (p.ParameterType is { IsGenericType: true } g && g.GetGenericTypeDefinition() == typeof(IHooks<>))
                        taken.Add((g.GetGenericArguments()[0].Name, t.Name));
                }
            }
        }

        return taken;
    }

    [Fact]
    public void EveryHookChainACoreServiceTakesIsRegistered()
    {
        string source = File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Server/ServerModule.cs"));
        var registered = Registered.Matches(source).Select(m => Simple(m.Groups[1].Value)).ToHashSet(StringComparer.Ordinal);
        List<(string Hook, string Taker)> taken = Taken();

        // The stimulus reaches the subject: the arsenal's and the player's chains are among those read.
        Assert.Contains(taken, x => x.Hook == nameof(IPlayerWeaponEventHook));
        Assert.Contains(taken, x => x.Hook == nameof(IPlayerHurtHook));
        Assert.Contains(nameof(IPlayerDeathHook), registered);

        string[] missing = taken.Where(x => !registered.Contains(x.Hook))
                                .Select(x => $"{x.Taker} takes IHooks<{x.Hook}>, which ServerModule never registers")
                                .Distinct()
                                .ToArray();
        Assert.True(missing.Length == 0, string.Join("\n", missing));
    }
}
