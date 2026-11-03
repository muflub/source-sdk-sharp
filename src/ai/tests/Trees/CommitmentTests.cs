using SourceSharp.Ai.Hl2mp.Trees;
using Xunit;

namespace SourceSharp.Ai.Tests.Trees;

/// <summary>
/// Decision commitment through the trees (plan_hl2mp_ai §5.4, user ruling
/// 2026-11-02): a bot does not change its mind for a small change, and what must
/// interrupt does at once. Each interrupt beside the same stimulus without it, so
/// the hold is shown to have been there to break.
/// </summary>
public sealed class CommitmentTests
{
    private static readonly AiVector KitA = new(-300f, 0f, 0f), KitB = new(500f, 0f, 0f);
    private static readonly AiVector Close = new(200f, 0f, 0f), Far = new(900f, 0f, 0f);

    private static ItemSpawn Kit(int index, AiVector at) => new(index, "item_healthkit", ItemKind.Health, at, 30f);

    private static Duel Normal(params ItemSpawn[] items) => new(Duel.Preset("normal"), items);

    private static SelfState At(Duel d, AiVector origin, int health = 100, int clip = 30, int reserve = 90)
        => d.Self(health: health, clip: clip, reserve: reserve) with { Origin = origin };

    /// <summary>A step of a bot on its way somewhere: its origin moves enough each step that it is getting somewhere.</summary>
    private static IntentCommand Walking(Duel d, AiVector around, int step, int health = 30)
        => d.Step(At(d, around + new AiVector(0f, step % 2 * 40f, 0f), health), []);

    /// <summary>Steps are 6 ticks, 0.09 s.</summary>
    private static int Steps(float seconds) => (int)Math.Ceiling(seconds / (6 * 0.015f));

    // ---- the goal -----------------------------------------------------------

    /// <summary>The nearest kit changes as the bot walks: it finishes going to the one it chose, for the hold's time.</summary>
    [Fact]
    public void AGoalHoldsAgainstANearerOneForItsTime()
    {
        Duel d = Normal(Kit(0, KitA), Kit(1, KitB));
        Assert.Equal(KitA, d.Step(At(d, AiVector.Zero, 30), []).Move.Corner0);
        var nearerB = new AiVector(200f, 0f, 0f);
        int held = Steps(Hl2mpDecider.GoalHoldSeconds);
        for (int i = 1; i < held - 1; i++)
            Assert.Equal(KitA, Walking(d, nearerB, i).Move.Corner0);
        AiVector after = default;
        for (int i = 0; i < 4; i++)
            after = Walking(d, nearerB, i).Move.Corner0;
        Assert.Equal(KitB, after);
    }

    [Fact]
    public void AReachedGoalLetsTheNextOneIn()
    {
        var a = new AiVector(-300f, 0f, 0f);
        var b = new AiVector(-300f, 130f, 0f);
        Duel d = Normal(Kit(0, a), Kit(1, b));
        Assert.Equal(a, d.Step(At(d, new AiVector(-300f, -300f, 0f), 30), []).Move.Corner0);
        // 90 from a, reached; b is nearer still, and 130 from a is no near goal.
        Assert.Equal(b, d.Step(At(d, new AiVector(-300f, 90f, 0f), 30), []).Move.Corner0);
    }

    [Fact]
    public void ANewGoalNearTheHeldOneIsTakenAtOnce()
    {
        var a = new AiVector(-1000f, 0f, 0f);
        var b = new AiVector(-1030f, 0f, 0f);
        Duel d = Normal(Kit(0, a), Kit(1, b));
        Assert.Equal(b, d.Step(At(d, new AiVector(-1400f, 0f, 0f), 30), []).Move.Corner0);
        Assert.Equal(a, d.Step(At(d, new AiVector(-700f, 0f, 0f), 30), []).Move.Corner0);
    }

    /// <summary>A goal drifting by steps (a belief's arg-max) is a change of mind: each step past the near distance waits, or a route that ties flips with every one.</summary>
    [Fact]
    public void AGoalThatMovesBeyondTheNearDistanceIsHeld()
    {
        var a = new AiVector(-1000f, 0f, 0f);
        var b = new AiVector(-1040f, 0f, 0f);
        Duel d = Normal(Kit(0, a), Kit(1, b));
        Assert.Equal(b, d.Step(At(d, new AiVector(-1400f, 0f, 0f), 30), []).Move.Corner0);
        Assert.Equal(b, d.Step(At(d, new AiVector(-700f, 0f, 0f), 30), []).Move.Corner0);
    }

    /// <summary>A goal on someone in sight is where they are, not a place to finish going to.</summary>
    [Fact]
    public void AGoalOnAnOpponentInSightFollowsThemAtOnce()
    {
        var d = new Duel(Duel.Preset("normal"));
        var there = new AiVector(1500f, 0f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, there)], d.SeeAt(Duel.Opponent, there));
        Assert.Equal(there, d.Step(d.Self(), [(Duel.Opponent, there)]).Move.Corner0);
        var moved = new AiVector(1500f, 600f, 0f);
        Assert.Equal(moved, d.Step(d.Self(), [(Duel.Opponent, moved)]).Move.Corner0);
    }

    /// <summary>The hold is a leaf's: a different decision's goal is taken at once, inside the hold's time.</summary>
    [Fact]
    public void ANewLeafTakesItsOwnGoalAtOnce()
    {
        Duel d = Roaming(Kit(0, KitA));
        AiVector roam = d.Step(At(d, AiVector.Zero), []).Move.Corner0;
        Assert.NotEqual(KitA, roam);
        // Out of Roam's one-second minimum and well inside the goal's three.
        for (int i = 0; i < Steps(1f); i++)
            d.Step(At(d, new AiVector(i % 2 * 40f, 0f, 0f)), []);
        Assert.Equal("Roam", d.Decider.Doing.Branch);
        // Short of health with no one hurting it: no interrupt, a new branch and leaf.
        (string branch, AiVector goal) = Branch(d, At(d, new AiVector(40f, 0f, 0f), 30));
        Assert.Equal("", d.Decider.LastInterrupt);
        Assert.Equal(("Survive", KitA), (branch, goal));
    }

    private static (string Branch, AiVector Goal) Branch(Duel d, SelfState self)
    {
        IntentCommand i = d.Step(self, []);
        return (d.Decider.Doing.Branch, i.Move.Corner0);
    }

    // ---- interrupts ---------------------------------------------------------

    /// <summary>A roaming bot, inside Roam's one-second minimum.</summary>
    private static Duel Roaming(params ItemSpawn[] items)
    {
        // Roam's goal is a pickup or a spawn; keep one far off so it has somewhere to go.
        ItemSpawn[] all = [new ItemSpawn(9, "item_battery", ItemKind.Armour, new AiVector(3000f, 0f, 0f), 30f), .. items];
        var d = new Duel(Duel.Preset("normal"), all);
        d.Step(d.Self(), []);
        Assert.Equal("Roam", d.Decider.Doing.Branch);
        return d;
    }

    /// <summary>The ruling: close, firing comes first, inside any minimum.</summary>
    [Fact]
    public void SomeoneSeenCloseInterruptsAndFiringComesFirst()
    {
        Duel d = Roaming();
        IntentCommand i = d.Step(d.Self(), [(Duel.Opponent, Close)], d.SeeAt(Duel.Opponent, Close));
        Assert.Equal("Fight", d.Decider.Doing.Branch);
        Assert.Equal("close", d.Decider.LastInterrupt);
        Assert.Equal(Duel.Opponent, d.Step(d.Self(), [(Duel.Opponent, Close)]).Look.Actor);
        Assert.Equal(Duel.Opponent, i.Look.Actor);
    }

    [Fact]
    public void SomeoneNewlySeenFarOffInterrupts()
    {
        Duel d = Roaming();
        d.Step(d.Self(), [(Duel.Opponent, Far)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal("Fight", d.Decider.Doing.Branch);
        Assert.Equal("seen", d.Decider.LastInterrupt);
        // Only the gain: still in sight next decide, nothing interrupts.
        d.Step(d.Self(), [(Duel.Opponent, Far)]);
        Assert.Equal("", d.Decider.LastInterrupt);
    }

    /// <summary>Health falls below 40 with a kit in reach, which Survive scores 0.6 over Roam's 0.1: hurt, at once; not hurt, it waits out the minimum.</summary>
    [Fact]
    public void DamageInterruptsAndTheSameDropInHealthWithoutItDoesNot()
    {
        Duel hurt = Roaming(Kit(0, KitA));
        hurt.Step(hurt.Self(health: 30), [], AiEvent.Damaged(Duel.Bot, new Tick(hurt.Tick + 6), Duel.Opponent, 70f, new AiVector(1f, 0f, 0f), 0));
        Assert.Equal("hurt", hurt.Decider.LastInterrupt);
        Assert.Equal("Survive", hurt.Decider.Doing.Branch);

        Duel quiet = Roaming(Kit(0, KitA));
        quiet.Step(quiet.Self(health: 30), []);
        Assert.Equal("Roam", quiet.Decider.Doing.Branch);
        // A larger score does replace it, once the minimum is out.
        for (int i = 0; i < Steps(1f); i++)
            Walking(quiet, AiVector.Zero, i);
        Assert.Equal("Survive", quiet.Decider.Doing.Branch);
    }

    [Fact]
    public void AnEmptyClipInterruptsAndALowOneDoesNot()
    {
        var box = new ItemSpawn(1, "item_ammo_smg1", ItemKind.Ammo, new AiVector(700f, 0f, 0f), 30f);
        Duel empty = Roaming(box);
        empty.Step(empty.Self(clip: 0, reserve: 10), []);
        Assert.Equal("empty", empty.Decider.LastInterrupt);
        Assert.Equal("Arm", empty.Decider.Doing.Branch);

        Duel low = Roaming(box);
        low.Step(low.Self(clip: 5, reserve: 5), []);
        Assert.Equal("", low.Decider.LastInterrupt);
        Assert.Equal("Roam", low.Decider.Doing.Branch);
    }

    /// <summary>A kit seen gone: on to the next at once, where a nearer one would have waited.</summary>
    [Fact]
    public void APickupKnownTakenLetsTheNextOneInAtOnce()
    {
        Duel d = Normal(Kit(0, KitA), Kit(1, KitB));
        Assert.Equal(KitA, d.Step(At(d, AiVector.Zero, 30), []).Move.Corner0);
        IntentCommand i = d.Step(At(d, new AiVector(40f, 0f, 0f), 30), [], AiEvent.ItemSeen(Duel.Bot, new Tick(d.Tick + 6), 0, false));
        Assert.Equal("goal gone", d.Decider.LastInterrupt);
        Assert.Equal(KitB, i.Move.Corner0);
    }

    /// <summary>Chasing someone last seen far off, who is then read dead on the kill feed.</summary>
    [Fact]
    public void TheOpponentDeadIsAGoalGone()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, Far)], d.SeeAt(Duel.Opponent, Far));
        var lost = AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, Far, default);
        d.Step(At(d, AiVector.Zero), [], lost);
        for (int k = 0; k < Steps(Hl2mpDecider.SightGraceSeconds); k++)
            d.Step(At(d, new AiVector(k % 2 * 40f, 0f, 0f)), []);
        Assert.Equal("chase", d.Decider.Doing.Leaf);
        d.Step(At(d, new AiVector(40f, 0f, 0f)), [], AiEvent.KillFeed(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, Duel.Other));
        Assert.Equal("goal gone", d.Decider.LastInterrupt);
    }

    /// <summary>Held off a nearer kit and not moving: it lets go at the no-progress time, well before the hold's own.</summary>
    [Fact]
    public void GettingNowhereLetsTheNextGoalInBeforeItsTime()
    {
        Assert.True(Hl2mpDecider.NoProgressSeconds < Hl2mpDecider.GoalHoldSeconds - 0.5f);
        Duel d = Normal(Kit(0, KitA), Kit(1, KitB));
        Assert.Equal(KitA, d.Step(At(d, AiVector.Zero, 30), []).Move.Corner0);
        var nearerB = new AiVector(200f, 0f, 0f);
        d.Step(At(d, nearerB, 30), []);
        AiVector goal = default;
        bool said = false;
        for (int i = 0; i < Steps(Hl2mpDecider.NoProgressSeconds) + 1; i++)
        {
            goal = d.Step(At(d, nearerB, 30), []).Move.Corner0;
            said |= d.Decider.LastInterrupt == "stuck";
        }

        Assert.True(said, "it said it was getting nowhere");
        Assert.Equal(KitB, goal);
    }

    // ---- sight that flickers ------------------------------------------------

    /// <summary>A corner takes the opponent out of sight for a moment: the fight goes on as it was, holding fire and looking where they were, then becomes a chase.</summary>
    [Fact]
    public void AMomentOutOfSightIsNoChase()
    {
        var d = new Duel(Duel.Preset("normal"));
        d.Step(d.Self(), [(Duel.Opponent, Far)], d.SeeAt(Duel.Opponent, Far));
        Assert.Equal("hold range", d.Step(d.Self(), [(Duel.Opponent, Far)]).Reason.NodeId >= 0 ? d.Decider.Doing.Leaf : "");
        IntentCommand i = d.Step(At(d, new AiVector(40f, 0f, 0f)), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, Far, default));
        Assert.Equal("hold range", d.Decider.Doing.Leaf);
        Assert.Equal(ActionKind.None, i.Action.Kind);
        Assert.Equal(LookKind.Point, i.Look.Kind);
        for (int k = 0; k < Steps(Hl2mpDecider.SightGraceSeconds); k++)
            d.Step(At(d, new AiVector(k % 2 * 40f, 0f, 0f)), []);
        Assert.Equal("chase", d.Decider.Doing.Leaf);
    }

    // ---- an ambush holds still ----------------------------------------------

    /// <summary>Commitment made ambushes last, which showed an ambusher jiggling on its spot (140 s stuck in one five-minute run): at the spot it stands, and the spot is on the graph.</summary>
    [Fact]
    public void AnAmbusherStandsOnItsSpotAndTheSpotIsOnTheGraph()
    {
        var (g, ids) = SourceSharp.Ai.Tests.Nav.Grids.Parse([.. Enumerable.Repeat(new string('.', 40), 40)]);
        SkillProfile p = Duel.Preset("normal");
        var d = new Duel(p.With(p.IndexOf("cunning"), 10), g, new ItemSpawn(0, "item_battery", ItemKind.Armour, new AiVector(3000f, 0f, 0f), 30f));
        var seen = new AiVector(600f, 600f, 0f);
        d.Step(d.Self(), [], d.SeeAt(Duel.Opponent, seen));
        d.Step(d.Self(), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, seen, default));
        d.Memory.HabitsOf(Duel.Opponent).Camped(SourceSharp.Ai.Tests.Nav.Grids.At(ids, 20, 20));
        IntentCommand i = default;
        for (int k = 0; k < 2000 && d.Decider.Doing.Branch != "Ambush"; k++)
            i = d.Step(d.Self(), []);
        Assert.Equal("Ambush", d.Decider.Doing.Branch);
        Assert.Equal(MoveKind.Follow, i.Move.Kind);
        AiVector spot = i.Move.Corner0;
        Assert.Equal(g.Cell(g.FindCell(spot + new AiVector(0f, 0f, 1f))).Centre, spot);

        IntentCommand there = d.Step(At(d, spot), []);
        Assert.Equal("Ambush", d.Decider.Doing.Branch);
        Assert.Equal(MoveKind.Hold, there.Move.Kind);
        Assert.Equal(LookKind.Watch, there.Look.Kind);
    }

    // ---- the target and the windows -----------------------------------------

    /// <summary>Two last seen either side: a little nearer does not take it from the one it is about; a lot nearer does.</summary>
    [Fact]
    public void TheOpponentItIsAboutHoldsAgainstOneAlittleNearer()
    {
        var left = new AiVector(-500f, 0f, 0f);
        var right = new AiVector(500f, 0f, 0f);
        var d = new Duel(Duel.Preset("normal"));
        (ActorId, AiVector)[] both = [(Duel.Opponent, right), (Duel.Other, left)];
        d.Step(d.Self(), both, [.. d.SeeAt(Duel.Opponent, right), .. d.SeeAt(Duel.Other, left)]);
        d.Step(At(d, AiVector.Zero), [], AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, right, default),
               AiEvent.SightLost(Duel.Bot, new Tick(d.Tick + 6), Duel.Other, left, default));
        ActorId first = d.Decider.Doing.Target;
        Assert.True(first.IsValid);
        AiVector towardOther = first == Duel.Opponent ? left : right;
        ActorId other = first == Duel.Opponent ? Duel.Other : Duel.Opponent;
        d.Step(At(d, towardOther * 0.04f), []);
        Assert.Equal(first, d.Decider.Doing.Target);
        d.Step(At(d, towardOther * 0.7f), []);
        Assert.Equal(other, d.Decider.Doing.Target);
    }

    /// <summary>§5.4: a commitment must not hide a window. The Fight branch holds; a push inside it ends the decide its window does.</summary>
    [Fact]
    public void AWindowThatClosesEndsAPushInsideACommittedFight()
    {
        SkillProfile p = Duel.Preset("hard");
        var d = new Duel(p.With(p.IndexOf("discipline"), 10));
        var near = new AiVector(150f, 0f, 0f);
        d.Step(d.Self(), [(Duel.Opponent, near)], d.SeeAt(Duel.Opponent, near));
        d.Step(d.Self(), [(Duel.Opponent, near)], d.Shots(Duel.Opponent, 45));
        Assert.Equal(("Fight", "push"), (d.Decider.Doing.Branch, d.Decider.Doing.Verdict));
        // Seen to reload to full: the window has shut.
        d.Step(d.Self(), [(Duel.Opponent, near)], AiEvent.SeenReload(Duel.Bot, new Tick(d.Tick + 6), Duel.Opponent, ReloadPhase.End, Duel.Smg1, 45));
        Assert.Equal("Fight", d.Decider.Doing.Branch);
        Assert.NotEqual("push", d.Decider.Doing.Verdict);
    }
}
