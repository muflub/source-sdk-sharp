using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two console registries feeding the container, and the container feeding
/// a command's constructor.
///
/// <para>
/// All of this runs with NO ENGINE, which is why both registries keep what the
/// engine refused: outside the game every registration comes back refused, so a
/// registry that only kept the accepted ones would be empty here and none of
/// the wiring below could be examined at all.
/// </para>
/// </summary>
public partial class ServiceRegistryTests
{
    //-------------------------------------------------------------------------
    // The declarations are in ServiceRegistryTests.Helpers.cs (src/test/common).
    //-------------------------------------------------------------------------

    [Fact]
    public void ConVarsReachTheContainer()
    {
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        var services = new ServiceContainer();

        convars.RegisterAll(typeof(ServiceRegistryTests).Assembly);
        int published = convars.PublishTo(services);

        Assert.True(published == convars.All.Count, "publishing covers every declared convar");

        // By type: what a rule in this mod asks for, and what the compiler
        // checks.
        var byType = services.Resolve<DiSpeedConVar>();
        Assert.True(byType is not null, "a convar resolves by its own class");

        // By name: what a map, a tool or anything driven by a string asks for.
        var byName = services.ConVar("ss_test_di_speed");
        Assert.True(ReferenceEquals(byName, byType), "the same convar resolves by the name a player types");

        Assert.True(ReferenceEquals(services.ConVar("SS_TEST_DI_SPEED"), byType), "and the console's case-insensitivity is honoured");

        // With no engine every convar was refused, and every one of them is
        // still here holding its declared default. That is the whole point:
        // resolving to null would send a caller to its own null-coalesce, which
        // is a different value from the declared one and invisible from outside.
        Assert.True(byType is not null, "a convar the engine refused is published anyway");
        Assert.True(byType!.Value == 250f, "holding its declared default");
        Assert.True(!byType.IsRegistered, "and saying it is not registered");
    }

    [Fact]
    public void CommandsAreBuiltFromTheContainer()
    {
        var services = new ServiceContainer();
        var counter = new Counter();

        // The order a module uses: convars first, then the systems, then the
        // commands that ask for either.
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(ServiceRegistryTests).Assembly);
        convars.PublishTo(services);
        services.AddInstance<ICounter>(counter);

        var commands = new ConCommandRegistry();
        commands.RegisterAll(typeof(ServiceRegistryTests).Assembly, TestServices.UnattachedConsole(), services);

        var bump = commands.ByName("ss_test_di_bump") as BumpCommand;
        Assert.True(bump is not null, "a command with dependencies is built");
        Assert.True(bump is not null && Runs(bump, counter), "and handed the live system");
        Assert.True(bump is not null && bump.SpeedSeen == 250f, "and the convar it asked for by class");

        // A command that asks for nothing is unaffected by any of this.
        Assert.True(commands.ByName("ss_test_di_plain") is PlainCommand, "a parameterless command is built too");
    }

    private static bool Runs(BumpCommand bump, Counter counter)
    {
        int before = counter.Count;
        bump.Execute(string.Empty);
        return counter.Count == before + 1;
    }

    [Fact]
    public void CommandsReachTheContainer()
    {
        var services = new ServiceContainer();
        services.AddInstance<ICounter>(new Counter());

        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(ServiceRegistryTests).Assembly);
        convars.PublishTo(services);

        var commands = new ConCommandRegistry();
        commands.RegisterAll(typeof(ServiceRegistryTests).Assembly, TestServices.UnattachedConsole(), services);
        commands.PublishTo(services);

        Assert.True(services.Command("ss_test_di_plain") is PlainCommand, "a command resolves from the container by name");
        Assert.True(services.ConVar("ss_test_di_plain") is null, "and asking for it as a convar comes back empty");

        // The listing and the by-name lookup have to agree about what exists.
        foreach (var info in commands.All)
        {
            if (services.ResolveNamed(info.Name) is not null)
                continue;

            Assert.Fail($"the listing and the container disagree about '{info.Name}'");
            return;
        }

        Assert.True(true, "everything the listing names is in the container");
    }

    [Fact]
    public void ACommandThatCannotBeBuiltIsSkipped()
    {
        // A container with the convars but WITHOUT the counter, so the command
        // asking for one cannot be built. The point is that the scan carries
        // on: one unbuildable declaration must not take the rest of the
        // console's commands down with it.
        var services = new ServiceContainer();
        var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
        convars.RegisterAll(typeof(ServiceRegistryTests).Assembly);
        convars.PublishTo(services);

        var commands = new ConCommandRegistry();
        commands.RegisterAll(typeof(ServiceRegistryTests).Assembly, TestServices.UnattachedConsole(), services);

        Assert.True(commands.ByName("ss_test_di_bump") is null, "a command whose dependency is missing is skipped");
        Assert.True(commands.ByName("ss_test_di_plain") is PlainCommand, "and the ones after it in the scan still register");

        // With no container at all, a command that needs one is skipped in the
        // same way rather than built half-formed.
        var bare = new ConCommandRegistry();
        bare.RegisterAll(typeof(ServiceRegistryTests).Assembly, TestServices.UnattachedConsole());

        Assert.True(bare.ByName("ss_test_di_bump") is null, "with no container a command that needs one is skipped");
        Assert.True(bare.ByName("ss_test_di_plain") is PlainCommand, "and a parameterless one still registers");
    }
}
