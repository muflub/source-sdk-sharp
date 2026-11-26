using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.Tests.ServerNav;
using SourceSharp.Ai.TF2.Nav;
using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A bot's routes follow its own kit (round 2's S2): an Atomizer Scout plans the third jump, a Baby Face's Blaster Scout and a
/// stock Scout the double jump they always did (the BFB's dash costs boost but is free with an empty meter: the
/// coordinator's ruling of 2026-11-22); another player's reach, as one player knows it, stays its
/// class's. The items carry the shipped schema's attributes (items_game.txt: the Atomizer 450's <c>air_dash_count</c> 1, the
/// Baby Face's Blaster 772's <c>hype_resets_on_jump</c> 75).
/// </summary>
public class TFJumpKitTests
{
    private const int ScattergunKind = 1, PistolKind = 2, BatKind = 3, AtomizerKind = 7, BlasterKind = 9;

    private static TFOwnKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind, params (string Class, float Value)[] attributes)
        => new(slot, defindex, itemClass, itemClass, kind, [.. attributes.Select(a => new TFOwnKitAttribute(a.Class, a.Value))]);

    private static readonly TFOwnKitItem Scattergun = Item(TFLoadoutSlot.Primary, 13, "tf_weapon_scattergun", ScattergunKind);
    private static readonly TFOwnKitItem Pistol = Item(TFLoadoutSlot.Secondary, 23, "tf_weapon_pistol", PistolKind);
    private static readonly TFOwnKitItem Bat = Item(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", BatKind);

    private static readonly TFOwnKitItem Atomizer = Item(TFLoadoutSlot.Melee, 450, "tf_weapon_bat", AtomizerKind,
                                                         ("air_dash_count", 1f), ("mult_single_wep_deploy_time", 1.5f), ("mult_dmg_vs_players", 0.85f));

    private static readonly TFOwnKitItem Blaster = Item(TFLoadoutSlot.Primary, 772, "tf_weapon_pep_brawler_blaster", BlasterKind,
                                                        ("mult_player_movespeed", 0.9f), ("boost_on_damage", 1f), ("hype_resets_on_jump", 75f));

    private static readonly TFOwnKitItem[] Stock = [Scattergun, Pistol, Bat], BonkBoy = [Scattergun, Pistol, Atomizer], Bfb = [Blaster, Pistol, Bat];

    [Fact]
    public void AStockScoutDoubleJumps() => Assert.Equal(JumpReach.Double, TFJumpKit.ReachOf(TFClass.Scout, Stock));

    [Fact]
    public void AnAtomizerScoutAlsoTripleJumps()
    {
        Assert.Equal(JumpReach.Double | JumpReach.Triple, TFJumpKit.ReachOf(TFClass.Scout, BonkBoy));
        Assert.Equal((2, 1), (TFJumpKit.Dashes(BonkBoy), TFJumpKit.Dashes(Stock)));
    }

    [Fact]
    public void ABabyFacesBlasterScoutPlansTheDoubleJump() => Assert.Equal(JumpReach.Double, TFJumpKit.ReachOf(TFClass.Scout, Bfb));

    [Fact]
    public void TheNoDashAttributeTakesTheDoubleJumpAway()
        => Assert.Equal(JumpReach.None, TFJumpKit.ReachOf(TFClass.Scout, [Scattergun, Pistol, Item(TFLoadoutSlot.Melee, 0, "tf_weapon_bat", BatKind, ("set_scout_doublejump_disabled", 1f))]));

    /// <summary>A dash count on a worn item is no weapon's to hold: only a weapon in the hands adds dashes (stock reads the active weapon).</summary>
    [Fact]
    public void AWornItemsDashCountGivesNoThirdJump()
        => Assert.Equal(JumpReach.Double, TFJumpKit.ReachOf(TFClass.Scout, [Scattergun, Pistol, Bat, Item(TFLoadoutSlot.Misc, 9999, "tf_wearable", -1, ("air_dash_count", 1f))]));

    [Theory]
    [InlineData(TFClass.Soldier, JumpReach.Blast)]
    [InlineData(TFClass.HeavyWeapons, JumpReach.None)]
    public void AnotherClassReachesAsItsClassDoesWhateverItCarries(TFClass playerClass, JumpReach reach)
        => Assert.Equal(reach, TFJumpKit.ReachOf(playerClass, BonkBoy));

    // ---------------------------------------------------------------- the profile

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
        public IReadOnlyList<TFOwnKitItem> Kit { get; set; } = kit;

        public int Handed { get; set; }

        public int Reads { get; private set; }

        public IReadOnlyList<TFOwnKitItem> KitOf(int entIndex)
        {
            Reads++;
            return Kit;
        }

        public int Version(int entIndex) => Handed;
    }

    private static readonly EntityHandle Bot = new(0x0001_0004u);

    private static TFNavProfile Profile(TFClass playerClass, Kits? kits)
        => new(new Facts(playerClass), null!, null!, new SourceSharp.Ai.TF2.Jump.TFBlastLinks(), kits: kits);

    [Fact]
    public void TheProfileReadsTheBotsOwnKit()
    {
        Assert.Equal(JumpReach.Double | JumpReach.Triple, Profile(TFClass.Scout, new Kits(BonkBoy)).Reach(Bot));
        Assert.Equal(JumpReach.Double, Profile(TFClass.Scout, new Kits(Bfb)).Reach(Bot));
        Assert.Equal(JumpReach.Double, Profile(TFClass.Scout, new Kits(Stock)).Reach(Bot));
    }

    /// <summary>No kit service, or a kit not yet handed: the class's reach, as before the hook.</summary>
    [Fact]
    public void WithNoKitTheReachIsTheClasss()
    {
        Assert.Equal(JumpReach.Double, Profile(TFClass.Scout, null).Reach(Bot));
        Assert.Equal(JumpReach.Double, Profile(TFClass.Scout, new Kits([])).Reach(Bot));
    }

    /// <summary>The kit is read once per kit handed: a drive step asks every tick, and a new kit (a resupply) is read again.</summary>
    [Fact]
    public void TheKitIsReadAgainOnlyWhenANewOneIsHanded()
    {
        var kits = new Kits(Stock);
        TFNavProfile profile = Profile(TFClass.Scout, kits);
        for (int i = 0; i < 5; i++)
            Assert.Equal(JumpReach.Double, profile.Reach(Bot));
        Assert.Equal(1, kits.Reads);
        kits.Kit = BonkBoy;
        Assert.Equal(JumpReach.Double, profile.Reach(Bot));
        kits.Handed++;
        Assert.Equal(JumpReach.Double | JumpReach.Triple, profile.Reach(Bot));
        Assert.Equal(2, kits.Reads);
    }

    /// <summary>What one player knows of another's reach is the class's: the class view does not read a kit.</summary>
    [Fact]
    public void TheClassViewIsUnchanged() => Assert.Equal(JumpReach.Double, TFNavProfile.ReachOf(TFClass.Scout));

    // ---------------------------------------------------------------- the routes, over TF2's numbers

    /// <summary>A floor, a ledge 90 u up (a double jump) and one 120 u up (a triple jump), derived by TF2's numbers.</summary>
    private static (DerivedNav D, int Floor, int Mid, int High) Course()
    {
        var s = new FakeNavSource();
        int floor = s.Add(0, 0, 0);
        int mid = s.Add(0, 1, 90);
        int high = s.Add(1, 0, 120);
        s.JumpLink(floor, mid);
        s.JumpLink(floor, high);
        return (NavGraphFromNav3d.Derive(s, TFNavProfile.ModNumbers, NavEdgeCosts.Default, []), floor, mid, high);
    }

    private static bool Routes(DerivedNav d, int from, int to, IReadOnlyList<TFOwnKitItem> kit)
    {
        var field = new FlowField();
        var state = new NavObstacleState(d.Graph.ObstacleCount) { Reach = Profile(TFClass.Scout, new Kits(kit)).Reach(Bot) };
        field.Build(d.Graph, new CellId(d.CellOfLeaf[to]), ClearanceClass.Crouch, state);
        return float.IsFinite(field.CostAt(d.CellOfLeaf[from]));
    }

    [Fact]
    public void AnAtomizerScoutsRoutesTakeTheTripleJumpAStockScoutsDoNot()
    {
        var (d, floor, _, high) = Course();
        Assert.Equal((true, false), (Routes(d, floor, high, BonkBoy), Routes(d, floor, high, Stock)));
    }

    [Fact]
    public void ABabyFacesBlasterScoutsRoutesTakeTheDoubleJumpAndNoTriple()
    {
        var (d, floor, mid, high) = Course();
        Assert.Equal((true, true, false), (Routes(d, floor, mid, Stock), Routes(d, floor, mid, Bfb), Routes(d, floor, high, Bfb)));
    }

    [Fact]
    public void TF2sNumbersReachTheGroundJumpAndTwoDashes()
        => Assert.Equal(142.2f, MathF.Round(TFNavProfile.ModNumbers.MaxTripleJumpRise, 1));
}
