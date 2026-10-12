using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>say</c>: stock's ConCommand in client.cpp, Host_Say for whoever typed it
/// (the server console has no speaker). Un-retired: the recipient list was
/// always empty only because Host_Say walked CBasePlayers (lane player-msgs).
/// </summary>
[ConCommand("say", Help = "Display player message")]
public sealed class SayCommand(HostSay say, IEnginePlayers players) : IConCommand
{
    public void Execute(string args) => say.Say(players.GetCommandClient(), args, teamOnly: false);
}

/// <summary><c>say_team</c>: Host_Say to teammates only.</summary>
[ConCommand("say_team", Help = "Display player message to team")]
public sealed class SayTeamCommand(HostSay say, IEnginePlayers players) : IConCommand
{
    public void Execute(string args) => say.Say(players.GetCommandClient(), args, teamOnly: true);
}
