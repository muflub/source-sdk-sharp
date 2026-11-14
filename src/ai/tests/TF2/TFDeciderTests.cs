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
/// home flag while an attacker fetches the enemy's, a grounded target is shot at the feet, a rocket
/// jump is applied exactly once on a long clear run, and a class with no subtree of its own runs
/// the Soldier's and says so, without the Soldier's jump.
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

        var defender = new TFField(slot: 3);
        defender.Step(TFField.Self(far), taken);
        Assert.Equal(TFDecider.ChaseCarrier, BranchOf(defender));
    }

    [Fact]
    public void ADefenderHoldsItsHomeFlag()
    {
        var f = new TFField(slot: 3);
        Assert.Equal(TFTeamRole.Defend, f.Decider.Role);
        IntentCommand i = f.Step(TFField.Self(Mid), TFField.Ctf());

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

        var defender = new TFField(slot: 3);
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

    private static int AbilityIntents(TFField f, Func<SelfState> self, int decides, out List<IntentCommand> intents)
    {
        intents = [.. Enumerable.Range(0, decides).Select(_ => f.Step(self(), TFField.Ctf()))];
        return intents.Count(i => i.Action.Kind == ActionKind.Ability);
    }

    [Fact]
    public void ASoldierOnALongClearRunRocketJumps()
    {
        var f = new TFField();
        Assert.Equal(1, AbilityIntents(f, () => Running(), 20, out List<IntentCommand> intents));
        IntentCommand jump = intents.Single(i => i.Action.Kind == ActionKind.Ability);
        Assert.True(f.Memory.World.Map.Abilities.TryGet(jump.Action.WeaponSlot, out AbilityRow row));
        Assert.Equal(TFAbilities.RocketJump, row.Name);
        Assert.True(jump.Action.OneShotSeq > 0);
        Assert.Equal(1, f.Decider.RocketJumps);
    }

    [Fact]
    public void ARocketJumpWaitsItsCooldownBeforeTheNext()
    {
        var f = new TFField();
        int decides = (int)(TFDecider.RocketJumpEverySeconds / 0.015f / DecideSchedule.Period);
        Assert.Equal(1, AbilityIntents(f, () => Running(), decides - 1, out _));
        Assert.Equal(1, AbilityIntents(f, () => Running(), 3, out _));
    }

    [Fact]
    public void NoRocketJumpUnderTheHealthFloor()
        => Assert.Equal(0, AbilityIntents(new TFField(), () => Running(health: (int)TFDecider.RocketJumpHealthFloor - 1), 20, out _));

    [Fact]
    public void NoRocketJumpWithAnEmptyLauncher()
        => Assert.Equal(0, AbilityIntents(new TFField(), () => Running(clip: 0), 20, out _));

    [Fact]
    public void NoRocketJumpWhereTheNavSaysNothing()
        => Assert.Equal(0, AbilityIntents(new TFField(graph: false), () => Running(), 20, out _));

    [Fact]
    public void NoRocketJumpOffTheEdgeOfTheNav()
        // The floor ends 10 cells east (640 u), a little past the bot: its landing, 512 u on, is no floor.
        => Assert.Equal(0, AbilityIntents(new TFField(floorLength: 10), () => Running(), 20, out _));

    [Fact]
    public void NoRocketJumpAwayFromItsGoal()
        // Moving west (round a corner, say) while the flag is 1,800 u east: the jump would land off its way.
        => Assert.Equal(0, AbilityIntents(new TFField(), () => TFField.Self(new AiVector(2000f, 256f, 0f), velocity: new AiVector(-240f, 0f, 0f)), 20, out _));

    [Fact]
    public void NoRocketJumpWithAnEnemyInSight()
    {
        // A defender chasing the enemy carrying its flag (a run, which outranks the fight) with another enemy in sight.
        var f = new TFField(slot: 3);
        ObjectiveKnowledge carried = TFField.Ctf(red: ObjectiveState.Carried, redAt: new AiVector(3700f, 256f, 0f), redCarrier: 9);
        List<IntentCommand> intents = [.. Enumerable.Range(0, 20).Select(_ => f.Step(Running(), carried, (Enemy, new AiVector(2500f, 256f, 0f), AiVector.Zero, TFField.Blue)))];
        Assert.Equal(TFDecider.ChaseCarrier, BranchOf(f));
        Assert.Equal(MoveKind.Follow, intents[^1].Move.Kind);
        Assert.Equal(0, intents.Count(i => i.Action.Kind == ActionKind.Ability));
    }

    [Fact]
    public void AClassWithNoSubtreeRunsTheSoldiersAndSaysSoWithoutItsJump()
    {
        var f = new TFField();
        Assert.Equal(0, AbilityIntents(f, () => Running(TFClass.Scout), 20, out List<IntentCommand> intents));
        Assert.Equal((MoveKind.Follow, TFField.BlueFlag), (intents[^1].Move.Kind, intents[^1].Move.Corner0));
        Assert.Equal("Soldier (as Scout)", f.Decider.Subtree);
        Assert.StartsWith("Soldier (as Scout): ", f.Decider.Doing.Branch, StringComparison.Ordinal);
    }

    [Fact]
    public void ASoldierRunsItsOwnSubtree()
    {
        var f = new TFField();
        f.Step(Running(), TFField.Ctf());
        Assert.Equal("Soldier", f.Decider.Subtree);
        Assert.Equal("Soldier: " + TFDecider.Fetch, f.Decider.Doing.Branch);
    }

    /// <summary>
    /// The decider's intents through the applier, with TF2's ability table: on a long clear run the
    /// rocket jump's sequence runs exactly once (one tick of jump, duck and fire), though the intent
    /// is resubmitted every decide.
    /// </summary>
    [Fact]
    public void ARocketJumpIsAppliedExactlyOnce()
    {
        var f = new TFField();
        var runner = new Runner();
        var applier = new BotCommandApplier(runner, new StillSelf(), null, TFAbilities.Table(), new NoCommands());
        var player = new EntityHandle(0x0001_0002u);
        applier.Add(f.Bot, player, new MotionParams(200f, 487.5f, 16.2f, 174f), 42);
        int tick = 0;
        for (int d = 0; d < 40; d++)
        {
            IntentCommand i = f.Step(Running(), TFField.Ctf());
            applier.Submit(in i);
            for (; tick < f.Tick; tick++)
                applier.ApplyTick(new Tick(tick + 1), 0.015f);
        }

        const int launch = (int)(InputButtons.Jump | InputButtons.Duck | InputButtons.Attack);
        int starts = 0;
        for (int c = 1; c < runner.Queued.Count; c++)
        {
            if ((runner.Queued[c].Buttons & launch) == launch && (runner.Queued[c - 1].Buttons & launch) != launch)
                starts++;
        }

        Assert.Equal(1, f.Decider.RocketJumps);
        Assert.Equal(1, starts);
        Assert.Equal(1, applier.AbilitySequences);
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
