using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;
using static SourceSharp.Ai.Tests.TF2.TFDemomanTests;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Loch-n-Load and the combo (plan_bots_teams_tf2 P7; .scratch/p7-research/demoman.md A4, A6): the Loch's pipes, which
/// break on the world, fought nearer and turned on a sentry; the combo's stickies in the fight further out, the
/// Quickiebomb's detonated in the air, and its keeping with the team.
/// </summary>
public sealed class TFDemomanLochComboTests
{
    private static TFHud LochKit()
        => new([Item(TFLoadoutSlot.Primary, 308, "tf_weapon_grenadelauncher", Pipes, new TFOwnKitAttribute("set_detonate_mode", 2f), new TFOwnKitAttribute("mult_clipsize", 0.75f)),
                Item(TFLoadoutSlot.Secondary, 20, "tf_weapon_pipebomblauncher", Stickies), Item(TFLoadoutSlot.Melee, 1, "tf_weapon_bottle", Bottle)], [], []);

    private static TFHud ComboKit(bool quickie = false)
        => new([Item(TFLoadoutSlot.Primary, 1151, "tf_weapon_grenadelauncher", Pipes, new TFOwnKitAttribute("grenade_no_bounce", 1f)),
                quickie ? Item(TFLoadoutSlot.Secondary, 1150, "tf_weapon_pipebomblauncher", Stickies, new TFOwnKitAttribute("sticky_arm_time", -0.2f))
                        : Item(TFLoadoutSlot.Secondary, 20, "tf_weapon_pipebomblauncher", Stickies),
                Item(TFLoadoutSlot.Melee, 1, "tf_weapon_bottle", Bottle)], [], []);

    /// <summary>The Loch-n-Load's detonate mode reads as pipes that shatter; the Iron Bomber's as pipes that do not roll; the Quickiebomb's arm as air detonation.</summary>
    [Fact]
    public void TheKitsReadAsTheirItems()
    {
        Assert.Equal((true, false, false), (TFDemomanKit.Of(LochKit()).Shatter, TFDemomanKit.Of(LochKit()).NoRoll, TFDemomanKit.Of(LochKit()).AirDetonate));
        Assert.Equal((true, true), (TFDemomanKit.Of(ComboKit()).NoRoll, TFDemomanKit.Of(ComboKit(quickie: true)).AirDetonate));
    }

    /// <summary>At 900 u with the pipes in hand the Loch closes (its band ends at 750), where the stock Hybrid holds (its ends at 1000).</summary>
    [Fact]
    public void TheLochFightsNearerThanTheHybrid()
    {
        string Verdict(TFHud hud)
        {
            var f = new TFField(weapons: Weapons) { Hud = hud };
            f.StepDrawn(Self(Mid, Pipes, 3), null, Drawn(Enemy, Mid + new AiVector(900f, 0f, 0f), TFField.Blue));
            return f.Decider.Doing.Verdict;
        }

        Assert.Equal(("closing", "holding range"), (Verdict(LochKit()), Verdict(StockKit())));
    }

    /// <summary>At a sentry's stand the Loch takes out its pipes for it, where the Hybrid takes its stickies.</summary>
    [Fact]
    public void TheLochTurnsItsPipesOnASentry()
    {
        int Taken(TFHud hud, int holding)
        {
            var f = new TFField(weapons: Weapons) { Hud = hud };
            for (int n = 0; n < 3; n++)
            {
                IntentCommand i = f.StepSeeing(Self(Mid, holding, holding == Pipes ? 3 : 8), null, [Sentry(Mid + new AiVector(1250f, 0f, 0f))]);
                if (i.Action.Kind == ActionKind.SelectWeapon)
                    return i.Action.WeaponSlot - EngineBotSelf.ByKind;
            }

            return -1;
        }

        Assert.Equal((Pipes, Stickies), (Taken(LochKit(), Stickies), Taken(StockKit(), Pipes)));
    }

    /// <summary>The combo fights a target 500 u off with its stickies, where the stock Hybrid takes its pipes.</summary>
    [Fact]
    public void TheComboFightsWithTheStickiesFurtherOut()
    {
        int Taken(ulong seed, TFHud hud)
        {
            var f = new TFField(seed: seed, weapons: Weapons) { Hud = hud };
            IntentCommand i = f.StepDrawn(Self(Mid, Bottle, 0), null, Drawn(Enemy, Mid + new AiVector(500f, 0f, 0f), TFField.Blue));
            return i.Action.Kind == ActionKind.SelectWeapon ? i.Action.WeaponSlot - EngineBotSelf.ByKind : -1;
        }

        Assert.Equal((Stickies, Pipes), (Taken(TFDemomanTrapTests.SeedDrawing(TFDemomanClass.Combo), ComboKit()), Taken(TFDemomanTrapTests.SeedDrawing(TFDemomanClass.Hybrid), StockKit())));
    }

    /// <summary>
    /// The Quickiebomb's sticky seen in flight by an enemy is detonated in the air once armed (0.6 s), before a stock sticky
    /// in flight would be (0.8 s and its flight).
    /// </summary>
    [Fact]
    public void TheQuickiebombDetonatesInTheAir()
    {
        float SecondsToDetonate(TFHud hud)
        {
            var f = new TFField(weapons: Weapons) { Hud = hud };
            AiVector landing = Mid + new AiVector(300f, 0f, 0f);
            int at = DecidesToDetonate(f, _ => [Drawn(Enemy, landing, TFField.Blue)],
                                       // Seen moving through the air 60 u over the enemy's middle, never at rest.
                                       l => [Sticky(77, l + new AiVector(0f, 0f, 60f), TFField.Red, new AiVector(300f, 0f, -50f))]);
            return at < 0 ? float.PositiveInfinity : at * DecideSchedule.Period * 0.015f;
        }

        float quickie = SecondsToDetonate(ComboKit(quickie: true)), stock = SecondsToDetonate(ComboKit());
        Assert.True(quickie < 0.8f && quickie < stock, $"quickie {quickie} s, stock {stock} s");
    }

    /// <summary>The combo weighs the team stance's branch up and the points' down; the Hybrid weighs both as the general play.</summary>
    [Fact]
    public void TheComboKeepsWithTheTeam()
    {
        (float Team, float Points) Weights(string archetype, TFHud hud)
        {
            var f = new TFField(seed: TFDemomanTrapTests.SeedDrawing(archetype), weapons: Weapons, tune: p => p with { ScoreNoise = 0f }) { Hud = hud };
            f.Step(Self(Mid, Pipes, 4), null);
            return (f.Decider.Weigh(TFDecider.TeamBranch, 0.5f), f.Decider.Weigh(TFDecider.PointsBranch, 0.5f));
        }

        (float comboTeam, float comboPoints) = Weights(TFDemomanClass.Combo, ComboKit());
        (float hybridTeam, float hybridPoints) = Weights(TFDemomanClass.Hybrid, StockKit());
        Assert.True(comboTeam / comboPoints > 1.5f * (hybridTeam / hybridPoints), $"combo {comboTeam}/{comboPoints}, hybrid {hybridTeam}/{hybridPoints}");
    }
}

/// <summary>The combo's loadout (review finding 6): its draw fields the Quickiebomb, so its air detonation plays live, not only in a fact.</summary>
public sealed class TFDemomanComboDrawTests
{
    [Fact]
    public void TheComboDrawsTheQuickiebomb()
    {
        TFArchetype combo = TFDemomanClass.Archetypes().Named(TFDemomanClass.Combo)!;
        IReadOnlyList<BotWorksItem> works =
        [
            new(20, "tf_weapon_pipebomblauncher", "", "tf_weapon_pipebomblauncher"),
            new(1150, "tf_weapon_pipebomblauncher", "sticky_arm_time;stickybomb_charge_rate", "tf_weapon_pipebomblauncher"),
        ];
        Assert.Equal(1150, TFArchetypeChoice.Choose(combo, 7, TFLoadoutSlot.Secondary, works));
    }
}
