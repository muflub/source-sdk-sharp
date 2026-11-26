using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// Archetypes and the loadout that follows (plan_bots_teams_tf2 C.3.1, P7 step 3): a bot draws a way of playing per
/// class by its seed and the weights, a pin names one, and the items follow its preference lists, a preferred item
/// standing for its attribute-free reskins; what an archetype does not name stays the seeded draw's.
/// </summary>
public sealed class TFArchetypeTests
{
    private static readonly TFArchetypeTable Two = new(TFClass.Scout,
    [
        new TFArchetype("Flanker", 3f, [new(TFLoadoutSlot.Primary, [13]), new(TFLoadoutSlot.Melee, [0])]),
        new TFArchetype("Harasser", 1f, [new(TFLoadoutSlot.Primary, [45]), new(TFLoadoutSlot.Melee, [44, 0])]),
    ]);

    private static readonly BotWorksItem[] Primaries =
    [
        new(13, "tf_weapon_scattergun", ""), new(200, "tf_weapon_scattergun", ""), new(669, "tf_weapon_scattergun", ""),
        new(45, "tf_weapon_scattergun", "scattergun_has_knockback;mult_clipsize"), new(220, "tf_weapon_handgun_scout_primary", "mult_dmg"),
    ];

    /// <summary>The same seed draws the same archetype, every time: a replay holds.</summary>
    [Fact]
    public void TheSameSeedDrawsTheSameArchetype()
        => Assert.All(Enumerable.Range(0, 50), s => Assert.Same(Two.Draw((ulong)s), Two.Draw((ulong)s)));

    /// <summary>Over 2,000 seeds the draw follows the weights: 3 to 1 is about three quarters Flankers (within 5 %).</summary>
    [Fact]
    public void TheDrawFollowsTheWeights()
    {
        int flankers = Enumerable.Range(0, 2000).Count(s => Two.Draw((ulong)s * 7919UL)!.Name == "Flanker");
        Assert.InRange(flankers / 2000f, 0.70f, 0.80f);
    }

    /// <summary>A pinned archetype is played whatever the seed draws; a name the table lacks is the seed's draw.</summary>
    [Fact]
    public void APinnedArchetypeWins()
    {
        Assert.All(Enumerable.Range(0, 50), s => Assert.Equal("Harasser", Two.For((ulong)s, "harasser")!.Name));
        Assert.Same(Two.Draw(9), Two.For(9, "Tank"));
    }

    /// <summary>A preferred item with no attributes stands for its reskins: the seeds give the stock scattergun and its two reskins, nothing else.</summary>
    [Fact]
    public void APreferredItemStandsForItsReskins()
    {
        TFArchetype flanker = Two.Named("Flanker")!;
        int[] drawn = [.. Enumerable.Range(0, 200).Select(s => TFArchetypeChoice.Choose(flanker, (ulong)s, TFLoadoutSlot.Primary, Primaries)).Distinct().Order()];
        Assert.Equal([13, 200, 669], drawn);
    }

    /// <summary>A preferred item with attributes of its own (the Force-a-Nature) is chosen as itself, never a look-alike.</summary>
    [Fact]
    public void APreferredItemWithAttributesIsChosenAsItself()
        => Assert.All(Enumerable.Range(0, 50), s => Assert.Equal(45, TFArchetypeChoice.Choose(Two.Named("Harasser"), (ulong)s, TFLoadoutSlot.Primary, Primaries)));

    /// <summary>The first preferred item that is offered is taken: the Sandman (44) is not, so the bat (0) is.</summary>
    [Fact]
    public void TheFirstOfferedPreferenceIsTaken()
        => Assert.Equal(0, TFArchetypeChoice.Choose(Two.Named("Harasser"), 1, TFLoadoutSlot.Melee, [new(0, "tf_weapon_bat", ""), new(325, "tf_weapon_bat", "mult_dmg")]));

    /// <summary>A slot the archetype does not name, or one whose preferences none is offered, is left to the seeded draw (-1); so is no archetype.</summary>
    [Fact]
    public void ASlotTheArchetypeDoesNotNameIsTheDraws()
    {
        Assert.Equal(-1, TFArchetypeChoice.Choose(Two.Named("Flanker"), 1, TFLoadoutSlot.Secondary, [new(23, "tf_weapon_pistol", "")]));
        Assert.Equal(-1, TFArchetypeChoice.Choose(Two.Named("Flanker"), 1, TFLoadoutSlot.Primary, [new(220, "tf_weapon_handgun_scout_primary", "mult_dmg")]));
        Assert.Equal(-1, TFArchetypeChoice.Choose(null, 1, TFLoadoutSlot.Primary, Primaries));
    }

    /// <summary>The Soldier's shipped table: every row is drawn by some seed, the stock archetype the most, and it carries the stock launcher (a reskin stands for it).</summary>
    [Fact]
    public void TheSoldiersStockArchetypeIsTheMostDrawnAndCarriesTheStockLauncher()
    {
        TFArchetypeTable soldier = TFSoldierClass.Archetypes();
        var drawn = Enumerable.Range(0, 1000).Select(s => soldier.Draw((ulong)s)!.Name).GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(soldier.Rows.Select(r => r.Name).Order(), drawn.Keys.Order());
        Assert.Equal("Stock", drawn.MaxBy(kv => kv.Value).Key);
        Assert.Equal([18], soldier.Named("Stock")!.Prefer(TFLoadoutSlot.Primary).ToArray());
    }

    private static RosterBot Joined(int slot, int seedIndex, params (string Key, string Value)[] options)
        => new(1, "Alyx", RosterKind.NamedCustom, SkillPresets.TryGet("hard", out SkillProfile? p) ? p! : throw new InvalidOperationException())
        {
            Actor = new ActorId(slot, 7), SeedIndex = seedIndex, Options = options.ToDictionary(o => o.Key, o => o.Value),
        };

    /// <summary>A joined bot's items follow its archetype (a pinned Stock and a pinned DirectHit Soldier here); a slot of no AI bot is left to the draw.</summary>
    [Fact]
    public void ABotsItemsFollowItsArchetype()
    {
        var pins = new TFBotPins();
        pins.Joined(Joined(3, 1, ("class", "soldier"), ("archetype", "Stock")));
        pins.Joined(Joined(5, 2, ("class", "soldier"), ("archetype", "DirectHit")));
        var items = new TFArchetypeItems(pins);
        BotWorksItem[] launchers = [new(18, "tf_weapon_rocketlauncher", ""), new(127, "tf_weapon_rocketlauncher_directhit", "mult_dmg")];
        Assert.Equal(18, items.Choose(3, TFClass.Soldier, TFLoadoutSlot.Primary, launchers));
        Assert.Equal(127, items.Choose(5, TFClass.Soldier, TFLoadoutSlot.Primary, launchers));
        Assert.Equal(-1, items.Choose(4, TFClass.Soldier, TFLoadoutSlot.Primary, launchers));
    }

    /// <summary>
    /// An <c>items=</c> pin (round 2's live proofs) puts its item in the slot whose works rows hold it, over the archetype's;
    /// a slot whose rows hold none of them keeps the archetype's choice.
    /// </summary>
    [Fact]
    public void AnItemsPinChoosesItsItemInTheSlotThatOffersIt()
    {
        var pins = new TFBotPins();
        pins.Joined(Joined(3, 1, ("class", "scout"), ("items", "450")));
        var items = new TFArchetypeItems(pins);
        BotWorksItem[] melee = [new(0, "tf_weapon_bat", ""), new(450, "tf_weapon_bat", "air_dash_count;mult_single_wep_deploy_time;mult_dmg_vs_players")];
        BotWorksItem[] primary = [new(13, "tf_weapon_scattergun", ""), new(772, "tf_weapon_pep_brawler_blaster", "hype_resets_on_jump")];
        Assert.Equal(450, items.Choose(3, TFClass.Scout, TFLoadoutSlot.Melee, melee));
        Assert.NotEqual(772, items.Choose(3, TFClass.Scout, TFLoadoutSlot.Primary, primary));
        Assert.Null(pins.Check(new Dictionary<string, string> { ["items"] = "450,772" }));
        Assert.Contains("no list of defindexes", pins.Check(new Dictionary<string, string> { ["items"] = "atomizer" }), StringComparison.Ordinal);
    }

    /// <summary>The pin's checks: a class by its menu word (heavy too), an archetype only with a class and only one of its own.</summary>
    [Fact]
    public void ThePinsChecksNameWhatIsWrong()
    {
        var pins = new TFBotPins();
        Assert.Null(pins.Check(new Dictionary<string, string> { ["class"] = "heavy" }));
        Assert.Null(pins.Check(new Dictionary<string, string> { ["class"] = "soldier", ["archetype"] = "stock" }));
        Assert.Contains("is no class", pins.Check(new Dictionary<string, string> { ["class"] = "tank" }), StringComparison.Ordinal);
        Assert.Contains("needs a class=", pins.Check(new Dictionary<string, string> { ["archetype"] = "stock" }), StringComparison.Ordinal);
        Assert.Contains("is no scout archetype", pins.Check(new Dictionary<string, string> { ["class"] = "scout", ["archetype"] = "NoClassTakesThisName" }), StringComparison.Ordinal);
    }

    /// <summary>The decider plays the archetype its items followed: the test field's Soldier (seed 7) draws the stock one; a pin's seed is the brain's.</summary>
    [Fact]
    public void TheDeciderPlaysTheArchetypeItsItemsFollowed()
    {
        var f = new TFField();
        f.Step(TFField.Self(new AiVector(300f, 256f, 0f)), TFField.Ctf());
        Assert.Equal("Stock", f.Decider.Archetype);
        var pins = new TFBotPins();
        pins.Joined(Joined(3, 11));
        Assert.Equal(AiRandom.SeedFor(AiBots.MatchSeed ^ SourceSharp.Ai.Server.Senses.ServerAiSession.BrainSalt, 11), pins.OfSlot(3)!.Seed);
    }
}
