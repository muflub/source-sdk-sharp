using SourceSharp.Abi;
using SourceSharp.Ai.Server.Senses;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>The Scout's own HUD numbers (P7, scout.md §4.7 item 4): its drink meter, its hype and whether it is phased, and only a Scout's.</summary>
public sealed class TFScoutMetersTests
{
    private sealed class Players(TFPlayerNetState state) : ITFPlayerData
    {
        public TFPlayerNetState? Predicted(int playerIndex) => state;

        public TFPlayerNetState? Networked(int playerIndex) => state;
    }

    private sealed class Conditions(bool phased) : ITFPlayerConditions
    {
        public TFConditions? Of(int playerIndex) => null;

        public TFCondAdd Add(EntityHandle player, TFCond cond, float duration = TFConditions.Permanent, EntityHandle provider = default) => TFCondAdd.Refused;

        public bool Remove(EntityHandle player, TFCond cond, bool ignoreDuration = false) => false;

        public TFCondMask RemoveAll(EntityHandle player) => default;

        public bool InCond(EntityHandle player, TFCond cond) => phased && cond == TFCond.Phase;
    }

    private static readonly EntityHandle Bot = new(0x0001_0003u);

    private static List<TFMeter> Read(TFClass cls, bool phased)
    {
        var state = new TFPlayerNetState();
        state.BindLocal();
        (state.EnergyDrinkMeter, state.HypeMeter) = (64f, 30f);
        var into = new List<TFMeter>();
        new TFScoutMeters(new Players(state), new Conditions(phased)).Read(Bot, cls, into);
        return into;
    }

    [Fact]
    public void AScoutsHudShowsItsDrinkItsHypeAndItsPhase()
        => Assert.Equal([new TFMeter(TFScoutMeters.Drink, 64f), new TFMeter(TFScoutMeters.Hype, 30f), new TFMeter(TFScoutMeters.Phased, 1f)], Read(TFClass.Scout, true));

    [Fact]
    public void AnotherClassShowsNoScoutMeter()
        => Assert.Empty(Read(TFClass.Soldier, true));

    /// <summary>
    /// TF2's server list composes with the Scout's meters in the HUD's chain beside the Medigun's: a reader registered "as"
    /// <see cref="ITFHudMeters"/> claimed the interface a second time and stopped the side's composition (live, the lane's
    /// first run: "ITFHudMeters is registered twice").
    /// </summary>
    /// <summary>
    /// Every reader of the meters chain stands at an order of its own: the chain refuses two at one number when it is first
    /// read (live, the lane's second run: "TFMedigunMeters and TFScoutMeters both declare HookOrder 0" from every bot's senses).
    /// </summary>
    [Fact]
    public void EveryMetersReaderHasAnOrderOfItsOwn()
    {
        Type[] readers = [.. typeof(SourceSharp.Ai.TF2.TFBotGame).Assembly.GetTypes().Where(t => typeof(ITFHudMeters).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })];
        int[] orders = [.. readers.Select(t => (System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t) as IOrderedHook)?.HookOrder ?? 0)];
        Assert.Contains(typeof(TFScoutMeters), readers);
        Assert.Contains(typeof(TFMedigunMeters), readers);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void TF2sServerListComposesWithTheScoutsMeters()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(TFServerActorFacts).Assembly.GetTypes(), .. typeof(VisionPass).Assembly.GetTypes(), .. typeof(SourceSharp.Ai.TF2.TFBotGame).Assembly.GetTypes()]);
        Assert.Contains(typeof(TFScoutMeters), registered);
        Assert.Contains(typeof(TFMedigunMeters), registered);
    }
}
