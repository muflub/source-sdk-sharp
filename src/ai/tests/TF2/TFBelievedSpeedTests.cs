using System.Reflection;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2;
using SourceSharp.Ai.TF2.Knowledge;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// How fast a TF2 bot believes an enemy out of sight moves (plan_bots_teams_tf2_part_g G.2.1):
/// its class's run from the class table, not HL2MP's 190 for everyone. The game's map knowledge
/// carries a body a class; one whose class the bot has not seen runs at the classes' mean; and a
/// Scout lost from sight is believed further along than a Heavy after the same time.
/// </summary>
public sealed class TFBelievedSpeedTests
{
    private static (float, int) Stock(TFClass c) => c switch
    {
        TFClass.Scout => (400f, 125),
        TFClass.Soldier => (240f, 200),
        TFClass.Demoman => (280f, 175),
        TFClass.HeavyWeapons => (230f, 300),
        TFClass.Medic => (320f, 150),
        _ => (300f, 125),
    };

    private static readonly BodyKnowledge Bodies = TFKnowledge.Bodies(TFKnowledge.Classes(Stock));

    [Fact]
    public void EachClassesBodyRunsAsItsScriptSays()
        => Assert.Equal((400f, 240f, 230f, 300f),
                        (Bodies.Of((int)TFClass.Scout).RunSpeed, Bodies.Of((int)TFClass.Soldier).RunSpeed, Bodies.Of((int)TFClass.HeavyWeapons).RunSpeed,
                         Bodies.Of((int)TFClass.Spy).RunSpeed));

    [Fact]
    public void AnEnemyWhoseClassWasNotSeenRunsAtTheClassesMean()
        => Assert.Equal((400f + 240f + 280f + 230f + 320f + 4 * 300f) / 9f, Bodies.Of(0).RunSpeed, 3);

    [Fact]
    public void AClassTableNotLoadedLeavesTheCoresDefault()
        => Assert.Equal(BodyKnowledge.DefaultRunSpeed, TFKnowledge.Bodies(TFKnowledge.Classes(_ => (0f, 0))).Of((int)TFClass.Scout).RunSpeed);

    [Fact]
    public void AClassWhoseScriptGaveNoSpeedHasNoRow()
    {
        BodyKnowledge b = TFKnowledge.Bodies(TFKnowledge.Classes(c => c == TFClass.Scout ? (0f, 0) : (300f, 125)));
        Assert.DoesNotContain(b.Classes, r => r.Class == (int)TFClass.Scout);
        Assert.Equal(300f, b.Of((int)TFClass.Scout).RunSpeed);
    }

    [Fact]
    public void TheMapsKnowledgeCarriesTheBodiesItIsGiven()
        => Assert.Same(Bodies, TFMapKnowledge.Build("m", new MapEntities([], null), new WeaponKnowledge([]), default, Bodies).Bodies);

    /// <summary>A file seam holding each class's script as plain text; a level's directory with nothing placed.</summary>
    public class Scripts : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IEngineFiles.ReadGameFileBytes))
                return null;
            foreach (TFClass c in TFKnowledge.Playable())
            {
                if ((string)args![0]! == TFClassTable.ScriptName(c) + ".txt")
                {
                    (float speed, int health) = Stock(c);
                    return Encoding.UTF8.GetBytes(FormattableString.Invariant($"\"{TFClassNames.Raw(c)}\"\n{{\n\"name\" \"{TFClassNames.Raw(c)}\"\n\"speed_max\" \"{speed}\"\n\"health_max\" \"{health}\"\n}}\n"));
                }
            }

            return null;
        }
    }

    public class NothingPlaced : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IEntityDirectory.OfClass) ? Array.Empty<ScriptedEntity>() : throw new InvalidOperationException($"asked {method?.Name}");
    }

    private sealed class NoLog : ILog
    {
        public void Msg(string message)
        {
        }

        public void Warning(string message)
        {
        }
    }

    /// <summary>The wiring: the game's own map knowledge has the class table's runs.</summary>
    [Fact]
    public void TheGamesMapKnowsEachClassesRunFromTheClassTable()
    {
        var classes = new TFClassTable(new NoLog(), DispatchProxy.Create<IEngineFiles, Scripts>());
        Assert.Equal(9, classes.Load());
        var placed = new TFPlacedEntities(DispatchProxy.Create<IEntityDirectory, NothingPlaced>(), DispatchProxy.Create<IEngineEntities, TFViewWiringTests.Unasked>());
        var game = new TFBotGame(WeaponCatalog.Stock(), null!, classes, placed, null!, TFFeelSwitchTests.Switches());
        MapKnowledge map = game.Map("m", new MapEntities([], null), new WeaponKnowledge([]));
        Assert.Equal((400f, 230f), (map.Bodies.Of((int)TFClass.Scout).RunSpeed, map.Bodies.Of((int)TFClass.HeavyWeapons).RunSpeed));
    }

    private static readonly ActorId Bot = new(2, 1);
    private static readonly ActorId Them = new(5, 0x4000_0001);
    private static readonly AiVector Stand = new(3900f, 256f, 0f), LostAt = new(96f, 288f, 0f), East = new(300f, 0f, 0f);

    private static PerceivedView View(int tick, TFClass? seenAs)
    {
        var v = new PerceivedView(4);
        v.Reset(Bot, new Tick(tick), new SelfState(Stand, default, default, 200, 0, TFField.Launcher, 4, 20, SelfFlags.Alive, Team: TFField.Red, Class: (int)TFClass.Soldier));
        if (seenAs is { } c)
            v.AddSighted(new ActorFacts(Them, LostAt, East, -1, ActorActivity.None, 1f, TFField.Blue, (int)c));
        return v;
    }

    /// <summary>How far east the bot believes an enemy drawn as <paramref name="seenAs"/> has got, <paramref name="seconds"/> after losing it at the field's west end.</summary>
    private static float BelievedEast(TFClass seenAs, float seconds)
    {
        NavGraph graph = TFField.Floor();
        var map = new MapKnowledge("tf_field", [], [], TFField.Weapons, bodies: Bodies);
        var m = new BotMemory(Bot, 33, new MemoryParams(60f, 120f), new MemoryWorld(map, graph, 0.015f, id => id.ToString()), new HabitStore());
        m.Step(new Tick(1), View(1, seenAs), [AiEvent.SightGained(Bot, new Tick(1), Them, LostAt, East, 1f)]);
        m.Step(new Tick(2), View(2, null), [AiEvent.SightLost(Bot, new Tick(2), Them, LostAt, East)]);
        int last = 2 + (int)(seconds / 0.015f);
        for (int t = 12; t <= last; t += 10)
            m.Step(new Tick(t), View(t, null), []);
        PositionPredictor where = m.Board.Actor(Them.Slot).Where!;
        Assert.True(where.Active, "the control: a belief is held");
        float east = 0f, mass = 0f;
        for (int i = 0; i < where.CellsWithMass; i++)
        {
            east += where.Masses[i] * graph.Cell(where.MassCells[i]).Centre.X;
            mass += where.Masses[i];
        }

        return east / mass;
    }

    [Fact]
    public void AScoutOutOfSightIsBelievedFurtherAlongThanAHeavyAfterTheSameTime()
    {
        float scout = BelievedEast(TFClass.Scout, 3f), heavy = BelievedEast(TFClass.HeavyWeapons, 3f);
        Assert.True(heavy > LostAt.X + 64f, $"the control: the Heavy is believed to have moved, to {heavy:F0}");
        Assert.True(scout > heavy + 128f, $"scout {scout:F0}, heavy {heavy:F0}");
    }
}
