using System.Collections.Immutable;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Senses;
using SourceSharp.Ai.TF2.Trees;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Spy's play (plan_bots_teams_tf2 C.3, P7; the Spy research §4.3-4.5): its rows, the disguise put on out of a facing
/// enemy's sight, the knife swung only at a back, the cloak pressed only with meter to spare, the victims Medic first, the
/// Saboteur's sapper. Each fact has its control arm.
/// </summary>
public sealed class TFSpyTests
{
    private const int Red = TFField.Red, Blue = TFField.Blue;
    private static readonly AiVector Mid = new(2048f, 256f, 0f);
    private const int Knife = TFField.Shovel, Revolver = TFField.Shotgun;

    /// <summary>A Spy's kit: the revolver and the knife (the field's shotgun and shovel kinds).</summary>
    private static readonly ImmutableArray<TFKitItem> Kit =
    [
        new TFKitItem(TFLoadoutSlot.Secondary, 24, "tf_weapon_revolver", Revolver, []),
        new TFKitItem(TFLoadoutSlot.Melee, 4, "tf_weapon_knife", Knife, []),
    ];

    /// <summary>The Dead Ringer's kit: the stock one with the feigning watch (set_weapon_mode 1).</summary>
    private static readonly ImmutableArray<TFKitItem> RingerKit =
        [.. Kit, new TFKitItem(TFLoadoutSlot.Pda2, 59, "tf_weapon_invis", -1, [new global::TF2.Server.Econ.TFOwnKitAttribute("set_weapon_mode", 1f)])];

    /// <summary>The Ambassador in the secondary slot (set_weapon_mode 1 on a revolver: the head shot).</summary>
    private static readonly ImmutableArray<TFKitItem> AmbassadorKit =
    [
        new TFKitItem(TFLoadoutSlot.Secondary, 61, "tf_weapon_revolver", Revolver, [new global::TF2.Server.Econ.TFOwnKitAttribute("set_weapon_mode", 1f)]),
        new TFKitItem(TFLoadoutSlot.Melee, 461, "tf_weapon_knife", Knife, []),
    ];

    private static TFHud Hud(bool disguised = true, float cloak = 100f, bool cloaked = false, bool disguising = false, bool armed = false,
                             ImmutableArray<TFKitItem>? kit = null)
        => new(kit ?? Kit, [new TFMeter(TFSpyMeters.CloakMeter, cloak), new TFMeter(TFSpyMeters.Disguised, disguised ? 1f : 0f),
                            new TFMeter(TFSpyMeters.Disguising, disguising ? 1f : 0f), new TFMeter(TFSpyMeters.Cloaked, cloaked ? 1f : 0f),
                            new TFMeter(TFSpyMeters.Feign, armed ? 1f : 0f)], []);

    private static SelfState Spy(AiVector at, int weapon = Knife) => TFField.Self(at, health: 125, clip: 6, reserve: 24, weapon: weapon, playerClass: TFClass.Spy);

    /// <summary>
    /// A field whose Spy plays <paramref name="archetype"/> (the Backstabber by default): the bot's seed is the first that
    /// draws it from the Spy's table, as the decider draws its archetype by its seed (TFField has no pin).
    /// </summary>
    private static TFField Field(TFHud hud, string archetype = TFSpyClass.Backstabber)
    {
        ulong seed = 1;
        while (TFSpyClass.Archetypes().Draw(seed)?.Name != archetype)
            seed++;
        return new TFField(seed: seed) { Hud = hud };
    }

    /// <summary>An enemy at <paramref name="at"/> looking along <paramref name="yaw"/> (degrees), drawn as <paramref name="c"/>.</summary>
    private static ActorFacts Enemy(int slot, AiVector at, float yaw, TFClass c = TFClass.Soldier)
        => new(new ActorId(slot, 1), at, AiVector.Zero, TFField.Shotgun, ActorActivity.None, 1f, Blue, (int)c, NameSlot: slot, Facing: new AiAngles(0f, yaw, 0f));

    private static string? AbilityOf(in IntentCommand i)
        => i.Action.Kind == ActionKind.Ability && TFAbilities.Table().TryGet(AbilityCall.Row(i.Action.WeaponSlot), out AbilityRow row) ? row.Name : null;

    /// <summary>Steps until the bot stops changing weapons (the hands' select), then the decide after.</summary>
    private static IntentCommand Settled(TFField f, SelfState self, params ActorFacts[] sighted)
    {
        IntentCommand i = default;
        for (int n = 0; n < 6; n++)
            i = f.StepDrawn(self, TFField.Ctf(), sighted);
        return i;
    }

    [Fact]
    public void TheSpysRowsAreItsDisguisesTheWatchAndTheSapper()
    {
        ImmutableArray<AbilityRow> rows = TFSpyClass.Abilities();
        Assert.Equal(12, rows.Length);
        Assert.Equal(("spy_disguise_medic", AbilityRun.Command, "disguise 5 -1"), (rows[4].Name, rows[4].Run, rows[4].Command));
        AbilityRow cloak = rows.Single(r => r.Name == TFSpyClass.Cloak);
        Assert.Equal((AbilityRun.Buttons, (int)SourceSharp.Abi.InputButtons.Attack2), (cloak.Run, cloak.Buttons));
        Assert.Equal("sap", rows.Single(r => r.Name == TFSpyClass.Sap).Command);
        Assert.Equal(TFSpyClass.LastDisguise, TFAbilities.Table().TryGet(TFClassSpecs.BlockOf(TFClass.Spy) + 9, out AbilityRow last) ? last.Name : null);
    }

    [Fact]
    public void ASpyWithNoDisguiseAndNobodyNearPutsOneOn()
    {
        var f = Field(Hud(disguised: false));
        IntentCommand i = f.StepDrawn(Spy(Mid), TFField.Ctf());
        Assert.StartsWith("spy_disguise_", AbilityOf(i));
        // The control: one already on, none asked.
        var on = Field(Hud(disguised: true));
        Assert.Null(AbilityOf(on.StepDrawn(Spy(Mid), TFField.Ctf())));
    }

    /// <summary>The smoke gives a Spy away (research §1's beginner mistakes): never a disguise in front of an enemy facing it.</summary>
    [Fact]
    public void TheDisguiseNeverGoesOnBeforeAnEnemyFacingTheSpy()
    {
        AiVector at = Mid + new AiVector(800f, 0f, 0f);
        // No meter to cloak behind first (the watch would go before the disguise).
        var facing = Field(Hud(disguised: false, cloak: 0f));
        Assert.Null(AbilityOf(facing.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 180f))));
        // The control: the same enemy looking away.
        var away = Field(Hud(disguised: false, cloak: 0f));
        Assert.StartsWith("spy_disguise_", AbilityOf(away.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 0f))));
    }

    /// <summary>The disguise's class is one walking at the Spy's speed (a Heavy's 77 % costs time): the slow ones lose to the quick.</summary>
    [Fact]
    public void TheDisguisesWeightFallsWithItsClassesSpeed()
    {
        float[] w = [.. new[] { TFClass.Medic, TFClass.Scout, TFClass.Demoman, TFClass.Soldier, TFClass.HeavyWeapons }.Select(TFDecider.SpyPlay.DressSpeed)];
        Assert.Equal(w.OrderByDescending(x => x), w);
        Assert.True(w[0] > w[^1]);
    }

    [Fact]
    public void TheKnifeSwingsAtABackAndNeverAtAFace()
    {
        AiVector at = Mid + new AiVector(40f, 0f, 0f);
        // Looking along +x, away from the Spy at Mid: its back.
        var back = Field(Hud());
        Assert.Equal(ActionKind.Fire, Settled(back, Spy(Mid), Enemy(5, at, 0f, TFClass.Medic)).Action.Kind);
        // The control: the same victim looking at the Spy.
        var face = Field(Hud());
        Assert.NotEqual(ActionKind.Fire, Settled(face, Spy(Mid), Enemy(5, at, 180f, TFClass.Medic)).Action.Kind);
    }

    [Fact]
    public void BehindIsTheVictimsForwardAwayFromTheSpy()
    {
        var victim = Enemy(5, new AiVector(100f, 0f, 0f), 0f);
        Assert.True(TFDecider.SpyPlay.Behind(AiVector.Zero, in victim, 0.2f));
        Assert.False(TFDecider.SpyPlay.Behind(AiVector.Zero, victim with { Facing = new AiAngles(0f, 180f, 0f) }, 0.2f));
        // Side on (90°) is behind only at a tolerance below 0: never, as Expert's 0 is the least.
        Assert.False(TFDecider.SpyPlay.Behind(AiVector.Zero, victim with { Facing = new AiAngles(0f, 90f, 0f) }, 0f));
    }

    /// <summary>The revolver is held while the disguise holds (a shot ends it); found out, it fires.</summary>
    [Fact]
    public void ADisguisedSpyHoldsTheRevolversFire()
    {
        AiVector at = Mid + new AiVector(600f, 0f, 0f);
        var held = Field(Hud(disguised: true));
        IntentCommand i = held.StepDrawn(Spy(Mid, Revolver), TFField.Ctf(), Enemy(5, at, 0f));
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        // The control: undisguised and uncloaked, found out, the gun fires.
        var blown = Field(Hud(disguised: false, cloak: 0f));
        Assert.Equal(ActionKind.Fire, Settled(blown, Spy(Mid, Revolver), Enemy(5, at, 180f)).Action.Kind);
    }

    [Fact]
    public void TheCloakNeverStartsUnderFortyOfMeter()
    {
        AiVector at = Mid + new AiVector(1000f, 0f, 0f);
        var low = Field(Hud(cloak: 30f));
        Assert.NotEqual(TFSpyClass.Cloak, AbilityOf(low.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 0f))));
        var full = Field(Hud(cloak: 60f));
        Assert.Equal(TFSpyClass.Cloak, AbilityOf(full.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 0f))));
    }

    [Fact]
    public void ACloakedSpyDropsItShortOfItsVictim()
    {
        var near = Field(Hud(cloaked: true, cloak: 80f));
        Assert.Equal(TFSpyClass.Cloak, AbilityOf(near.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(500f, 0f, 0f), 0f))));
        // The control: the enemy farther than the drop, the cloak kept.
        var far = Field(Hud(cloaked: true, cloak: 80f));
        Assert.NotEqual(TFSpyClass.Cloak, AbilityOf(far.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(1000f, 0f, 0f), 0f))));
    }

    /// <summary>
    /// The Spy's victims by the pub order (research §1): a Sniper over a Pyro at the same range. (Not the Medic, whom every
    /// bot already ranks first: the Spy's own order is what this holds.)
    /// </summary>
    [Fact]
    public void ASniperIsTheSpysVictimOverAPyroAtTheSameRange()
    {
        var f = Field(Hud());
        f.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(500f, 100f, 0f), 0f, TFClass.Pyro), Enemy(6, Mid + new AiVector(500f, -100f, 0f), 0f, TFClass.Sniper));
        Assert.Equal(new ActorId(6, 1), f.Decider.Doing.Target);
        // The control: a Soldier bot, with no Spy's order, takes the first it was offered.
        var soldier = new TFField();
        soldier.StepDrawn(TFField.Self(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(500f, 100f, 0f), 0f, TFClass.Pyro), Enemy(6, Mid + new AiVector(500f, -100f, 0f), 0f, TFClass.Sniper));
        Assert.NotEqual(new ActorId(6, 1), soldier.Decider.Doing.Target);
    }

    [Fact]
    public void ASpySapsAnEnemyBuildingInReach()
    {
        var sentry = new ThingFacts(40, ThingKind.Building, (int)TFThingType.Sentry, Mid + new AiVector(100f, 0f, 0f), default, Blue, 9, 1, -1);
        var f = Field(Hud());
        IntentCommand i = f.StepSeeing(Spy(Mid), TFField.Ctf(), [sentry]);
        Assert.Equal(TFSpyClass.Sap, AbilityOf(i));
        // The control: the same sentry out of the sapper's reach.
        var far = Field(Hud());
        Assert.NotEqual(TFSpyClass.Sap, AbilityOf(far.StepSeeing(Spy(Mid), TFField.Ctf(), [sentry with { Origin = Mid + new AiVector(400f, 0f, 0f) }])));
    }

    /// <summary>The research's draw rule: the watch and the knife are always the archetype's, so the draw never hands a Backstabber a Dead Ringer.</summary>
    [Fact]
    public void EverySpyArchetypeNamesItsWatchAndItsKnife()
    {
        foreach (TFArchetype a in TFSpyClass.Archetypes().Rows)
        {
            Assert.NotEmpty(a.Prefer(TFLoadoutSlot.Pda2));
            Assert.NotEmpty(a.Prefer(TFLoadoutSlot.Melee));
        }
    }

    /// <summary>The Dead Ringer (research §2 A2): never a cloak to cross. The control: the stock watch, same meter, same enemy, cloaks.</summary>
    [Fact]
    public void ADeadRingerSpyNeverCloaksToCross()
    {
        AiVector at = Mid + new AiVector(1000f, 0f, 0f);
        var ringer = Field(Hud(cloak: 60f, kit: RingerKit));
        Assert.NotEqual(TFSpyClass.Cloak, AbilityOf(ringer.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 0f))));
        var stock = Field(Hud(cloak: 60f));
        Assert.Equal(TFSpyClass.Cloak, AbilityOf(stock.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, at, 0f))));
    }

    /// <summary>The Dead Ringer is armed at a full meter with an enemy near; the control: nobody near, not armed.</summary>
    [Fact]
    public void ADeadRingerArmsAtAFullMeterWithAnEnemyNear()
    {
        var near = Field(Hud(cloak: 100f, kit: RingerKit));
        Assert.Equal(TFSpyClass.Cloak, AbilityOf(near.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(1000f, 0f, 0f), 0f))));
        var alone = Field(Hud(cloak: 100f, kit: RingerKit));
        Assert.NotEqual(TFSpyClass.Cloak, AbilityOf(alone.StepDrawn(Spy(Mid), TFField.Ctf())));
    }

    /// <summary>A feign (armed, then cloaked and unarmed) runs for home; the control: armed and uncloaked, it holds its ground.</summary>
    [Fact]
    public void AFeignedDeadRingerRunsForItsSpawn()
    {
        var f = Field(Hud(cloak: 100f, armed: true, kit: RingerKit));
        f.StepDrawn(Spy(Mid), TFField.Ctf());
        f.Hud = Hud(cloak: 50f, cloaked: true, armed: false, kit: RingerKit);
        f.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(300f, 0f, 0f), 180f));
        Assert.Equal(TFField.RedSpawn, f.Decider.Doing.Goal);
        var still = Field(Hud(cloak: 100f, armed: true, kit: RingerKit));
        still.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(300f, 0f, 0f), 180f));
        Assert.NotEqual(TFField.RedSpawn, still.Decider.Doing.Goal);
    }

    /// <summary>The Gunspy (research §2 A5) opens with the revolver on a target 600 u off while disguised; the Backstabber holds it (ADisguisedSpyHoldsTheRevolversFire).</summary>
    [Fact]
    public void AGunspyOpensWithTheRevolverInItsBand()
    {
        AiVector at = Mid + new AiVector(600f, 0f, 0f);
        var gun = Field(Hud(), TFSpyClass.Gunspy);
        Assert.Equal(ActionKind.Fire, Settled(gun, Spy(Mid, Revolver), Enemy(5, at, 0f)).Action.Kind);
        var stab = Field(Hud(), TFSpyClass.Backstabber);
        Assert.NotEqual(ActionKind.Fire, Settled(stab, Spy(Mid, Revolver), Enemy(5, at, 0f)).Action.Kind);
    }

    /// <summary>The Ambassador's head shot crits again only after a second: two decides apart, the second holds; at the head.</summary>
    [Fact]
    public void TheAmbassadorFiresAtTheHeadAtMostOnceASecond()
    {
        AiVector at = Mid + new AiVector(600f, 0f, 0f);
        var f = Field(Hud(kit: AmbassadorKit), TFSpyClass.Gunspy);
        IntentCommand first = f.StepDrawn(Spy(Mid, Revolver), TFField.Ctf(), Enemy(5, at, 0f));
        Assert.Equal((ActionKind.Fire, AimPoint.Head), (first.Action.Kind, first.Look.Aim));
        IntentCommand next = f.StepDrawn(Spy(Mid, Revolver), TFField.Ctf(), Enemy(5, at, 0f));
        Assert.NotEqual(ActionKind.Fire, next.Action.Kind);
    }

    /// <summary>
    /// The HUD meter readers are a chain, and a chain refuses two members at one order: the Spy's reader, unordered, stood at
    /// 0 beside the Medic's and every sense tick threw live (no bot joined a team). TFBindingTests' chain census reads only
    /// interfaces named *Hook, so this holds the meters' chain: every reader in the binding at an order of its own.
    /// </summary>
    [Fact]
    public void EveryHudMeterReaderHasAnOrderOfItsOwn()
    {
        Type[] readers = [.. typeof(TFSpyMeters).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ITFHudMeters).IsAssignableFrom(t))];
        Assert.Contains(typeof(TFSpyMeters), readers);
        Assert.Contains(typeof(TFMedigunMeters), readers);
        int[] orders = [.. readers.Select(t => typeof(SourceSharp.Abi.IOrderedHook).IsAssignableFrom(t)
            ? ((SourceSharp.Abi.IOrderedHook)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).HookOrder : 0)];
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    /// <summary>
    /// Stock's swing reaches from the eye to the body (review F4): a victim 43 u off and 46 u below, 65 u apart by origin, is
    /// in reach; the control: the same victim 120 u off is not.
    /// </summary>
    [Fact]
    public void TheKnifeReachesFromTheEyeToTheBody()
    {
        var spy = new AiVector(0f, 0f, 46f);
        AiVector eye = spy + new AiVector(0f, 0f, 68f);
        Assert.True(TFDecider.SpyPlay.InSwingReach(eye, new AiVector(43f, 0f, 0f), crouched: false));
        Assert.False(TFDecider.SpyPlay.InSwingReach(eye, new AiVector(120f, 0f, 0f), crouched: false));
    }

    /// <summary>
    /// The door deadlock (review F4): a Spy near a victim it cannot reach, two seconds without a swing, goes by the paths to it.
    /// The control: the first decides there it still circles (a straight-line strafe).
    /// </summary>
    [Fact]
    public void ASpyLongOnAVictimWithNoSwingRepathsToIt()
    {
        // Facing the Spy, 120 u off: never behind, never in reach.
        ActorFacts victim = Enemy(5, Mid + new AiVector(120f, 0f, 0f), 180f, TFClass.Medic);
        var f = Field(Hud());
        f.StepDrawn(Spy(Mid), TFField.Ctf(), victim);
        Assert.NotEqual("re-pathing to the victim", f.Decider.Doing.Verdict);
        for (int n = 0; n < 30; n++)
            f.StepDrawn(Spy(Mid), TFField.Ctf(), victim);
        Assert.Equal("re-pathing to the victim", f.Decider.Doing.Verdict);
    }

    /// <summary>
    /// A carrier can neither disguise nor cloak (TFSpy.cs:297, :912), so a Spy carrying the enemy's flag asks neither
    /// (review F10: the counters counted re-sent asks). The control: the same Spy not carrying puts a disguise on.
    /// </summary>
    [Fact]
    public void ACarrierAsksNoDisguise()
    {
        var carrying = Field(Hud(disguised: false));
        ObjectiveKnowledge carried = TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: carrying.Bot.Slot);
        Assert.Null(AbilityOf(carrying.StepDrawn(Spy(Mid), carried)));
        var free = Field(Hud(disguised: false));
        Assert.StartsWith("spy_disguise_", AbilityOf(free.StepDrawn(Spy(Mid), TFField.Ctf())));
    }

    /// <summary>
    /// Stalled twice as long (4 s), the Spy lets the victim go and leaves the fight with it (2fort, the re-measure: 18 s at a
    /// ledge, a sole victim kept by its priority). The control: inside the re-path window it is still on that victim.
    /// </summary>
    [Fact]
    public void ASpyStalledTwiceAsLongLetsTheVictimGo()
    {
        ActorFacts victim = Enemy(5, Mid + new AiVector(120f, 0f, 0f), 180f, TFClass.Medic);
        var f = Field(Hud());
        for (int n = 0; n < 30; n++)
            f.StepDrawn(Spy(Mid), TFField.Ctf(), victim);
        Assert.Equal("re-pathing to the victim", f.Decider.Doing.Verdict);
        for (int n = 0; n < 25; n++)
            f.StepDrawn(Spy(Mid), TFField.Ctf(), victim);
        Assert.DoesNotContain(f.Decider.Doing.Verdict, new[] { "re-pathing to the victim", "round to the back", "behind the victim" });
    }

    /// <summary>
    /// Spy against Spy (the re-measure: two disguised Spies 55 u apart stood 22 and 24 s): the victim's actor changes with
    /// each disguise and neither body moves, so the stall is watched by place and motion, not by actor. Within 150 u, both
    /// still, no swing, it leaves the fight at 4 s. The control: the same victim walking (30 u a decide) keeps the fight.
    /// </summary>
    [Fact]
    public void TwoStillSpiesLetEachOtherGoWhateverTheyAreDressedAs()
    {
        var still = Field(Hud());
        for (int n = 0; n < 55; n++)
            still.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5 + (n & 1), Mid + new AiVector(55f, 0f, 0f), 180f, TFClass.Medic));
        Assert.NotEqual(TFDecider.Fight, still.Branch);

        var walking = Field(Hud());
        for (int n = 0; n < 55; n++)
            walking.StepDrawn(Spy(Mid), TFField.Ctf(), Enemy(5, Mid + new AiVector(55f, (n % 4) * 30f - 45f, 0f), 180f, TFClass.Medic));
        Assert.Equal(TFDecider.Fight, walking.Branch);
    }
}
