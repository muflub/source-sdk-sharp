using System.Collections.Immutable;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server.Interop;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The class files composed (plan_bots_teams_tf2 P7): the one ability table keeps TF2's shared rows where they were
/// and gives each class a block of its own, so a row one class adds moves no other; and only a class whose file says
/// so is ready to be fielded.
/// </summary>
public class TFClassSpecsTests
{
    private static TFClassSpec Spec(TFClass c, params string[] rows)
        => new(c, false, [.. rows.Select(r => new AbilityRow(r, AbilityRun.Command, "say " + r))]);

    private static int IndexOf(AbilityTable table, string name) => table.Rows.IndexOf(table.Rows.Single(r => r.Name == name));

    /// <summary>The shared rows are at the indices a decider and the recordings knew before P7: the rocket jump 0, the voice lines from 11.</summary>
    [Fact]
    public void TheSharedRowsKeepTheirIndices()
    {
        AbilityTable table = TFAbilities.Table();
        ImmutableArray<AbilityRow> shared = TFAbilities.Shared();
        Assert.Equal(15, shared.Length);
        Assert.Equal(shared, table.Rows.Take(shared.Length));
        Assert.Equal((0, 11), (IndexOf(table, TFAbilities.RocketJump), IndexOf(table, TFAbilities.VoiceMedic)));
    }

    /// <summary>A class's first row is at its block: the Scout's at 16, the Engineer's at 144.</summary>
    [Fact]
    public void AClassRowSitsAtItsClasssBlock()
    {
        AbilityTable table = TFClassSpecs.Compose(TFAbilities.Shared(), [Spec(TFClass.Scout, "dash"), Spec(TFClass.Engineer, "wrangle")]);
        Assert.Equal((16, 144), (IndexOf(table, "dash"), IndexOf(table, "wrangle")));
    }

    /// <summary>A row one class adds moves none of another class's rows, nor the shared ones.</summary>
    [Fact]
    public void ARowAddedToOneClassMovesNoOtherRow()
    {
        AbilityTable before = TFClassSpecs.Compose(TFAbilities.Shared(), [Spec(TFClass.Scout, "dash"), Spec(TFClass.Sniper, "scope")]);
        AbilityTable after = TFClassSpecs.Compose(TFAbilities.Shared(), [Spec(TFClass.Scout, "dash", "drink", "stun"), Spec(TFClass.Sniper, "scope")]);
        Assert.Equal(IndexOf(before, "scope"), IndexOf(after, "scope"));
        Assert.Equal(before.Rows.Take(15), after.Rows.Take(15));
        Assert.Equal(18, IndexOf(after, "stun"));
    }

    /// <summary>A class with more rows than its block is refused: the next class's rows would move.</summary>
    [Fact]
    public void AClassThatOverflowsItsBlockIsRefused()
    {
        string[] many = [.. Enumerable.Range(0, TFClassSpecs.BlockRows + 1).Select(i => $"row{i}")];
        Assert.Throws<InvalidOperationException>(() => TFClassSpecs.Compose(TFAbilities.Shared(), [Spec(TFClass.Scout, many)]));
    }

    /// <summary>Two files for one class are refused: whose rows the block holds would depend on the order.</summary>
    [Fact]
    public void TwoFilesForOneClassAreRefused()
        => Assert.Throws<InvalidOperationException>(() => TFClassSpecs.Compose(TFAbilities.Shared(), [Spec(TFClass.Scout, "a"), Spec(TFClass.Scout, "b")]));

    /// <summary>The shipped table has a file for each of the nine classes, and every class's rows lie inside its own block.</summary>
    [Fact]
    public void EveryShippedClassRowLiesInItsOwnBlock()
    {
        Assert.Equal(TFKnowledge.Playable().Order(), TFClassSpecs.All().Select(s => s.Class).Order());
        AbilityTable table = TFAbilities.Table();
        foreach (TFClassSpec spec in TFClassSpecs.All())
        {
            int block = TFClassSpecs.BlockOf(spec.Class);
            for (int i = 0; i < spec.Abilities.Length; i++)
                Assert.Equal(spec.Abilities[i], table.Rows[block + i]);
        }

        Assert.True(table.Count - 1 <= AbilityCall.MaxRow, $"{table.Count} rows");
    }

    private sealed class Runner : IPlayerCommandRunner
    {
        public readonly List<UserCmd> Queued = new();

        public void Queue(EntityHandle player, in UserCmd cmd) => Queued.Add(cmd);

        public int RunQueued(EntityHandle player) => 1;
    }

    private sealed class Self : IBotSelf
    {
        public BotSelfFacts Read(EntityHandle player) => new(true, false, default, new AiVector(0f, 0f, 64f), default);

        public int WeaponSelectFor(EntityHandle player, int slot) => 0;
    }

    private sealed class Commands : IBotClientCommand
    {
        public readonly List<string> Sent = new();

        public void Run(EntityHandle bot, string command) => Sent.Add(command);
    }

    /// <summary>
    /// A one-shot naming a block's unused row sends nothing and presses nothing; the control, the row of a shared
    /// command (the first building order), sends its word.
    /// </summary>
    [Fact]
    public void AnUnusedRowRunsNothing()
    {
        (int Sent, int Buttons) Run(int row)
        {
            var runner = new Runner();
            var commands = new Commands();
            var applier = new BotCommandApplier(runner, new Self(), null, TFAbilities.Table(), commands);
            var bot = new ActorId(3, 1);
            applier.Add(bot, new EntityHandle(0x0001_0003u), new MotionParams(500f, 487.5f, 16.2f, 174f), 42);
            var from = new Tick(100);
            applier.Submit(new IntentCommand(bot, from, 1, from + IntentCommand.LifetimeTicks, MoveIntent.Hold, LookIntent.AlongPath,
                                             new ActionIntent(ActionKind.Ability, FireStyle.Tap, row, OneShotSeq: 1), default));
            applier.ApplyTick(new Tick(100), 0.015f);
            applier.ApplyTick(new Tick(101), 0.015f);
            return (commands.Sent.Count, runner.Queued.Sum(c => c.Buttons));
        }

        Assert.Equal((1, 0), Run(IndexOf(TFAbilities.Table(), "build_sentry")));
        Assert.Equal((0, 0), Run(TFClassSpecs.BlockOf(TFClass.Scout)));
    }
}
