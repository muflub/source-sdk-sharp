using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The core's <c>changeteam</c> (TF2's client command, tf_viewport.cpp:127): under
/// team play it shows the team panel, the one the server's VGUIMenu "team" shows;
/// under deathmatch there is no team to choose and it shows nothing. Its key is a
/// row of HL2DM's keyboard page.
/// </summary>
public class ChangeTeamCommandTests
{
    private sealed class Standalones : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> All = new();

        public IReadOnlyList<ClientStandaloneEntity> Live() => All;

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => null;
    }

    private sealed class Panels : IViewportPanels
    {
        public readonly List<(string Name, bool Show)> Runs = new();

        public void Run(string name, bool show) => Runs.Add((name, show));
    }

    private static Standalones Roster(params int[] teams)
    {
        var resource = new ClientPlayerResource(new RecordingEngine());
        resource.State.BindLocal();
        for (int i = 0; i < teams.Length; i++)
        {
            resource.State.Flags[i + 1] = PlayerResourceNetState.FlagConnected;
            resource.State.Team[i + 1] = (byte)teams[i];
        }
        var standalones = new Standalones();
        standalones.All.Add(resource);
        return standalones;
    }

    [Fact]
    public void UnderTeamPlayItShowsTheTeamPanel()
    {
        var panels = new Panels();

        new ChangeTeamCommand(Roster(2, 3), panels, new RecordingLog()).Execute();

        Assert.Equal([("team", true)], panels.Runs);
    }

    [Fact]
    public void UnderDeathmatchItShowsNothingAndSaysWhy()
    {
        var panels = new Panels();
        var log = new RecordingLog();

        new ChangeTeamCommand(Roster(0, 0), panels, log).Execute();

        Assert.Empty(panels.Runs);
        Assert.Contains(log.Lines, l => l.Text.Contains("changeteam: not a team game", StringComparison.Ordinal));
    }

    [Fact]
    public void WithNoPlayerResourceItShowsNothing()
    {
        var panels = new Panels();

        new ChangeTeamCommand(new Standalones(), panels, new RecordingLog()).Execute();

        Assert.Empty(panels.Runs);
    }

    [Fact]
    public void TheCoreBindsItsKeyToChangeTeam()
    {
        var core = typeof(KeyActions).Assembly;
        var actions = KeyActions.Of(core.GetTypes(), core);

        Assert.Contains(("m", "changeteam"), actions.DefaultBinds);
        Assert.Contains(actions.Actions(), r => r.Command == "changeteam" && r.Label == "#Valve_Change_Team");
    }
}
