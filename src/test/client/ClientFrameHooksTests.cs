using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client steps the mods' per-frame thinks (<see cref="SourceSharp.Client.IClientFrameThink"/>)
/// once a frame, after the core's own work. Held as facts about the source, as
/// <see cref="ClientLevelHookTests"/> is: <c>ClientModule</c>'s <c>Frame</c>
/// cannot run without a composed module, and the composition itself is what
/// wires the chain.
/// </summary>
public class ClientFrameHooksTests
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
    public void The_frame_hooks_are_registered_in_the_clients_container()
    {
        Assert.Contains(
            "_services.AddFactory<IHooks<IClientFrameThink>>(locator => new Hooks<IClientFrameThink>(locator, log));",
            ClientModule());
    }

    [Fact]
    public void Each_frame_steps_the_hooks_with_the_frame_time()
    {
        Assert.Contains(
            ".Each(nameof(IClientFrameThink.ClientThink), t => t.ClientThink(frameTime));",
            BodyOf(ClientModule(), "public void Frame(float frameTime)"));
    }
}
