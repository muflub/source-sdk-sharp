using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Ai.TF2.Nav;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The route planner budgets a blast jump with the bot's own kit (round 2's S13): each link costs what the bot's kit pays
/// for its blast, the share the Soldier's tree pays at the link (the Gunboats 133's <c>rocket_jump_dmg_reduction</c> 0.4,
/// the Direct Hit 127's <c>mult_dmg</c> 1.25, nothing under the Rocket Jumper 237's <c>no_self_blast_dmg</c>), against the
/// tree's floor; a stock Soldier's routes are what they were.
/// </summary>
public class TFJumpBudgetTests
{
    private const int LauncherKind = 3, ShotgunKind = 5, ShovelKind = 7;

    private static TFOwnKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params (string Class, float Value)[] attributes)
        => new(slot, defindex, itemClass, itemClass, kind, [.. attributes.Select(a => new TFOwnKitAttribute(a.Class, a.Value))]);

    private static readonly TFOwnKitItem Launcher = Item(TFLoadoutSlot.Primary, 18, "tf_weapon_rocketlauncher", LauncherKind);
    private static readonly TFOwnKitItem Shotgun = Item(TFLoadoutSlot.Secondary, 10, "tf_weapon_shotgun_soldier", ShotgunKind);
    private static readonly TFOwnKitItem Shovel = Item(TFLoadoutSlot.Melee, 6, "tf_weapon_shovel", ShovelKind);
    private static readonly TFOwnKitItem Gunboats = Item(TFLoadoutSlot.Secondary, 133, "tf_wearable", -1, ("rocket_jump_dmg_reduction", 0.4f));
    private static readonly TFOwnKitItem DirectHit = Item(TFLoadoutSlot.Primary, 127, "tf_weapon_rocketlauncher_directhit", LauncherKind, ("mult_dmg", 1.25f));

    private static readonly TFOwnKitItem RocketJumper = Item(TFLoadoutSlot.Primary, 237, "tf_weapon_rocketlauncher", LauncherKind,
                                                             ("mult_dmg", 0f), ("no_self_blast_dmg", 2f), ("mult_maxammo_primary", 3f));

    private static readonly TFOwnKitItem[] Stock = [Launcher, Shotgun, Shovel], Boats = [Launcher, Gunboats, Shovel], Jumper = [RocketJumper, Shotgun, Shovel];

    [Fact]
    public void TheStockKitPaysTheWholeBlast() => Assert.Equal(1f, TFJumpBudget.CostShare(Stock));

    [Fact]
    public void TheGunboatsPayFourTenths() => Assert.Equal(0.4f, TFJumpBudget.CostShare(Boats), 5);

    [Fact]
    public void TheRocketJumperPaysNothing() => Assert.Equal(0f, TFJumpBudget.CostShare(Jumper));

    /// <summary>The launcher's damage scales its own blast, and the Gunboats multiply over it: as the tree's share.</summary>
    [Fact]
    public void TheDirectHitPaysItsLaunchersDamageAndTheGunboatsMultiply()
        => Assert.Equal((1.25f, 0.5f), (TFJumpBudget.CostShare([DirectHit, Shotgun, Shovel]), TFJumpBudget.CostShare([DirectHit, Gunboats, Shovel])));

    // ---------------------------------------------------------------- the spends

    private static readonly NavLinkCosts Costs = new() { RunSpeed = 240f };

    /// <summary>A gap in a floor, crossed by one link whose blast costs <paramref name="cost"/>.</summary>
    private static (NavGraph G, int[,] Ids, JumpLinkSet Set) Gap(float cost)
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse("..... .....");
        var recipe = new JumpRecipe(new Vector(144f, 16f, 0f), new Vector(208f, 16f, 0f), 0f, 60f, 180f, 2, false);
        return (g, ids, JumpLinkSet.Of(g, [new JumpLink(ids[4, 0], ids[6, 0], recipe, JumpFamily.Up, 1f, 2f, 1.5f, cost, null)], Costs));
    }

    /// <summary>
    /// A link that lands by a pack spends its blast less what the pack gives back past the fall (published 10 for a blast of
    /// 40); at 0.4 the blast is 16 and the pack's part the same, so it spends 10 - 24 = -14. The stock share is the links' own.
    /// </summary>
    [Fact]
    public void ABotsSpendIsThePublishedSpendLessItsBlastsUnpaidShare()
    {
        var (g, _, set) = Gap(40f);
        JumpLinkSet byPack = set.WithSpends(g, [10f], Costs);
        Assert.Equal(-14f, Assert.Single(TFJumpBudget.Spends(byPack, 0.4f)!), 4);
        Assert.Equal(16f, Assert.Single(TFJumpBudget.Spends(set, 0.4f)!), 4);
        Assert.Null(TFJumpBudget.Spends(set, 1f));
    }

    // ---------------------------------------------------------------- the profile and the routes

    private sealed class Facts(TFClass playerClass) : ITFActorFacts
    {
        public bool TryRead(int playerIndex, out TFActorFacts facts)
        {
            facts = new TFActorFacts(playerClass, TFTeam.Red, TFClass.Civilian, TFTeam.Unassigned, 0, 0f, 0, false, false, false, false, false, false);
            return true;
        }
    }

    private sealed class Kits(IReadOnlyList<TFOwnKitItem> kit) : ITFOwnKit
    {
        public IReadOnlyList<TFOwnKitItem> KitOf(int entIndex) => kit;

        public int Version(int entIndex) => 1;
    }

    private static readonly EntityHandle Bot = new(0x0001_0004u);

    private static NavLinkAllowance Allowance(JumpLinkSet set, int health, IReadOnlyList<TFOwnKitItem>? kit)
    {
        IEngineEntities entities = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, _) => m.Name == nameof(IEngineEntities.GetHealth)
            ? health : throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m));
        var published = new TFBlastLinks { Set = set };
        return new TFNavProfile(new Facts(TFClass.Soldier), null!, entities, published, kits: kit is null ? null : new Kits(kit)).LinksFor(Bot);
    }

    /// <summary>Whether a search from the floor's west end to its east end crosses, with the allowance the profile gives at <paramref name="health"/>.</summary>
    private static bool Crosses(int health, IReadOnlyList<TFOwnKitItem>? kit)
    {
        var (g, ids, set) = Gap(40f);
        NavLinkAllowance a = Allowance(set, health, kit);
        var state = new NavObstacleState(g.ObstacleCount) { Reach = JumpReach.Blast, Links = a.Links, LinkHealth = a.Health, LinksShut = a.Shut, LinkSpends = a.Spends };
        var field = new FlowField();
        field.Build(g, new CellId(ids[10, 0]), ClearanceClass.Stand, state);
        return float.IsFinite(field.CostAt(ids[0, 0]));
    }

    /// <summary>At 110 health a stock Soldier may spend 20 on a link and the blast costs 40; with the Gunboats it costs 16.</summary>
    [Fact]
    public void AGunboatsSoldiersRouteTakesALinkAStockSoldiersRefusesAtTheSameHealth()
    {
        Assert.Equal(110f - TFDecider.RocketJumpHealthFloor, Allowance(Gap(40f).Set, 110, Boats).Health);
        Assert.Equal((true, false), (Crosses(110, Boats), Crosses(110, Stock)));
        // The control: with health enough both cross.
        Assert.Equal((true, true), (Crosses(200, Boats), Crosses(200, Stock)));
    }

    [Fact]
    public void ARocketJumpersRouteTakesTheLinkOverItsFloor() => Assert.Equal((true, false), (Crosses(91, Jumper), Crosses(91, Stock)));

    /// <summary>A stock Soldier's allowance is the one it was before the hook: no spends of its own, with a kit read or none.</summary>
    [Fact]
    public void AStockSoldiersAllowanceIsUnchanged()
    {
        JumpLinkSet set = Gap(40f).Set;
        Assert.Equal(Allowance(set, 150, null), Allowance(set, 150, Stock));
        Assert.Null(Allowance(set, 150, Stock).Spends);
    }

    /// <summary>The spends are worked once per set and share: two Gunboats Soldiers share one list.</summary>
    [Fact]
    public void TheSpendsAreWorkedOncePerSetAndShare()
    {
        JumpLinkSet set = Gap(40f).Set;
        IEngineEntities entities = SourceSharp.Ai.Tests.Senses.FakeEngine.Of<IEngineEntities>((m, _) => m.Name == nameof(IEngineEntities.GetHealth)
            ? 150 : throw SourceSharp.Ai.Tests.Senses.FakeEngine.Unasked(m));
        var profile = new TFNavProfile(new Facts(TFClass.Soldier), null!, entities, new TFBlastLinks { Set = set }, kits: new Kits(Boats));
        Assert.Same(profile.LinksFor(Bot).Spends, profile.LinksFor(new EntityHandle(0x0001_0005u)).Spends);
    }
}
