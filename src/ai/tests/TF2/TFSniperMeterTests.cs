using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The Sniper's HUD meters' arithmetic: the charge bar in whole points, a holstered jar's bar run on at its recharge.</summary>
public sealed class TFSniperMeterTests
{
    /// <summary>A charge begun 1.01 s ago shows 50 points (stock's 50 a second), in whole points.</summary>
    [Fact]
    public void TheChargeBarShowsWholePoints()
    {
        var rifle = new WeaponNetState();
        rifle.BindLocal();
        rifle.BehaviourMeter = 1f;
        Assert.Equal(50f, TFSniperMeters.ChargeShown(rifle, 2.01f));
    }

    /// <summary>A holstered jar at half its bar ten seconds ago is full (20.1 s a bar); at a quarter one second ago it is not.</summary>
    [Fact]
    public void AHolsteredJarsBarRunsOnAtItsRecharge()
        => Assert.Equal((1f, true), (TFSniperMeters.JarShown(0.5f, 10f, 20.06f), TFSniperMeters.JarShown(0.25f, 10f, 11f) < 1f));

    /// <summary>
    /// The HUD meter chain's members each hold an order of their own (an unordered one is 0): two at one order throw at the
    /// chain's first call, which live stopped every bot's senses (AiSenses.OnPostThink threw: TFMedigunMeters and
    /// TFSniperMeters both declare HookOrder 0). TFBindingTests' chain census reads only interfaces named *Hook, so it
    /// passed over this chain.
    /// </summary>
    [Fact]
    public void TheHudMeterChainsMembersHoldOrdersOfTheirOwn()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. typeof(TFBotGame).Assembly.GetTypes()]);
        static int Order(Type t)
            => typeof(IOrderedHook).IsAssignableFrom(t)
                ? ((IOrderedHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).HookOrder
                : 0;
        Type[] members = [.. registered.Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t)).Distinct()];
        Assert.Contains(typeof(TFSniperMeters), members);
        Assert.Equal(members.Length, members.Select(Order).Distinct().Count());
    }
}
