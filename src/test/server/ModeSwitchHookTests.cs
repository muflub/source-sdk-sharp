using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A mode switch is heard (plan_bots_teams_tf2 D.2): the chain is told the mode
/// left and the mode taken once the new round is set up, and votes send
/// everyone the new list of what can be voted on.
/// </summary>
public class ModeSwitchHookTests
{
    private sealed class ListHooks<T>(params T[] hooks) : IHooks<T> where T : class
    {
        public IReadOnlyList<T> All => hooks;
        public void Each(string what, Action<T> call) { foreach (var h in hooks) call(h); }
    }

    private sealed class Heard : IModeSwitchHook
    {
        public readonly List<(string? From, string To)> Switches = [];
        public void OnModeSwitched(IGameMode? previous, IGameMode next) => Switches.Add((previous?.Id, next.Id));
    }

    private sealed class Options : IVoteOptions
    {
        public IReadOnlyList<VoteOption> Build() => [new(VoteKind.Restart, "restart", "", "mp_restartgame 1")];
    }

    [Fact]
    public void ASwitchTellsTheChainTheModeLeftAndTheModeTaken()
    {
        var rig = new TeamDeathmatchTests.Rig();
        var heard = new Heard();
        rig.Services.AddInstance<IHooks<IModeSwitchHook>>(new ListHooks<IModeSwitchHook>(heard));
        _ = rig.Modes.Active;

        rig.Tdm();
        rig.Modes.SwitchTo(GameModes.NullModeId);

        Assert.Equal([("null", "tdm"), ("tdm", "null")], heard.Switches);
    }

    [Fact]
    public void AnUnknownModeIsNoSwitchAndTellsNoOne()
    {
        var rig = new TeamDeathmatchTests.Rig();
        var heard = new Heard();
        rig.Services.AddInstance<IHooks<IModeSwitchHook>>(new ListHooks<IModeSwitchHook>(heard));

        Assert.False(rig.Modes.SwitchTo("nosuch"));

        Assert.Empty(heard.Switches);
    }

    [Fact]
    public void VotesSendEveryoneTheNewListOnASwitch()
    {
        var engine = TestServices.Fake();
        var votes = new ServerVotes(new VoteOffice(), engine, engine, engine, engine, engine, options: new Options());

        votes.OnModeSwitched(null, new TeamDeathmatchTests.Rig().Mode);

        var sent = Assert.Single(engine.AllOf("UserMessages.Send"));
        Assert.Equal(("SourceSharp:VoteOptionsPiece", 0), ((string)sent.Args[0]!, (int)sent.Args[2]!));
    }
}
