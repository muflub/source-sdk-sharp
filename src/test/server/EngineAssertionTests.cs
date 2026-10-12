using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What an entity ASKED THE ENGINE TO DO — the assertion this project could not
/// make until the engine came from a container.
///
/// <para>
/// Every check here would have passed before the sweep no matter what the
/// entity did, because <c>Engine</c> in a test process answers 0 and empty to
/// everything and returns void from the rest. An entity that hurt nobody, or
/// hurt the wrong entity, or hurt them for the wrong amount, was indistinguishable
/// from one that behaved. That is the same shape as the three checks this project
/// has shipped that could not fail.
/// </para>
/// </summary>
public class EngineAssertionTests
{
    private static readonly EntityHandle Ent = new(0x0801_0001u);
    private static readonly EntityHandle Toucher = new(0x0801_0002u);

    private static (ScriptedEntityRegistry Registry, RecordingEngine Engine) Fitted()
    {
        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(TriggerServerRagdoll).Assembly);

        var fake = Mock().Ignores("Entities.IsPlayer");
        var services = new ServiceContainer();

        // Only the areas this test cares about. Everything else the entity
        // wants falls back to the real engine, which is what keeps a focused
        // test from having to register twenty-two things to assert one.
        services.AddInstance<IEngineDamage>(fake);
        services.AddInstance<IEngineEntities>(fake);

        registry.Services = services;
        return (registry, fake);
    }

    [Fact]
    public void ATriggerSaysWhatItDidToWhoeverWalkedIn()
    {
        var (registry, fake) = Fitted();

        // Not a player: the volume skips those on purpose, because a player's
        // ragdoll flag is set by a convar it was never asked about.
        fake.Returns("Entities.IsPlayer", false);

        registry.Spawn(Ent, EntityNames.TriggerServerragdoll);
        registry.Touch(Ent, Toucher);

        Assert.True(fake.Called("Damage.SetForceServerRagdoll"), "the trigger asked the engine to force a server ragdoll");

        Assert.True(fake.Last("Damage.SetForceServerRagdoll") is { } call
            && call.ArgAs<EntityHandle>(0) == Toucher, "on whoever walked in");

        Assert.True(fake.Last("Damage.SetForceServerRagdoll")!.Value.ArgAs<bool>(1), "and asked for it to be ON");

        // Leaving asks for the opposite, which is a different call with a
        // different argument and was previously indistinguishable from no call.
        registry.EndTouch(Ent, Toucher);

        Assert.True(fake.CountOf("Damage.SetForceServerRagdoll") == 2
            && !fake.Last("Damage.SetForceServerRagdoll")!.Value.ArgAs<bool>(1), "leaving asked for it to be off");
    }

    [Fact]
    public void AndSaysWhenItDidNothing()
    {
        var (registry, fake) = Fitted();

        // A player walks in. The volume must ask the engine for NOTHING, and
        // "asked for nothing" is the assertion that was impossible before: a
        // skipped call and a call into the void looked identical.
        fake.Returns("Entities.IsPlayer", true);

        registry.Spawn(Ent, EntityNames.TriggerServerragdoll);
        registry.Touch(Ent, Toucher);

        Assert.True(fake.Called("Entities.IsPlayer"), "a player is asked about");
        Assert.True(!fake.Called("Damage.SetForceServerRagdoll"), "and then left alone");
    }

    [Fact]
    public void TheFakeIsPreferredOverTheRealEngine()
    {
        var (registry, fake) = Fitted();

        registry.Spawn(Ent, EntityNames.TriggerServerragdoll);
        registry.Touch(Ent, Toucher);

        // The container answers first and the real engine is only the fallback.
        // If that were the other way round every check above would pass while
        // testing nothing, which is exactly the failure being designed out.
        Assert.True(fake.Calls.Count > 0, "the entity reached the fake and not the real engine");

        // And the fallback still covers what the container does not, or the
        // entity could not have been built at all. The fallback is the
        // registry's OWN engine (plan_abi_hotreload D8), not the active side's
        // process-wide defaults, so this asks the registry.
        Assert.True(registry.Resolver.Resolve<IEngineWorldQuery>() is not null, "an area nobody registered still resolves to the real engine");
    }
}
