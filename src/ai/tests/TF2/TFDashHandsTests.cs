using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The hands for a triple jump (round 2's S2): the Atomizer gives its third jump only in the hands (stock CanAirDash), so a
/// Scout whose route just ahead says two presses in the air takes it out in a lull and keeps it through the jump; with no
/// triple jump near, or no Atomizer, the general hands are as they were.
/// </summary>
public sealed class TFDashHandsTests
{
    private const int Scattergun = 20, Pistol = 21, Bat = 22, Atomizer = 26;

    private static readonly WeaponKnowledge Weapons = new([
        .. TFField.Weapons.All,
        new WeaponFacts(Scattergun, "tf_weapon_scattergun", 6, 0.625f, 3f, true, true, 0f),
        new WeaponFacts(Pistol, "tf_weapon_pistol_scout", 12, 0.15f, 1f, true, false, 0f),
        new WeaponFacts(Bat, "tf_weapon_bat", 0, 0.5f, 0f, false, false, 0f),
        new WeaponFacts(Atomizer, "tf_weapon_bat", 0, 0.5f, 0f, false, false, 0f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params TFOwnKitAttribute[] attributes)
        => new(slot, defindex, itemClass, kind, [.. attributes]);

    private static TFHud BonkBoy() => new([Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun), Item(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", Pistol),
                                           Item(TFLoadoutSlot.Melee, 450, "tf_weapon_bat", Atomizer, new TFOwnKitAttribute("air_dash_count", 1f))], [], []);

    private static TFHud Stock() => new([Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", Scattergun), Item(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", Pistol),
                                         Item(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", Bat)], [], []);

    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    private static SelfState Self(int weapon) => TFField.Self(Mid, health: 125, clip: weapon == Scattergun ? 6 : 0, reserve: 32, weapon: weapon, playerClass: TFClass.Scout);

    private static (TFField Field, TFRouteDashes Dashes) Scout(TFHud hud)
    {
        var dashes = new TFRouteDashes();
        return (new TFField(slot: 3, mates: [1, 2], weapons: Weapons, routeDashes: dashes) { Hud = hud }, dashes);
    }

    private static bool Selects(IntentCommand c, int kind) => c.Action.Kind == ActionKind.SelectWeapon && c.Action.WeaponSlot == EngineBotSelf.ByKind + kind;

    private static IntentCommand[] Decides(TFField f, int weapon, int count) => [.. Enumerable.Range(0, count).Select(_ => f.StepDrawn(Self(weapon), TFField.Ctf()))];

    [Fact]
    public void ATripleJumpAheadTakesTheAtomizerOut()
    {
        var (f, dashes) = Scout(BonkBoy());
        dashes.Dashes(f.Bot, new Tick(0), 2);
        Assert.Contains(Decides(f, Scattergun, 4), c => Selects(c, Atomizer));
    }

    /// <summary>The control: a double jump ahead (one press in the air) needs no Atomizer, and the Scattergun stays out.</summary>
    [Fact]
    public void ADoubleJumpAheadLeavesTheScattergunOut()
    {
        var (f, dashes) = Scout(BonkBoy());
        dashes.Dashes(f.Bot, new Tick(0), 1);
        Assert.DoesNotContain(Decides(f, Scattergun, 24), c => Selects(c, Atomizer));
    }

    [Fact]
    public void AScoutWithNoAtomizerTakesNothingOutForIt()
    {
        var (f, dashes) = Scout(Stock());
        dashes.Dashes(f.Bot, new Tick(0), 2);
        Assert.DoesNotContain(Decides(f, Scattergun, 24), c => c.Action.Kind == ActionKind.SelectWeapon);
    }

    /// <summary>The Atomizer out with the triple jump ahead: the lull's hands do not take the Scattergun back, as they would with no jump near.</summary>
    [Fact]
    public void TheAtomizerStaysOutThroughTheJumpAndTheScattergunComesBackAfter()
    {
        var (f, dashes) = Scout(BonkBoy());
        dashes.Dashes(f.Bot, new Tick(0), 2);
        Assert.DoesNotContain(Decides(f, Atomizer, 24), c => Selects(c, Scattergun));
        // The route past the jump (the bot over the ledge, mid-flight): the Atomizer stays out for the hold, 1.5 s, so the
        // third press still answers; once the hold has run out the primary comes back out.
        dashes.Dashes(f.Bot, new Tick(f.Tick), 0);
        Assert.DoesNotContain(Decides(f, Atomizer, 8), c => Selects(c, Scattergun));
        Assert.Contains(Decides(f, Atomizer, 48), c => Selects(c, Scattergun));
    }

    private static readonly ActorId Enemy = new(5, 0x4000_0001);

    private static ActorFacts EnemyAt(float dist) => new(Enemy, Mid + new AiVector(dist, 0f, 0f), default, Scattergun, ActorActivity.None, 1f, TFField.Blue, (int)TFClass.Soldier);

    /// <summary>
    /// A target in sight: the hands are the fight's, and the dash weapon stays away however near the triple jump is. Held on
    /// the general hands themselves (a Soldier's play is theirs; the Scout's own hands keep a sighted fight from them anyway),
    /// over a kit with a dash weapon in the melee slot, and a target past the guns' reach, so the hands are not firing and the
    /// general hands are asked.
    /// </summary>
    [Fact]
    public void ASightedTargetKeepsTheDashWeaponAway()
    {
        var dashes = new TFRouteDashes();
        var f = new TFField(slot: 3, mates: [1, 2], routeDashes: dashes)
        {
            Hud = new([Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", TFField.Launcher), Item(TFLoadoutSlot.Secondary, 10, "tf_weapon_shotgun_soldier", TFField.Shotgun),
                       Item(TFLoadoutSlot.Melee, 450, "tf_weapon_shovel", TFField.Shovel, new TFOwnKitAttribute("air_dash_count", 1f))], [], []),
        };
        dashes.Dashes(f.Bot, new Tick(0), 2);
        SelfState soldier = TFField.Self(Mid, weapon: TFField.Launcher);
        IntentCommand[] decides = [.. Enumerable.Range(0, 24).Select(_ => f.StepDrawn(soldier, TFField.Ctf(), EnemyAt(2600f)))];
        Assert.DoesNotContain(decides, c => Selects(c, TFField.Shovel));
        // The control: nobody about, the same general hands take it out.
        var other = new TFRouteDashes();
        var h = new TFField(slot: 3, mates: [1, 2], routeDashes: other) { Hud = f.Hud };
        other.Dashes(h.Bot, new Tick(0), 2);
        Assert.Contains(Enumerable.Range(0, 4).Select(_ => h.StepDrawn(soldier, TFField.Ctf())).ToArray(), c => Selects(c, TFField.Shovel));
    }

    /// <summary>
    /// A target the bot is hunting (seen, then gone round a corner, still believed near): no weapon switch for a dash, the
    /// gun stays out for when it comes back into sight. The control: the same Scout with nobody about takes the Atomizer out.
    /// </summary>
    [Fact]
    public void AHuntedTargetKeepsTheDashWeaponAway()
    {
        var (f, dashes) = Scout(BonkBoy());
        dashes.Dashes(f.Bot, new Tick(0), 2);
        f.StepDrawn(Self(Scattergun), TFField.Ctf(), EnemyAt(500f));
        IntentCommand[] decides = [.. Enumerable.Range(0, 6).Select(_ => f.StepDrawn(Self(Scattergun), TFField.Ctf()))];
        Assert.DoesNotContain(decides, c => Selects(c, Atomizer));
        var (control, said) = Scout(BonkBoy());
        said.Dashes(control.Bot, new Tick(0), 2);
        Assert.Contains(Decides(control, Scattergun, 4), c => Selects(c, Atomizer));
    }

    // ---------------------------------------------------------------- the counters

    /// <summary>A flight from the floor up to <paramref name="top"/> and down onto a ledge at <paramref name="land"/>, a decide a step.</summary>
    private static void Flight(TFField f, float top, float land)
    {
        SelfState At(float z, bool grounded) => TFField.Self(Mid with { Z = z }, health: 125, clip: 6, reserve: 32, weapon: Atomizer, playerClass: TFClass.Scout)
                                                 with { Flags = grounded ? SelfFlags.Alive | SelfFlags.Grounded : SelfFlags.Alive };
        f.StepDrawn(At(0f, true), TFField.Ctf());
        foreach (float z in new[] { 20f, top * 0.5f, top, top - 2f })
            f.StepDrawn(At(z, false), TFField.Ctf());
        f.StepDrawn(At(land, true), TFField.Ctf());
    }

    /// <summary>
    /// A flight begun with a triple jump just ahead that rises past the most a double jump can (117.2) counts a triple jump,
    /// and its rise is kept; the control: the same flight with no triple jump said ahead counts none, nor does a lower one.
    /// </summary>
    [Fact]
    public void ATripleJumpFlownIsCounted()
    {
        var (f, dashes) = Scout(BonkBoy());
        dashes.Dashes(f.Bot, new Tick(0), 2);
        Flight(f, 140f, 130f);
        Assert.Equal((1, 140f), (f.Decider.TripleJumps, f.Decider.TripleJumpBestRise));
        // A double jump's whole rise, tucked (117.2), is no triple jump.
        Flight(f, 115f, 0f);
        Assert.Equal(1, f.Decider.TripleJumps);
        Assert.Equal(117.2f, TFDecider.DoubleJumpTop, 1);
        Assert.Contains(f.Decider.ClassCounters(), c => c.Key == "triple jumps" && c.Value == 1);

        var (g, none) = Scout(BonkBoy());
        none.Dashes(g.Bot, new Tick(0), 1);
        Flight(g, 140f, 130f);
        Assert.Equal(0, g.Decider.TripleJumps);
    }

    // ---------------------------------------------------------------- the store

    [Fact]
    public void ADecideReadsTheNewestReportStampedBeforeIt()
    {
        var store = new TFRouteDashes();
        var bot = new ActorId(4, 1);
        store.Dashes(bot, new Tick(10), 2);
        store.Dashes(bot, new Tick(12), 0);
        Assert.Equal((0, 2, 0, 0), (store.Before(4, new Tick(10)), store.Before(4, new Tick(11)), store.Before(4, new Tick(13)), store.Before(5, new Tick(13))));
    }

    /// <summary>
    /// A new level starts the tick count again: the first triple jump said on it is read, though the last report of the old
    /// level said the same count at a later tick (kept, it would have dropped the new one as no change, and hidden it).
    /// </summary>
    [Fact]
    public void TheFirstTripleJumpOnANewLevelIsRead()
    {
        var store = new TFRouteDashes();
        var bot = new ActorId(4, 1);
        store.Dashes(bot, new Tick(5000), 2);
        ((SourceSharp.Abi.IGameLifecycle)store).OnLevelShutdownPostEntity();
        ((SourceSharp.Abi.IGameLifecycle)store).OnLevelInitPreEntity("cp_granary");
        store.Dashes(bot, new Tick(3), 2);
        Assert.Equal(2, store.Before(4, new Tick(4)));
    }

    /// <summary>
    /// The route reports take part in the level's lifecycle at a stage no other participant of the tf2 mod's list declares
    /// (the lifecycle driver refuses a tie at startup: live, at 156630b010, TFRouteDashes and the server's NetTrafficProfiler
    /// both declared the default 0 and the server did not start). Over every participant the server, the tf2 mod and the AI
    /// assemblies declare; the control: the server's NetTrafficProfiler is among them.
    /// </summary>
    [Fact]
    public void TheRouteReportsHaveALifecycleStageOfTheirOwn()
    {
        Type[] lifecycles = [.. new[] { typeof(SourceSharp.Server.NetTrafficProfiler), typeof(global::TF2.Server.Player.TFServerActorFacts), typeof(SourceSharp.Ai.Server.AiBots), typeof(TFRouteDashes) }
            .SelectMany(t => t.Assembly.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && typeof(SourceSharp.Abi.IGameLifecycle).IsAssignableFrom(t)).Distinct()];
        static int Order(Type t) => ((SourceSharp.Abi.IGameLifecycle)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).LifecycleOrder;
        Assert.Contains(typeof(SourceSharp.Server.NetTrafficProfiler), lifecycles);
        Assert.Equal([nameof(TFRouteDashes)], lifecycles.Where(t => Order(t) == Order(typeof(TFRouteDashes))).Select(t => t.Name));
    }
}
