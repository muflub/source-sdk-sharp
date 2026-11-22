using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server.Interop;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The TF2 tree (plan_bots_teams_tf2 C.3, C.6, C.8, P6 step 6) over a synthetic CTF field
/// (<see cref="TFField"/>): the carrier delivers, an enemy carrier is chased, a defender holds the
/// home flag while an attacker fetches the enemy's, a grounded target is shot at the feet, and a
/// class with no subtree of its own runs the Soldier's and says so, without the Soldier's jump
/// (whose own rule is TFJumpRuleTests').
/// </summary>
public sealed class TFDeciderTests
{
    private static readonly ActorId Enemy = new(5, 0x4000_0001);
    private static readonly AiVector Mid = new(2048f, 256f, 0f);

    [Fact]
    public void TheFlagCarrierTakesItToItsOwnCaptureZone()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot));

        Assert.Equal(TFDecider.Deliver, BranchOf(f));
        Assert.Equal((MoveKind.Follow, TFField.RedZone), (i.Move.Kind, i.Move.Corner0));
    }

    [Fact]
    public void ACarrierKeepsDeliveringWithAnEnemyInSightAndShootsIt()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: Mid, blueCarrier: f.Bot.Slot),
                                 (Enemy, Mid + new AiVector(500f, 0f, 0f), AiVector.Zero, TFField.Blue));

        Assert.Equal(TFDecider.Deliver, BranchOf(f));
        Assert.Equal((MoveKind.Follow, TFField.RedZone), (i.Move.Kind, i.Move.Corner0));
        Assert.Equal((LookKind.Actor, Enemy, ActionKind.Fire), (i.Look.Kind, i.Look.Actor, i.Action.Kind));
    }

    [Fact]
    public void AnEnemyCarryingTheBotsFlagIsChased()
    {
        var f = new TFField();
        AiVector carrier = new(1500f, 300f, 0f);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(red: ObjectiveState.Carried, redAt: carrier, redCarrier: Enemy.Slot));

        Assert.Equal(TFDecider.ChaseCarrier, BranchOf(f));
        Assert.Equal((MoveKind.Follow, carrier), (i.Move.Kind, i.Move.Corner0));
    }

    /// <summary>An attacker far from the enemy carrying its flag goes on for the enemy's flag; a defender as far off turns back.</summary>
    [Fact]
    public void AnAttackerFarFromTheCarrierGoesOnForTheEnemysFlag()
    {
        AiVector carrier = new(256f, 300f, 0f);
        AiVector far = carrier + new AiVector(TFDecider.AttackerChaseReach + 200f, 0f, 0f);
        ObjectiveKnowledge taken = TFField.Ctf(red: ObjectiveState.Carried, redAt: carrier, redCarrier: Enemy.Slot);

        var attacker = new TFField();
        Assert.Equal(TFTeamRole.Attack, attacker.Decider.Role);
        attacker.Step(TFField.Self(far), taken);
        Assert.Equal(TFDecider.Fetch, BranchOf(attacker));

        var defender = TFField.Defender();
        defender.Step(TFField.Self(far), taken);
        Assert.Equal(TFDecider.ChaseCarrier, BranchOf(defender));
    }

    /// <summary>The CTF policy's split: a third of a team defends, and every other bot but the carrier may escort.</summary>
    [Fact]
    public void ATeamsSplitIsAThirdDefendingTheRestAttacking()
    {
        RoleSplit split = TFDecider.CtfRoleSplit;
        Assert.Equal([new RoleCounts(0, 0), new RoleCounts(0, 1), new RoleCounts(1, 1), new RoleCounts(1, 2), new RoleCounts(2, 3), new RoleCounts(2, 5)],
                     new[] { 1, 2, 3, 4, 6, 8 }.Select(n => split(n)));
    }

    /// <summary>
    /// A team's defenders are its last slots on the scoreboard, whatever the slots: live at 4v4 on
    /// ctf_2fort red was slots 3, 5, 6 and 9 and, a defender every third slot, held its flag with
    /// three and sent one; blue (2, 4, 7, 8) had no defender.
    /// </summary>
    [Fact]
    public void AFourBotTeamHasOneDefenderItsLastSlot()
    {
        int[] red = [3, 5, 6, 9];
        TFTeamRole[] roles = [.. red.Select(slot =>
        {
            var f = new TFField(slot: slot, mates: [.. red.Where(m => m != slot)]);
            f.Step(TFField.Self(Mid), TFField.Ctf());
            return f.Decider.Role;
        })];
        Assert.Equal([TFTeamRole.Attack, TFTeamRole.Attack, TFTeamRole.Attack, TFTeamRole.Defend], roles);
    }

    /// <summary>The other team's rows do not count: a bot alone on its team attacks, however many enemies the scoreboard shows.</summary>
    [Fact]
    public void ABotAloneOnItsTeamAttacks()
    {
        var f = new TFField(slot: 9);
        foreach (int enemy in new[] { 2, 4, 7 })
            f.Memory.Apply(AiEvent.ActorTeam(f.Bot, new Tick(1), new ActorId(enemy, 1), TFField.Blue, 0));
        f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFTeamRole.Attack, f.Decider.Role);
    }

    [Fact]
    public void ADefenderHoldsItsHomeFlag()
    {
        var f = TFField.Defender();
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf());
        Assert.Equal(TFTeamRole.Defend, f.Decider.Role);

        Assert.Equal(TFDecider.Defend, BranchOf(f));
        Assert.Equal(MoveKind.Follow, i.Move.Kind);
        Assert.True(i.Move.Corner0.DistanceTo(TFField.RedFlag) <= TFDecider.DefendRadius, $"the defend spot {i.Move.Corner0} is off the flag at {TFField.RedFlag}");
    }

    [Fact]
    public void AnAttackerFetchesTheEnemysFlag()
    {
        var f = new TFField(slot: 2);
        Assert.Equal(TFTeamRole.Attack, f.Decider.Role);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf());

        Assert.Equal(TFDecider.Fetch, BranchOf(f));
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag), (i.Move.Kind, i.Move.Corner0));
    }

    /// <summary>An attacker on its way to the enemy's flag goes on past an enemy 700 u off, firing as it goes; a defender stops to fight it.</summary>
    [Fact]
    public void AnAttackerGoesOnPastAFarEnemyFiringAndADefenderFightsIt()
    {
        var attacker = new TFField(slot: 2);
        IntentCommand a = attacker.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(700f, 0f, 0f), AiVector.Zero, TFField.Blue));
        Assert.Equal(TFDecider.Fetch, BranchOf(attacker));
        Assert.Equal((MoveKind.Follow, ActionKind.Fire), (a.Move.Kind, a.Action.Kind));

        var defender = TFField.Defender();
        IntentCommand d = defender.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(700f, 0f, 0f), AiVector.Zero, TFField.Blue));
        Assert.Equal(TFDecider.Fight, BranchOf(defender));
        Assert.Equal(0.8f, d.Reason.Utility);
    }

    [Fact]
    public void AnAttackerStopsToFightAnEnemyClose()
    {
        var f = new TFField(slot: 2);
        f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(300f, 0f, 0f), AiVector.Zero, TFField.Blue));
        Assert.Equal(TFDecider.Fight, BranchOf(f));
    }

    [Fact]
    public void ATeammateCarryingTheFlagIsEscorted()
    {
        var f = new TFField(slot: 2);
        AiVector carrier = new(1200f, 256f, 0f);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(blue: ObjectiveState.Carried, blueAt: carrier, blueCarrier: 4));

        Assert.Equal(TFDecider.Escort, BranchOf(f));
        Assert.Equal((MoveKind.Follow, carrier), (i.Move.Kind, i.Move.Corner0));
    }

    [Fact]
    public void AGroundedTargetIsShotAtTheFeet()
    {
        var f = new TFField();
        AiVector at = Mid + new AiVector(600f, 0f, 0f);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, at, AiVector.Zero, TFField.Blue));

        Assert.Equal((LookKind.Actor, AimPoint.Feet, ActionKind.Fire), (i.Look.Kind, i.Look.Aim, i.Action.Kind));
        Assert.Equal(at.Z + TFKnowledge.FeetAim, i.Look.Point.Z, 3);
        Assert.Equal(1, f.Decider.FeetShots);
    }

    [Fact]
    public void ATargetInTheAirIsShotAtTheBody()
    {
        var f = new TFField();
        AiVector at = Mid + new AiVector(600f, 0f, 64f);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, at, new AiVector(0f, 0f, 300f), TFField.Blue));

        Assert.Equal((AimPoint.Body, ActionKind.Fire), (i.Look.Aim, i.Action.Kind));
        Assert.Equal(0, f.Decider.FeetShots);
    }

    [Fact]
    public void AShotgunIsAimedAtTheBody()
    {
        var f = new TFField();
        AiVector at = Mid + new AiVector(300f, 0f, 0f);
        IntentCommand i = f.Step(TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6), TFField.Ctf(), (Enemy, at, AiVector.Zero, TFField.Blue));

        Assert.Equal((AimPoint.Body, ActionKind.Fire), (i.Look.Aim, i.Action.Kind));
    }

    [Fact]
    public void ARocketIsNotFiredAtATargetInsideItsOwnBlast()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(100f, 0f, 0f), AiVector.Zero, TFField.Blue));

        Assert.Equal(LookKind.Actor, i.Look.Kind);
        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
    }

    [Fact]
    public void AnActorDrawnOnTheBotsTeamIsNoTarget()
    {
        // A disguised Spy is drawn as the disguise's team: with no cue, the bot never targets him.
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf(), (Enemy, Mid + new AiVector(600f, 0f, 0f), AiVector.Zero, TFField.Red));

        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(ActorId.None, f.Decider.Doing.Target);
    }

    [Fact]
    public void AnEnemyInItsOwnRespawnRoomIsNoTarget()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(new AiVector(3500f, 256f, 0f)), TFField.Ctf(), (Enemy, new AiVector(4000f, 256f, 0f), AiVector.Zero, TFField.Blue));

        Assert.NotEqual(ActionKind.Fire, i.Action.Kind);
        Assert.Equal(ActorId.None, f.Decider.Doing.Target);
    }

    [Fact]
    public void AHurtBotGoesToAHealthPack()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(new AiVector(700f, 64f, 0f), health: 50), TFField.Ctf());

        Assert.Equal(TFDecider.Survive, BranchOf(f));
        Assert.Equal((MoveKind.Follow, new AiVector(512f, 64f, 0f)), (i.Move.Kind, i.Move.Corner0));
    }

    /// <summary>A pack not healed at in 15 s (taken, or out of reach) is given up for the next one (live: bots stood "hurt" by a pack for minutes).</summary>
    [Fact]
    public void AHurtBotGivesUpAPackItIsNotHealedAt()
    {
        var f = new TFField();
        AiVector here = new(1800f, 64f, 0f);
        Assert.Equal(new AiVector(512f, 64f, 0f), f.Step(TFField.Self(here, health: 50), TFField.Ctf()).Move.Corner0);
        int decides = (int)(TFDecider.HealPatienceSeconds / 0.015f / DecideSchedule.Period) + 2;
        IntentCommand last = default;
        for (int d = 0; d < decides; d++)
            last = f.Step(TFField.Self(here, health: 50), TFField.Ctf());
        Assert.Equal(new AiVector(3584f, 64f, 0f), last.Move.Corner0);
    }

    [Fact]
    public void APackSeenTakenIsNotGoneTo()
    {
        var f = new TFField();
        f.Memory.Apply(AiEvent.ItemSeen(f.Bot, new Tick(1), 0, false));
        IntentCommand i = f.Step(TFField.Self(new AiVector(1800f, 64f, 0f), health: 50), TFField.Ctf());
        Assert.Equal(new AiVector(3584f, 64f, 0f), i.Move.Corner0);
    }

    [Fact]
    public void ADryLauncherIsSwappedForTheShotgunUpClose()
    {
        var f = new TFField();
        IntentCommand i = f.Step(TFField.Self(Mid, clip: 0), TFField.Ctf(), (Enemy, Mid + new AiVector(400f, 0f, 0f), AiVector.Zero, TFField.Blue));

        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + TFField.Shotgun), (i.Action.Kind, i.Action.WeaponSlot));
        Assert.True(i.Action.OneShotSeq > 0);
    }

    [Fact]
    public void InALullTheLauncherIsTakenBackAndLoaded()
    {
        var f = new TFField();
        IntentCommand back = f.Step(TFField.Self(Mid, weapon: TFField.Shotgun, clip: 6), TFField.Ctf());
        Assert.Equal((ActionKind.SelectWeapon, EngineBotSelf.ByKind + TFField.Launcher), (back.Action.Kind, back.Action.WeaponSlot));

        f.Step(TFField.Self(Mid, clip: 1), TFField.Ctf());
        IntentCommand load = Enumerable.Range(0, 20).Select(_ => f.Step(TFField.Self(Mid, clip: 1), TFField.Ctf())).First(c => c.Action.Kind != ActionKind.None);
        Assert.Equal(ActionKind.Reload, load.Action.Kind);
    }

    // ------------------------------------------------------------------ the rocket jump

    /// <summary>A Soldier at the west end running east along the floor toward the enemy's flag, 3,500 u away.</summary>
    private static SelfState Running(TFClass playerClass = TFClass.Soldier, int health = 200, int clip = 4, AiVector? velocity = null)
        => TFField.Self(new AiVector(300f, 256f, 0f), health: health, clip: clip, velocity: velocity ?? new AiVector(240f, 0f, 0f), playerClass: playerClass);

    /// <summary>How many of the decides' intents are the rocket jump's one-shot (a voice line is an ability one-shot too, and is not counted).</summary>
    private static int AbilityIntents(TFField f, Func<SelfState> self, int decides, out List<IntentCommand> intents)
    {
        intents = [.. Enumerable.Range(0, decides).Select(_ => f.Step(self(), TFField.Ctf()))];
        return intents.Count(i => f.BeginsJump(i));
    }

    /// <summary>A Scout runs its own subtree (P7), named as itself, and the Soldier's rocket jump is not in it.</summary>
    [Fact]
    public void AScoutRunsItsOwnSubtreeAndSaysSoWithoutTheSoldiersJump()
    {
        // The control: the book advises a jump for the run, and a Soldier on it takes it (TFJumpRuleTests).
        var f = new TFField(jumps: new TFJumpAdvice());
        Assert.Equal(1, AbilityIntents(new TFField(jumps: new TFJumpAdvice()), () => Running(), 20, out _));
        Assert.Equal(0, AbilityIntents(f, () => Running(TFClass.Scout), 20, out List<IntentCommand> intents));
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag), (intents[^1].Move.Kind, intents[^1].Move.Corner0));
        Assert.Equal("Scout", f.Decider.Subtree);
        Assert.StartsWith("Scout: ", f.Decider.Doing.Branch, StringComparison.Ordinal);
    }

    /// <summary>Each of the nine classes' subtrees is named by the class as its menu says it: a Heavy is "Heavy", not the enum's word.</summary>
    [Theory]
    [InlineData(TFClass.Scout, "Scout")]
    [InlineData(TFClass.Sniper, "Sniper")]
    [InlineData(TFClass.Soldier, "Soldier")]
    [InlineData(TFClass.Demoman, "Demoman")]
    [InlineData(TFClass.Medic, "Medic")]
    [InlineData(TFClass.HeavyWeapons, "Heavy")]
    [InlineData(TFClass.Pyro, "Pyro")]
    [InlineData(TFClass.Spy, "Spy")]
    [InlineData(TFClass.Engineer, "Engineer")]
    public void EachClassRunsASubtreeNamedForIt(TFClass playerClass, string name)
    {
        var f = new TFField();
        f.Step(Running(playerClass), TFField.Ctf());
        Assert.Equal((name, name + ": " + TFDecider.Fetch), (f.Decider.Subtree, f.Decider.Doing.Branch));
    }

    /// <summary>A bot that comes back as another class runs that class's subtree, named anew.</summary>
    [Fact]
    public void ABotThatChangesClassRunsTheNewClasssSubtree()
    {
        var f = new TFField();
        f.Step(Running(TFClass.Soldier), TFField.Ctf());
        f.Step(Running(TFClass.Medic), TFField.Ctf());
        Assert.Equal(("Medic", "Medic: " + TFDecider.Fetch), (f.Decider.Subtree, f.Decider.Doing.Branch));
    }

    [Fact]
    public void ASoldierRunsItsOwnSubtree()
    {
        var f = new TFField();
        f.Step(Running(), TFField.Ctf());
        Assert.Equal("Soldier", f.Decider.Subtree);
        Assert.Equal("Soldier: " + TFDecider.Fetch, f.Decider.Doing.Branch);
    }

    /// <summary>The applier counts the commands that fire at a target's feet (the counters' ai.apply.fire.feet): none at the body.</summary>
    [Fact]
    public void TheApplierCountsTheShotsAimedAtTheFeet()
    {
        long Fired(AiVector velocity)
        {
            var f = new TFField();
            var runner = new Runner();
            var applier = new BotCommandApplier(runner, new StillSelf(runner), null, TFAbilities.Table(), new NoCommands());
            applier.Add(f.Bot, new EntityHandle(0x0001_0002u), new MotionParams(200f, 487.5f, 16.2f, 174f), 42);
            int tick = 0;
            for (int d = 0; d < 40; d++)
            {
                IntentCommand i = f.Step(TFField.Self(new AiVector(300f, 256f, 0f)), TFField.Ctf(), (Enemy, new AiVector(900f, 256f, 0f), velocity, TFField.Blue));
                applier.Submit(in i);
                for (; tick < f.Tick; tick++)
                    applier.ApplyTick(new Tick(tick + 1), 0.015f);
            }

            return applier.FeetFireCommands;
        }

        Assert.True(Fired(AiVector.Zero) > 0, "a grounded target in sight for 240 ticks drew no fire at its feet");
        Assert.Equal(0, Fired(new AiVector(0f, 0f, 300f)));
    }

    private static string BranchOf(TFField f) => f.Decider.Doing.Branch[(f.Decider.Doing.Branch.IndexOf(": ", StringComparison.Ordinal) + 2)..];

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    /// <summary>A bot standing at (300 256 0) whose view is its last command's, as a client's own is.</summary>
    private sealed class StillSelf(Runner? runner = null) : IBotSelf
    {
        public BotSelfFacts Read(EntityHandle player)
            => new(true, false, new AiVector(300f, 256f, 0f), new AiVector(300f, 256f, 68f),
                   runner is null || runner.Queued.Count == 0 ? default : new AiAngles(runner.Queued[^1].Pitch, runner.Queued[^1].Yaw, 0f));

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private sealed class NoCommands : IBotClientCommand
    {
        public void Run(EntityHandle bot, string command)
        {
        }
    }
}
