using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Shared;
using TF2.Server.Econ;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Huntsman (round2.md §5 "Sniper", the research's archetype C): the draw begun with the eye on the target, held while it
/// charges and let go at a full charge with the crosshair on the led head (stock's 0.95, tf_bot_behavior.cpp:1382), or near
/// at half a charge; never held into the tired shot; lowered when the target is gone; still while drawn; the lead by the
/// arrow's flight; the bow's targets.
/// </summary>
public sealed class TFSniperBowTests
{
    internal const int Bow = 64;
    private static readonly ActorId Enemy = new(5, 0x4000_0001), Scout = new(6, 0x4000_0001);
    private static readonly AiVector Me0 = new(1024f, 256f, 0f);

    private static readonly WeaponKnowledge Weapons = new([
        .. TFSniperPlayTests.Weapons.All,
        new WeaponFacts(Bow, TFSniperKit.BowClass, 0, 2f, 0f, false, false, 2600f, ProjectileGravity: 80f, ChargeSeconds: 1f),
    ]);

    private static TFKitItem Item(TFLoadoutSlot slot, int defindex, string itemClass, int kind) => new(slot, defindex, itemClass, kind, []);

    private static TFHud Kit(float draw, float jar = 0f)
        => new([Item(TFLoadoutSlot.Primary, 56, TFSniperKit.BowClass, Bow), Item(TFLoadoutSlot.Secondary, 58, TFSniperKit.JarClass, TFSniperPlayTests.JarKind),
                Item(TFLoadoutSlot.Melee, 3, "tf_weapon_club", TFSniperPlayTests.Kukri)],
               [new TFMeter(TFSniperMeters.Zoomed, 0f), new TFMeter(TFSniperMeters.Charge, 0f), new TFMeter(TFSniperMeters.Jar, jar), new TFMeter(TFSniperMeters.Draw, draw)], []);

    private static TFField Field(bool exact = false)
        => new(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Huntsman), weapons: Weapons, tune: exact ? p => p with { AimErrorDeg = 0f } : null);

    private static bool Is(TFField f, in IntentCommand i, string row)
        => i.Action.Kind == ActionKind.Ability && f.Memory.World.Map.Abilities.TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow r) && r.Name == row;

    /// <summary>The bot, a red Sniper holding the bow, looking at <paramref name="look"/> (or along +x).</summary>
    private static SelfState Me(AiVector? look = null)
    {
        SelfState s = TFField.Self(Me0, health: 125, clip: 1, reserve: 12, weapon: Bow, playerClass: TFClass.Sniper);
        AiVector eye = Me0 + new AiVector(0f, 0f, 75f);
        return s with { Eye = look is { } p ? BotCommandApplier.AnglesTo(eye, p) : default };
    }

    private static ActorFacts Target(float distance, AiVector velocity = default, TFClass cls = TFClass.Soldier, ActorId? id = null)
        => new(id ?? Enemy, Me0 + new AiVector(distance, 0f, 0f), velocity, TFSniperPlayTests.Rifle, ActorActivity.None, 1f, TFField.Blue, (int)cls);

    /// <summary>Steps with the target in sight and the bow drawn <paramref name="draw"/> s, the view on the decide's own aim (the eye keeps up).</summary>
    private static IntentCommand[] Drawing(TFField f, float distance, float draw, int decides, bool onAim = true, AiVector velocity = default)
    {
        var target = Target(distance, velocity);
        var all = new IntentCommand[decides];
        AiVector? look = onAim ? null : Me0 + new AiVector(0f, 3000f, 0f);
        for (int n = 0; n < decides; n++)
        {
            f.Hud = Kit(draw);
            all[n] = f.StepDrawn(Me(look), TFField.Ctf(), target);
            if (onAim)
                look = all[n].Look.Point;
        }

        return all;
    }

    // ------------------------------------------------------------------ the release rule

    /// <summary>A full draw (stock's 0.95) goes at any range; 0.9 of one at 1,000 u is held.</summary>
    [Fact]
    public void AFullDrawGoesAndALesserOneIsHeldFar()
        => Assert.Equal((true, false), (TFSniperBowRule.Ready(0.95f, 1000f), TFSniperBowRule.Ready(0.9f, 1000f)));

    /// <summary>Near (under 400 u), half a draw goes; less than half is held.</summary>
    [Fact]
    public void NearHalfADrawGoes()
        => Assert.Equal((true, false), (TFSniperBowRule.Ready(0.5f, 300f), TFSniperBowRule.Ready(0.4f, 300f)));

    // ------------------------------------------------------------------ the draw

    /// <summary>The draw does not start with the eye far off the target (the eye's reaction would cut it and let a weak arrow go).</summary>
    [Fact]
    public void NoDrawWithTheEyeOffTheTarget()
        => Assert.DoesNotContain(Drawing(Field(), 1000f, 0f, 8, onAim: false), i => i.Action.Kind == ActionKind.Fire);

    /// <summary>With the eye on the target, after a settle, the draw starts: the trigger held (a Fire held as FireStyle.Hold, so the bow keeps charging and nothing lets it go).</summary>
    [Fact]
    public void WithTheEyeOnTheTargetTheDrawStarts()
        => Assert.Contains(Drawing(Field(), 1000f, 0f, 8), i => i.Action is { Kind: ActionKind.Fire, Style: FireStyle.Hold });

    /// <summary>Half drawn at 1,000 u, on aim: the trigger stays held (the bow charges on).</summary>
    [Fact]
    public void AHalfDrawFarIsHeld()
        => Assert.All(Drawing(Field(), 1000f, 0.5f, 4)[1..], i => Assert.Equal(ActionKind.Fire, i.Action.Kind));

    /// <summary>Fully drawn at 1,000 u with the view on the led head: the trigger goes up (no fire, no one-shot), and the arrow goes.</summary>
    [Fact]
    public void AFullDrawOnAimIsLetGo()
        => Assert.Contains(Drawing(Field(), 1000f, 1.2f, 4)[1..], i => i.Action.Kind == ActionKind.None);

    /// <summary>Fully drawn but with the view far off, the trigger is held: the arrow waits for the crosshair.</summary>
    [Fact]
    public void AFullDrawOffAimIsHeld()
        => Assert.All(Drawing(Field(), 1000f, 1.2f, 4, onAim: false), i => Assert.Equal(ActionKind.Fire, i.Action.Kind));

    /// <summary>Drawn past 4.5 s with the crosshair still off, the bow is lowered (the tired shot at 5 s goes wild).</summary>
    [Fact]
    public void ADrawPastFourAndAHalfSecondsIsLowered()
    {
        TFField f = Field();
        IntentCommand[] all = Drawing(f, 1000f, 4.6f, 3, onAim: false);
        Assert.Contains(all, i => Is(f, i, TFSniperClass.BowLower));
    }

    /// <summary>The target gone for longer than the draw is kept for its peek back (1.5 s), the bow is lowered.</summary>
    [Fact]
    public void ATargetGoneTheDrawIsLowered()
    {
        TFField f = Field();
        Drawing(f, 1000f, 0.6f, 3);
        var lowered = new List<bool>();
        for (int n = 0; n < 30; n++)
        {
            f.Hud = Kit(0.6f + n * 0.09f);
            lowered.Add(Is(f, f.StepDrawn(Me(), TFField.Ctf()), TFSniperClass.BowLower));
        }

        int first = lowered.IndexOf(true);
        Assert.True(first > 0, "never lowered");
        Assert.True(first * DecideSchedule.Period * 0.015f >= 1.4f, $"lowered at decide {first}");
    }

    /// <summary>
    /// Drawn in the fight (no objective on the HUD) it holds still: stock's CTFBotMainAction::Dodge does not dodge a drawing bot.
    /// Drawn on the way to the enemy's flag (the fetch branch walks it), it keeps walking: the review's M2, the draw stopped every branch.
    /// </summary>
    [Fact]
    public void DrawnItHoldsStillInTheFightAndWalksOnElsewhere()
    {
        (string Branch, MoveKind Move, string Verdict) Run(ObjectiveKnowledge? objectives)
        {
            TFField f = Field();
            var target = Target(700f);
            IntentCommand last = default;
            for (int n = 0; n < 3; n++)
            {
                f.Hud = Kit(0.5f);
                last = f.StepDrawn(Me(), objectives, target);
            }

            return (f.Branch, last.Move.Kind, f.Decider.Doing.Verdict);
        }

        var fight = Run(null);
        var fetch = Run(TFField.Ctf());
        Assert.Equal((TFDecider.Fight, MoveKind.Hold, "drawing"), fight);
        Assert.True(fetch.Branch != TFDecider.Fight && fetch.Move != MoveKind.Hold, $"{fetch}");
    }

    // ------------------------------------------------------------------ the aim

    /// <summary>The aim is the head's height of the class drawn, with an arc's lift for the arrow's drop, not the body's middle.</summary>
    [Fact]
    public void TheAimIsAtTheHead()
    {
        IntentCommand last = Drawing(Field(exact: true), 1000f, 0.5f, 2)[^1];
        Assert.Equal((LookKind.Actor, AimPoint.Head), (last.Look.Kind, last.Look.Aim));
        // A Soldier's eye is 68 u up, his middle 41: with no error the aim is the eye and the arc's few units over it.
        Assert.InRange(last.Look.Point.Z, 66f, 90f);
    }

    /// <summary>A target running across at 300 u/s 1,500 u off is led by the arrow's flight: the aim is ahead of where it stands, by far more than the still target's.</summary>
    [Fact]
    public void AMovingTargetIsLedByTheArrowsFlight()
    {
        float Across(AiVector velocity) => Drawing(Field(), 1500f, 0.5f, 2, velocity: velocity)[^1].Look.Point.Y - Me0.Y;
        float still = Across(default), running = Across(new AiVector(0f, 300f, 0f));
        Assert.True(running - still > 60f, $"still {still} running {running}");
    }

    // ------------------------------------------------------------------ the targets

    private static ActorId Picked(ActorFacts a, ActorFacts b)
    {
        TFField f = Field();
        IntentCommand last = default;
        for (int n = 0; n < 4; n++)
        {
            f.Hud = Kit(0f);
            last = f.StepDrawn(Me(), TFField.Ctf(), a, b);
        }

        return last.Look.Actor;
    }

    /// <summary>The bow leaves a Scout (the guide: weak against Scouts): a Heavy 2,000 u off over a Scout at 450 u, whom nearness alone would pick.</summary>
    [Fact]
    public void TheBowLeavesANearScoutForAFarHeavy()
        => Assert.Equal(Enemy, Picked(Target(2000f, cls: TFClass.HeavyWeapons), Target(450f, cls: TFClass.Scout, id: Scout)));

    /// <summary>The bow prefers a Heavy (good against him): one at 1,400 u over an Engineer at 900 u, whom nearness alone would pick.</summary>
    [Fact]
    public void TheBowPrefersAFarHeavyToANearEngineer()
        => Assert.Equal(Enemy, Picked(Target(1400f, cls: TFClass.HeavyWeapons), Target(900f, cls: TFClass.Engineer, id: Scout)));

    /// <summary>The Huntsman archetype carries the bow (56, standing for 1005 and 1092), Jarate and the Kukri.</summary>
    [Fact]
    public void TheHuntsmanCarriesTheBow()
        => Assert.Equal([56, 58, 3], TFSniperClass.Archetypes().Named(TFSniperClass.Huntsman)!.Slots.Select(s => s.Prefer[0]));

    /// <summary>A bow in hand drawn at 10.0 s shows 1.3 s at 11.3 s; undrawn, 0 (the meter's arithmetic).</summary>
    [Fact]
    public void TheDrawMeterIsTheTimeSinceTheDrawBegan()
    {
        var drawn = new WeaponNetState();
        drawn.BindLocal();
        drawn.BehaviourMeter = 10f;
        var undrawn = new WeaponNetState();
        undrawn.BindLocal();
        Assert.Equal((1.3f, 0f), (MathF.Round(TFSniperMeters.DrawShown(drawn, 11.3f), 3), TFSniperMeters.DrawShown(undrawn, 11.3f)));
    }
    /// <summary>
    /// The arrow gone, the bow's clip is 0 until it nocks the next: with a Soldier at 400 u the hands keep the bow (the review's H1:
    /// the general hands took the secondary, the jar, for a dry primary, and threw it at one target).
    /// </summary>
    [Fact]
    public void AfterAnArrowTheBowIsKept()
    {
        TFField f = Field();
        var soldier = Target(400f);
        SelfState dry = Me() with { Clip = 0 };
        var actions = new List<IntentCommand>();
        for (int n = 0; n < 12; n++)
        {
            f.Hud = Kit(0f);
            actions.Add(f.StepDrawn(dry, TFField.Ctf(), soldier));
        }

        Assert.DoesNotContain(actions, i => i.Action.Kind == ActionKind.SelectWeapon);
    }

    /// <summary>The jar in hand outside its own throw (one Soldier, no group: no rule chose it): it is not thrown, and the bow is taken back.</summary>
    [Fact]
    public void AJarOutOfItsOwnThrowIsNotThrown()
    {
        TFField f = Field();
        var soldier = Target(600f);
        SelfState jar = Me() with { ActiveWeapon = TFSniperPlayTests.JarKind };
        var actions = new List<IntentCommand>();
        for (int n = 0; n < 12; n++)
        {
            f.Hud = Kit(0f);
            actions.Add(f.StepDrawn(jar, TFField.Ctf(), soldier));
        }

        Assert.DoesNotContain(actions, i => i.Action.Kind == ActionKind.Fire);
        Assert.Contains(actions, i => i.Action.Kind == ActionKind.SelectWeapon);
    }
    /// <summary>
    /// An arrow is counted when the bow's own rounds fall (clip 1 to 0), whoever let the trigger go: a loose the decider did not
    /// ask for is an arrow, and no release of its own (the review's L5: the counter counted let-go decides).
    /// </summary>
    [Fact]
    public void AnArrowIsCountedWhenTheRoundsFall()
    {
        TFField f = Field();
        var soldier = Target(1000f);
        AiVector? look = Me0 + new AiVector(0f, 3000f, 0f);
        for (int n = 0; n < 3; n++)
        {
            f.Hud = Kit(0.6f);
            f.StepDrawn(Me(look), TFField.Ctf(), soldier);
        }

        f.Hud = Kit(0f);
        f.StepDrawn(Me(look) with { Clip = 0 }, TFField.Ctf(), soldier);
        long Count(string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;
        Assert.Equal((1L, 0L), (Count("sniper.arrows"), Count("sniper.bow_releases")));
    }
    /// <summary>The decider's own release, then the rounds falling: one arrow and one release, not two arrows.</summary>
    [Fact]
    public void AReleaseAndItsArrowAreOneArrow()
    {
        TFField f = Field();
        IntentCommand[] all = Drawing(f, 1000f, 1.2f, 4);
        Assert.Contains(all, i => i.Action.Kind == ActionKind.None);
        f.Hud = Kit(0f);
        f.StepDrawn(Me(all[^1].Look.Point) with { Clip = 0 }, TFField.Ctf(), Target(1000f));
        long Count(string name) => f.Decider.ClassCounters().Single(kv => kv.Key == name).Value;
        Assert.Equal(1L, Count("sniper.arrows"));
        Assert.True(Count("sniper.bow_releases") >= 1);
    }
    /// <summary>Drawn on a Heavy at 1,100 u, a Soldier steps into sight at 420 u (nearer, the bow's second-best class, whom the pick would take): the draw stays on the Heavy.</summary>
    [Fact]
    public void ADrawnArrowsTargetIsHeld()
    {
        TFField f = Field();
        ActorFacts heavy = Target(1100f, cls: TFClass.HeavyWeapons), soldier = Target(420f, cls: TFClass.Soldier, id: Scout);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = Kit(0f);
            f.StepDrawn(Me(), TFField.Ctf(), heavy);
        }

        IntentCommand last = default;
        for (int n = 0; n < 4; n++)
        {
            f.Hud = Kit(0.3f + n * 0.09f);
            last = f.StepDrawn(Me(), TFField.Ctf(), heavy, soldier);
        }

        Assert.Equal(Enemy, last.Look.Actor);
    }

    /// <summary>Drawn, the target steps out of sight: through the draw's hold the look stays on him where he was last seen, so his showing again is no new stimulus.</summary>
    [Fact]
    public void ALostTargetsDrawLooksWhereHeWasLastSeen()
    {
        TFField f = Field();
        Drawing(f, 1000f, 0.6f, 3);
        f.Hud = Kit(0.7f);
        IntentCommand gone = f.StepDrawn(Me(), TFField.Ctf());
        Assert.Equal((ActionKind.Fire, LookKind.Actor, Enemy), (gone.Action.Kind, gone.Look.Kind, gone.Look.Actor));
    }
    /// <summary>With the bow, under 4 arrows in a lull and an ammo pack in reach, it goes for ammo; the rifle Sniper with as few rounds does not (the errand is the bow's).</summary>
    [Fact]
    public void TheBowFetchesAmmoUnderFourArrows()
    {
        string Verdict(bool bow)
        {
            ItemSpawn[] packs = [new ItemSpawn(0, "item_ammopack_medium", ItemKind.AmmoMedium, Me0 + new AiVector(400f, 0f, 0f), 10f)];
            TFField f = bow ? new TFField(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Huntsman), weapons: Weapons, packs: packs)
                            : new TFField(seed: TFSniperPlayTests.SeedOf(TFSniperClass.Marksman), weapons: Weapons, packs: packs);
            for (int n = 0; n < 16; n++)
            {
                f.Hud = bow ? Kit(0f) : TFSniperPlayTests.Kit();
                SelfState me = bow ? Me() with { Clip = 0, Reserve = 2 } : TFSniperPlayTests.Me(Me0) with { Reserve = 2 };
                f.StepDrawn(me, null);
            }

            return f.Decider.Doing.Verdict;
        }

        Assert.Equal((TFDecider.AmmoVerdict, false), (Verdict(bow: true), Verdict(bow: false) == TFDecider.AmmoVerdict));
    }

    /// <summary>Two Soldiers together 600 u off and the jar full: undrawn the jar is taken out for them; drawn, the bow is not put away for it.</summary>
    [Fact]
    public void NoJarWhileDrawn()
    {
        bool JarTaken(float draw)
        {
            TFField f = Field();
            ActorFacts a = Target(600f), b = Target(620f, id: Scout);
            AiVector? look = null;
            for (int n = 0; n < 4; n++)
            {
                f.Hud = Kit(draw, jar: 1f);
                IntentCommand i = f.StepDrawn(Me(look), TFField.Ctf(), a, b);
                if (i.Action.Kind == ActionKind.SelectWeapon)
                    return true;
                look = i.Look.Point;
            }

            return false;
        }

        Assert.Equal((true, false), (JarTaken(0f), JarTaken(0.5f)));
    }

    /// <summary>Lowered (the draw past 4.5 s off aim), no new draw starts within a second, though the eye is on the target; after it, one does.</summary>
    [Fact]
    public void NoNewDrawForASecondAfterLowering()
    {
        TFField f = Field();
        IntentCommand[] tired = Drawing(f, 1000f, 4.6f, 3, onAim: false);
        Assert.Contains(tired, i => Is(f, i, TFSniperClass.BowLower));
        IntentCommand[] after = Drawing(f, 1000f, 0f, 20);
        int first = Array.FindIndex(after, i => i.Action.Kind == ActionKind.Fire);
        Assert.True(first >= 0 && first * DecideSchedule.Period * 0.015f >= 0.9f, $"first draw at decide {first}");
    }

    /// <summary>In the fight (no objective), undrawn: past 1,200 u it closes, inside 300 u it backs off, between it holds its range.</summary>
    [Fact]
    public void TheBowKeepsItsBand()
    {
        string Verdict(float distance)
        {
            TFField f = Field();
            var target = Target(distance);
            for (int n = 0; n < 2; n++)
            {
                f.Hud = Kit(0f);
                f.StepDrawn(Me(Me0 + new AiVector(0f, 3000f, 0f)), null, target);
            }

            return f.Decider.Doing.Verdict;
        }

        Assert.Equal(("closing (bow)", "holding range (bow)", "backing off (bow)"), (Verdict(1500f), Verdict(700f), Verdict(250f)));
    }
    /// <summary>Drawn on a Heavy at 1,100 u, a Soldier at 420 u hits the bot: the hold gives way and the bow turns to him (the re-review's B1).</summary>
    [Fact]
    public void AnEnemyHittingTheBotTakesTheDrawFromItsHold()
    {
        TFField f = Field();
        ActorFacts heavy = Target(1100f, cls: TFClass.HeavyWeapons), soldier = Target(420f, cls: TFClass.Soldier, id: Scout);
        for (int n = 0; n < 4; n++)
        {
            f.Hud = Kit(0f);
            f.StepDrawn(Me(), TFField.Ctf(), heavy);
        }

        for (int n = 0; n < 2; n++)
        {
            f.Hud = Kit(0.3f + n * 0.09f);
            f.StepDrawn(Me(), TFField.Ctf(), heavy, soldier);
        }

        IntentCommand last = default;
        for (int n = 0; n < 3; n++)
        {
            f.Hud = Kit(0.5f + n * 0.09f);
            AiEvent[] hit = n == 0 ? [AiEvent.Damaged(f.Bot, new Tick(f.Tick + DecideSchedule.Period), Scout, 20f, new AiVector(1f, 0f, 0f), 0)] : [];
            last = f.StepHearing(Me(), TFField.Ctf(), hit, heavy, soldier);
        }

        Assert.Equal(Scout, last.Look.Actor);
    }
}
