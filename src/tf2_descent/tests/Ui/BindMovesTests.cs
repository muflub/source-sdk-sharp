extern alias client;

using Xunit;

using BindsConVar = client::TF2.Descent.Client.DescentBindsConVar;
using BindMoves = client::TF2.Descent.Client.DescentBindMoves;
using ICommandLine = client::TF2.Descent.Client.Ui.IDescentCommandLine;
using ClientKeyActions = client::SourceSharp.Client.Input.KeyActions;
using DescentScreens = client::TF2.Descent.Client.Ui.DescentScreens;

namespace TF2.Descent.Tests.Ui;

/// <summary>
/// The default binds a config predates: a key action's keys reach only a fresh config, so a
/// player whose config.cfg holds TF2's Tab (+showscores) and E (dropitem) gets Descent's once,
/// at the first level, and the config records it.
/// </summary>
public sealed class BindMovesTests
{
    private sealed class Lines : ICommandLine
    {
        public List<string> Sent { get; } = new();
        public void Send(string line) => Sent.Add(line);
    }

    [Fact]
    public void A_config_from_before_the_moves_gets_tab_and_e_and_records_the_version()
    {
        var lines = new Lines();
        new BindMoves(DeclaredClientConVars.Fresh<BindsConVar>(), lines).OnLevelInit("tf2_descent_town");

        Assert.Equal(["bind \"TAB\" \"tf2_descent_map_overlay\"", "bind \"e\" \"+use\"", $"tf2_descent_binds {BindMoves.Current}"],
                     lines.Sent);
    }

    [Fact]
    public void A_config_that_holds_the_moves_is_left_alone()
    {
        var lines = new Lines();
        var binds = DeclaredClientConVars.Holding<BindsConVar>(BindMoves.Current.ToString(System.Globalization.CultureInfo.InvariantCulture));
        new BindMoves(binds, lines).OnLevelInit("tf2_descent_town");

        Assert.Empty(lines.Sent);
    }

    /// <summary>The moves end each key on the bind a fresh config gets for it.</summary>
    [Fact]
    public void Each_move_is_the_bind_a_fresh_config_gets()
    {
        var lines = new Lines();
        new BindMoves(DeclaredClientConVars.Fresh<BindsConVar>(), lines).OnLevelInit("tf2_descent_town");
        var descent = typeof(DescentScreens).Assembly;
        var tf2 = typeof(client::TF2.Client.MoveForwardKey).Assembly;
        var actions = ClientKeyActions.Of(tf2.GetTypes().Concat(descent.GetTypes()), typeof(ClientKeyActions).Assembly);
        var fresh = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, command) in actions.DefaultBinds)
            fresh[key] = command;

        var binds = lines.Sent.Where(l => l.StartsWith("bind ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, binds.Count);
        foreach (string line in binds)
        {
            string[] parts = line.Split('"');
            Assert.Equal(fresh[parts[1]], parts[3]);
        }
    }
}
