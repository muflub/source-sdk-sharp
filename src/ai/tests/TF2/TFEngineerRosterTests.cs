using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Trees;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The Engineer in his team's roster: the composition's cap (stock's defence roster limit, tf_bot.cpp:835: one per four
/// members, at least one, at most three) and stock's CanChangeClass (tf_bot.cpp:1156-1163: no change with a sentry or a
/// teleporter exit standing), read off the game's buildings by builder.
/// </summary>
public sealed class TFEngineerRosterTests
{
    private static bool SoldierOrEngineer(TFClass c) => c is TFClass.Soldier or TFClass.Engineer;

    /// <summary>A team of <paramref name="size"/> picking one bot at a time, as first picks do, from Soldier and Engineer only.</summary>
    private static int[] Fill(int size, TFTeamStance stance)
    {
        var counts = new int[(int)TFClass.CountAll];
        for (int i = 0; i < size; i++)
            counts[(int)TFClassComposition.Pick(counts, stance, SoldierOrEngineer)]++;
        return counts;
    }

    [Fact]
    public void TheEngineersCapIsStocksDefenceRoster()
        => Assert.Equal((1, 1, 2, 3, 3), (TFClassComposition.Cap(TFClass.Engineer, 3), TFClassComposition.Cap(TFClass.Engineer, 4),
                                          TFClassComposition.Cap(TFClass.Engineer, 8), TFClassComposition.Cap(TFClass.Engineer, 12),
                                          TFClassComposition.Cap(TFClass.Engineer, 24)));

    /// <summary>No team of any size, in any stance, fields more Engineers than his cap.</summary>
    [Fact]
    public void ATeamNeverFieldsMoreEngineersThanTheCap()
    {
        foreach (TFTeamStance stance in new[] { TFTeamStance.Symmetric, TFTeamStance.Attack, TFTeamStance.Defend })
        {
            for (int size = 1; size <= 16; size++)
                Assert.True(Fill(size, stance)[(int)TFClass.Engineer] <= TFClassComposition.Cap(TFClass.Engineer, size), $"{stance} {size}");
        }
    }

    /// <summary>
    /// With only Soldier and Engineer ready a team of 8 gets two Engineers and six Soldiers (the review: by the shares alone it
    /// alternated, four of each).
    /// </summary>
    [Fact]
    public void ATeamOfEightGetsTheCapOfEngineersAndSoldiersForTheRest()
    {
        int[] team = Fill(8, TFTeamStance.Symmetric);
        Assert.Equal((2, 6), (team[(int)TFClass.Engineer], team[(int)TFClass.Soldier]));
    }

    /// <summary>A class with no cap of its own (the Soldier) has none.</summary>
    [Fact]
    public void AClassWithNoCapHasNone()
        => Assert.Equal(int.MaxValue, TFClassComposition.Cap(TFClass.Soldier, 8));

    private sealed class Commands : IBotClientCommand
    {
        public List<(EntityHandle Bot, string Command)> Sent { get; } = [];

        public void Run(EntityHandle bot, string command) => Sent.Add((bot, command));
    }

    private static (TFBotClassPick Pick, Commands Sent) Pick()
    {
        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>
        {
            ["ai_bot_classes_ready"] = "medic,engineer", ["ai_bot_class_cooldown"] = "30", ["ai_bot_class_stay"] = "120",
        }));
        var sent = new Commands();
        return (new TFBotClassPick(null!, null!, null!, sent, null!, new TFBotPins(), convars.Declare<AiBotClassCooldownConVar>(),
                                   convars.Declare<AiBotClassStayConVar>(), convars.Declare<AiBotClassesReadyConVar>(), null!, null!, null!), sent);
    }

    /// <summary>A red team of five Soldiers and the dead Engineer: it wants a Medic, so the Engineer would change.</summary>
    private static int[] Team()
    {
        var counts = new int[(int)TFClass.CountAll];
        counts[(int)TFClass.Soldier] = 5;
        counts[(int)TFClass.Engineer] = 1;
        return counts;
    }

    private static readonly EntityHandle Engineer = new(0x0001_0003u);

    private static List<string> DiesAndIsWeighed(bool holds)
    {
        (TFBotClassPick pick, Commands sent) = Pick();
        pick.Step(10f, false, [new TFClassSeat(3, Engineer, (int)TFTeam.Red, TFClass.Engineer, false, null, holds)], Team(),
                  new int[(int)TFClass.CountAll], 30f, 120f);
        return [.. sent.Sent.Select(s => s.Command)];
    }

    /// <summary>A dead Engineer with his sentry standing does not change class; the control, without one, does.</summary>
    [Fact]
    public void ADeadEngineerWithHisSentryUpDoesNotChangeClass()
    {
        Assert.Equal(["joinclass medic"], DiesAndIsWeighed(holds: false));
        Assert.Empty(DiesAndIsWeighed(holds: true));
    }

    /// <summary>His buildings, as the game's directory answers by builder, type and mode.</summary>
    private sealed class Buildings(params (EntityHandle Builder, TFObjectType Type, int Mode)[] standing) : ITFBuildings
    {
        public IEnumerable<TFBaseObject> OwnedBy(EntityHandle? builder, TFObjectType? type, int? mode)
        {
            foreach (var b in standing)
            {
                if ((builder is null || builder == b.Builder) && (type is null || type == b.Type) && (mode is null || mode == b.Mode))
                    yield return (TFBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(TFSentryGun));
            }
        }
    }

    [Fact]
    public void AnEngineerWithASentryUpHoldsHisClass()
        => Assert.True(TFBotClassPick.HoldsClass(new Buildings((Engineer, TFObjectType.Sentrygun, 0)), Engineer, TFClass.Engineer));

    [Fact]
    public void AnEngineerWithATeleporterExitUpHoldsHisClass()
        => Assert.True(TFBotClassPick.HoldsClass(new Buildings((Engineer, TFObjectType.Teleporter, 1)), Engineer, TFClass.Engineer));

    /// <summary>Not held: a dispenser or an entrance only, another player's sentry, or another class.</summary>
    [Fact]
    public void ADispenserAnEntranceAnotherOnesSentryOrAnotherClassHoldsNothing()
    {
        var other = new EntityHandle(0x0001_0004u);
        Assert.False(TFBotClassPick.HoldsClass(new Buildings((Engineer, TFObjectType.Dispenser, 0), (Engineer, TFObjectType.Teleporter, 0)), Engineer, TFClass.Engineer));
        Assert.False(TFBotClassPick.HoldsClass(new Buildings((other, TFObjectType.Sentrygun, 0)), Engineer, TFClass.Engineer));
        Assert.False(TFBotClassPick.HoldsClass(new Buildings((Engineer, TFObjectType.Sentrygun, 0)), Engineer, TFClass.Soldier));
    }
}
