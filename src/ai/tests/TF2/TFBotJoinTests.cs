using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.Server.Teams;
using SourceSharp.Ai.TF2;
using SourceSharp.Server;
using SourceSharp.Shared;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// A TF2 bot joins as a player's keys do (plan_bots_teams_tf2 C.4, P6 step 7): its team through
/// the mod's own <c>jointeam</c> word (a team's name; auto for the mode's pick), and, on a playing
/// team with no class asked for, <c>joinclass soldier</c>; a game whose menu sends the core's
/// <c>jointeam N</c> is joined through the team service as before.
/// </summary>
public sealed class TFBotJoinTests
{
    private static readonly EntityHandle Player = new(0x0001_0004u);

    private sealed class Commands : IBotClientCommand
    {
        public readonly List<string> Sent = new();

        public void Run(EntityHandle bot, string command) => Sent.Add(command);
    }

    public class Recorder : DispatchProxy
    {
        public readonly List<string> Calls = new();

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls.Add($"{method!.Name}({string.Join(",", args ?? [])})");
            return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private static TFBotGame Game() => new(WeaponCatalog.Stock(), null!, null!, null!, null!);

    [Theory]
    [InlineData(0, "jointeam auto")]
    [InlineData((int)TFTeam.Red, "jointeam red")]
    [InlineData((int)TFTeam.Blue, "jointeam blue")]
    [InlineData((int)TFTeam.Spectator, "jointeam spectate")]
    public void TF2sTeamMenuWordIsTheTeamsName(int team, string word) => Assert.Equal(word, Game().JoinTeamWord(team));

    [Fact]
    public void ABotJoinsATF2TeamWithTheModsWordNotTheTeamService()
    {
        var commands = new Commands();
        ITeams teams = DispatchProxy.Create<ITeams, Recorder>();
        RosterJoin.Join(Game(), commands, teams, Player, 0);
        Assert.Equal(["jointeam auto"], commands.Sent);
        Assert.Empty(((Recorder)(object)teams).Calls);
    }

    [Fact]
    public void AGameWithNoWordOfItsOwnIsJoinedThroughTheTeamService()
    {
        var commands = new Commands();
        ITeams teams = DispatchProxy.Create<ITeams, Recorder>();
        RosterJoin.Join(DispatchProxy.Create<IBotGame, Recorder>(), commands, teams, Player, 3);
        Assert.Empty(commands.Sent);
        Assert.Equal([$"Join({Player},3)"], ((Recorder)(object)teams).Calls);
    }

    [Theory]
    [InlineData((int)TFTeam.Red)]
    [InlineData((int)TFTeam.Blue)]
    public void ABotOnAPlayingTeamWithNoClassAskedForPicksSoldier(int team)
        => Assert.Equal("joinclass soldier", TFBotClassPick.Word(team, TFClass.Undefined));

    [Fact]
    public void ABotThatHasAskedForAClassAsksNothing()
        => Assert.Null(TFBotClassPick.Word((int)TFTeam.Red, TFClass.Soldier));

    [Theory]
    [InlineData((int)TFTeam.Unassigned)]
    [InlineData((int)TFTeam.Spectator)]
    public void ABotOnNoPlayingTeamAsksNothing(int team)
        => Assert.Null(TFBotClassPick.Word(team, TFClass.Undefined));
}
