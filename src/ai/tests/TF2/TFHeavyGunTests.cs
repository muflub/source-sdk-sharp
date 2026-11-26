using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Ai.Tests.Senses;
using SourceSharp.Abi;
using TF2.Server.Entities.Items;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Heavy's read of his own gun (review finding 3's measure): the HUD meter <c>heavy.aiming</c> from his own
/// TF_COND_AIMING, and the play's count of decides the gun was idle when the hands had asked for it spun.
/// </summary>
public sealed class TFHeavyGunTests
{
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private static readonly ActorId Enemy = new(5, 0x4000_0001);

    private static List<TFMeter> Read(TFClass playerClass, bool aiming)
    {
        var bot = new EntityHandle(0x0001_0003u);
        ITFMapPlayers people = FakeEngine.Of<ITFMapPlayers>((m, a) => m.Name switch
        {
            "InCond" => aiming && (EntityHandle)a[0]! == bot && (TFCond)a[1]! == TFCond.Aiming,
            _ => throw FakeEngine.Unasked(m),
        });
        var into = new List<TFMeter>();
        new TFHeavyMeters(people).Read(bot, playerClass, into);
        return into;
    }

    /// <summary>A Heavy's HUD shows his own gun's spin as 1 or 0; another class's shows no such meter.</summary>
    [Fact]
    public void TheHeavysHudShowsHisOwnGunsSpin()
    {
        Assert.Equal([new TFMeter(TFHeavyMeters.Aiming, 1f)], Read(TFClass.HeavyWeapons, aiming: true));
        Assert.Equal([new TFMeter(TFHeavyMeters.Aiming, 0f)], Read(TFClass.HeavyWeapons, aiming: false));
        Assert.Empty(Read(TFClass.Soldier, aiming: true));
    }

    /// <summary>
    /// The TF2 server's lists compose with the Heavy's reader in them: the side registers it beside the Medic's (a second
    /// reader claiming the chain's interface stopped the server's load), and every reader in the chain has an order of its own.
    /// </summary>
    [Fact]
    public void TheHeavysReaderComposesBesideTheOthers()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.Server.AiBots).Assembly.GetTypes(), .. typeof(TFHeavyMeters).Assembly.GetTypes()]);
        Type[] readers = [.. registered.Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t)).Distinct()];
        Assert.Contains(typeof(TFHeavyMeters), readers);
        Assert.Contains(typeof(TFMedigunMeters), readers);
        static int Order(Type t) => System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t) is IOrderedHook o ? o.HookOrder : 0;
        Assert.Equal(readers.Length, readers.Select(Order).Distinct().Count());
    }

    /// <summary>The play's (wanted spun, wanted but idle) decides over 10 decides at a target 600 u off, the HUD showing the gun at <paramref name="aiming"/>.</summary>
    private static (long Wanted, long Idle) Counted(float aiming)
    {
        TFHud kit = TFHeavyTests.Kit();
        TFField f = TFHeavyTests.Field(new TFHud(kit.Kit, [new TFMeter(TFHeavyMeters.Aiming, aiming)], []));
        for (int i = 0; i < 10; i++)
            // No objectives: a fight, not the run for the flag (where a target past the band is not fought).
            f.Step(TFHeavyTests.Heavy(Mid), null, (Enemy, Mid + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Blue));
        List<KeyValuePair<string, long>> c = f.Decider.ClassCounters();
        return (c.Single(k => k.Key == "heavy.wanted_spun").Value, c.Single(k => k.Key == "heavy.wanted_idle").Value);
    }

    /// <summary>
    /// With a target 600 u off and the hands firing, every wanted decide whose HUD shows the gun idle is counted as idle; with
    /// the gun spinning none is. Both count the same wanted decides.
    /// </summary>
    [Fact]
    public void AnIdleGunTheHandsWantedSpunIsCounted()
    {
        (long wanted, long idle) = Counted(0f);
        Assert.True(wanted > 0, $"wanted decides {wanted}");
        Assert.Equal((wanted, wanted, 0L), (idle, Counted(1f).Wanted, Counted(1f).Idle));
    }
}
