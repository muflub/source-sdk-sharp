using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client calls a mod's <see cref="SourceSharp.Client.IClientLevelHook"/>
/// on each level's init and shutdown. Held as facts about the source, as
/// <see cref="ClientFrameCountTests"/> is: <c>ClientModule</c>'s
/// <c>LevelInit</c> cannot run without an initialised module.
/// </summary>
public class ClientLevelHookTests
{
    private static string ClientModule()
        => File.ReadAllText(RepoLayout.Sdk("src/sourcesharp/managed/SourceSharp.Client/ClientModule.cs"));

    private static string BodyOf(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the source declares {signature}");

        int open = source.IndexOf('{', at);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"{signature} has no closing brace");
    }

    [Fact]
    public void The_hooks_are_registered_in_the_clients_container()
    {
        Assert.Contains(
            "_services.AddFactory<IHooks<IClientLevelHook>>(locator => new Hooks<IClientLevelHook>(locator, log));",
            ClientModule());
    }

    [Fact]
    public void Level_init_calls_the_hooks_with_the_map()
    {
        Assert.Contains(
            ".Each(nameof(IClientLevelHook.OnLevelInit), h => h.OnLevelInit(mapName));",
            BodyOf(ClientModule(), "public void LevelInit(string mapName)"));
    }

    [Fact]
    public void Level_shutdown_calls_the_hooks()
    {
        Assert.Contains(
            ".Each(nameof(IClientLevelHook.OnLevelShutdown), h => h.OnLevelShutdown());",
            BodyOf(ClientModule(), "public void LevelShutdown()"));
    }
}
