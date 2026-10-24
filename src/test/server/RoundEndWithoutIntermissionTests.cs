using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Rules built without an intermission end a round by marking it over and
/// nothing else. The native rules' EndMultiplayerGame, which the old fallback
/// reached through IShimGameRules::EndRound, has an empty body
/// (CSourceSharpGameRules), so the engine is not asked at all.
/// </summary>
public class RoundEndWithoutIntermissionTests
{
    [Fact]
    public void The_rules_mark_the_round_over_and_ask_the_engine_nothing()
    {
        _ = Services(out var engine);
        var convars = ConVarRegistry.For(UnattachedConsole());
        convars.RegisterAll(typeof(GameRules).Assembly);
        var deaths = new PlayerDeathEvent();
        var board = new Scoreboard(engine, new PlayerDisconnectEvent(), new RecordingLog());
        var rules = new GameRules(new RecordingLog(), convars, engine, engine, engine, board, deaths, new ListHooks<IDamageRule>(), new ListHooks<ILoadoutProvider>(), null);
        engine.Clear();

        rules.EndRound();

        Assert.True(rules.RoundOver);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void The_end_round_command_asks_the_engine_nothing()
    {
        _ = Services(out var engine);
        var log = new RecordingLog();
        engine.Clear();

        new EndRoundCommand(log, engine).Execute("");

        Assert.Empty(engine.Calls);
        Assert.Contains("ending round on request", log.Text);
    }
}
