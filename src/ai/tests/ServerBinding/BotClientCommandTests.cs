using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerBinding;

/// <summary>
/// A bot's client command (plan_bots_teams_tf2 C.3) takes a player's route to a game's verb: the
/// mods' command chain in its order, the first hook that claims the word owns it (the verb and its
/// arguments as a typed command arrives), a hook that throws is skipped, and a word nobody claims
/// is named in the log.
/// </summary>
public sealed class BotClientCommandTests
{
    private static readonly EntityHandle Bot = new(0x0001_0004u);

    private sealed class Hook(string verb, List<string> heard, bool throws = false) : IClientCommandHook
    {
        public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
        {
            heard.Add($"{verb}?{command}({string.Join(",", args.ToArray())})@{player.Value:X}");
            if (throws)
                throw new InvalidOperationException("broken mod");
            return command == verb;
        }
    }

    private sealed class Chain(params IClientCommandHook[] all) : IHooks<IClientCommandHook>
    {
        public IReadOnlyList<IClientCommandHook> All => all;

        public void Each(string what, Action<IClientCommandHook> call)
        {
            foreach (IClientCommandHook h in all)
                call(h);
        }
    }

    private sealed class Lines : ILog
    {
        public readonly List<string> Warnings = new();
        public void Msg(string message) { }
        public void Warning(string message) => Warnings.Add(message);
    }

    [Fact]
    public void TheFirstHookThatClaimsTheWordOwnsIt()
    {
        var heard = new List<string>();
        var log = new Lines();
        var run = new HookedBotClientCommand(new Chain(new Hook("disguise", heard), new Hook("build", heard), new Hook("build", heard)), log);
        run.Run(Bot, "build 2 0");
        Assert.Equal(["disguise?build(2,0)@10004", "build?build(2,0)@10004"], heard);
        Assert.Equal(1, run.Claimed);
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void AHookThatThrowsIsSkipped()
    {
        var heard = new List<string>();
        var log = new Lines();
        var run = new HookedBotClientCommand(new Chain(new Hook("x", heard, throws: true), new Hook("cloak", heard)), log);
        run.Run(Bot, "cloak");
        Assert.Equal(1, run.Claimed);
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void AWordNobodyClaimsIsNamed()
    {
        var log = new Lines();
        var run = new HookedBotClientCommand(new Chain(new Hook("build", [])), log);
        run.Run(Bot, "taunt");
        Assert.Equal(0, run.Claimed);
        Assert.Contains("'taunt'", Assert.Single(log.Warnings));
    }
}
