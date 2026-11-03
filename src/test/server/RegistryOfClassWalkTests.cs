using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Regression: <c>PlayerClientData.OnPreClientUpdate</c> threw "Collection was
/// modified" on a live death cam — <see cref="ScriptedEntityRegistry.OfClass"/>
/// walked the directory's dense list lazily, and the game code a frame runs
/// (a <c>PreThink</c> that binds an entity) mutated the list mid-walk. The walk
/// takes its handles first now, so it answers the frame's opening state.
/// </summary>
public class RegistryOfClassWalkTests
{
    private const string Relay = "test_shellless_relay";

    private ScriptedEntityRegistry Fresh()
    {
        var side = TestSides.Create(Side.Server);
        var registry = new ScriptedEntityRegistry(side.ShellLess, side.EntityIo, side.Removal,
                                                  TestEngines.Server());

        var services = new SourceSharp.Abi.ServiceContainer().AddInstance(new StandaloneMirror());
        EngineServices.AddTo(services, side);
        registry.Services = services;

        registry.RegisterAll(typeof(RegistryOfClassWalkTests).Assembly);
        return registry;
    }

    [Fact]
    public void EnumeratingOfClassSurvivesABindDuringTheWalk()
    {
        var registry = Fresh();
        registry.Create(Relay, default);

        int seen = 0;

        foreach (var _ in registry.OfClass(Relay))
        {
            seen++;

            // What a PreThink spawning does: an entity binds mid-walk.
            if (seen == 1)
                registry.Create(Relay, default);
        }

        // The walk answered the frame's opening state: the first entity only.
        Assert.Equal(1, seen);
    }

    [Fact]
    public void EnumeratingOfClassSurvivesAnUnbindDuringTheWalk()
    {
        var registry = Fresh();
        var first = registry.Create(Relay, default);
        registry.Create(Relay, default);

        int seen = 0;

        foreach (var _ in registry.OfClass(Relay))
        {
            seen++;

            // What a death does: an entity goes mid-walk (marked, then the
            // frame's free pass — the dense list reorders on an unbind).
            if (seen == 1)
            {
                registry.Directory.Remove(first);
                registry.Directory.FreeRemoved();
            }
        }

        // The surviving entity is still walked, the gone one is not.
        Assert.Equal(2, seen);
    }
}
