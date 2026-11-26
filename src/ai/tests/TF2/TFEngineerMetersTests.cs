using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Engineer's panel reader joins the HUD's meter chain the way the side composes it: as itself (a side service joins
/// every chain it implements), at an order of its own. Live, a reader registered <c>As = [typeof(ITFHudMeters)]</c>
/// stopped the server at load ("ITFHudMeters is registered twice"), and two readers at one order throw at the chain's
/// first call (StageUniqueness).
/// </summary>
public sealed class TFEngineerMetersTests
{
    private static readonly System.Reflection.Assembly Binding = typeof(TFBotGame).Assembly;

    [Fact]
    public void ThePanelReaderClaimsNoContractTheMedigunsReaderHolds()
    {
        var services = new ServiceContainer();
        IReadOnlyList<Type> registered = SideServices.Register(services, Side.Server,
            [.. typeof(global::TF2.Server.Player.TFServerActorFacts).Assembly.GetTypes(), .. Binding.GetTypes()]);
        Assert.Contains(typeof(TFEngineerMeters), registered);
        Assert.Contains(typeof(TFMedigunMeters), registered);
    }

    [Fact]
    public void EveryMeterReaderHasAnOrderOfItsOwn()
    {
        Type[] readers = [.. Binding.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t))];
        Assert.Contains(typeof(TFEngineerMeters), readers);
        int[] orders = [.. readers.Select(t => (RuntimeHelpers.GetUninitializedObject(t) as IOrderedHook)?.HookOrder ?? 0)];
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    [Fact]
    public void APanelRowReadsBackAsTheReaderWroteIt()
    {
        string k = TFEngineerPanel.NameOf(TFEngineerPanel.Dispenser);
        var hud = new TFHud([], [new(k + TFEngineerPanel.Flags, TFEngineerPanel.Present | TFEngineerPanel.Built | TFEngineerPanel.Sapped),
                                 new(k + TFEngineerPanel.Level, 2), new(k + TFEngineerPanel.Health, 90), new(k + TFEngineerPanel.MaxHealth, 180),
                                 new(k + TFEngineerPanel.X, 10f), new(k + TFEngineerPanel.Y, 20f), new(k + TFEngineerPanel.Z, 30f)], []);
        TFOwnBuilding d = TFEngineerPanel.Read(hud, TFEngineerPanel.Dispenser);
        Assert.Equal((true, true, true, 2, 90, 180, new AiVector(10f, 20f, 30f)), (d.Exists, d.Built, d.Sapped, d.Level, d.Health, d.MaxHealth, d.At));
        Assert.False(TFEngineerPanel.Read(hud, TFEngineerPanel.Sentry).Exists);
    }

    /// <summary>The buildings he is asked about: the builder each ask names.</summary>
    private sealed class AskedBuildings : global::TF2.Server.Buildings.ITFBuildings
    {
        public List<EntityHandle?> Builders { get; } = new();

        public IEnumerable<global::TF2.Server.Buildings.TFBaseObject> OwnedBy(EntityHandle? builder, global::TF2.Shared.TFObjectType? type, int? mode)
        {
            Builders.Add(builder);
            return [];
        }
    }

    /// <summary>
    /// The panel reads the bot's own buildings only: every ask names him as the builder (the review's F1: an ask for every
    /// player's buildings turned no fact red).
    /// </summary>
    [Fact]
    public void ThePanelAsksOnlyForTheBotsOwnBuildings()
    {
        var asked = new AskedBuildings();
        var bot = new EntityHandle(0x0001_0003u);
        var into = new List<TFMeter>();
        new TFEngineerMeters(asked, null!).Read(bot, global::TF2.Shared.TFClass.Engineer, into);
        Assert.NotEmpty(asked.Builders);
        Assert.All(asked.Builders, b => Assert.Equal(bot, b));
        Assert.Contains(into, m => m.Name == TFEngineerPanel.Placing);
    }

    /// <summary>To any class but the Engineer the panel shows nothing and asks for nothing.</summary>
    [Fact]
    public void ThePanelIsAnEngineersOnly()
    {
        var asked = new AskedBuildings();
        var into = new List<TFMeter>();
        new TFEngineerMeters(asked, null!).Read(new EntityHandle(0x0001_0003u), global::TF2.Shared.TFClass.Soldier, into);
        Assert.Empty(asked.Builders);
        Assert.Empty(into);
    }
}
