using System.Reflection;
using SourceSharp.Ai.Server;
using Xunit;

namespace SourceSharp.Ai.Tests.Boundary;

/// <summary>
/// The generic server binding names no game (plan_bots_teams_tf2 C.2): a game's
/// binding (HL2MP's, later TF2's) builds on it and answers <see cref="IBotGame"/>,
/// so SourceSharp.Ai.Server neither references a game's binding nor carries
/// HL2MP's name in a type, field, constant or string. tools/ai-mutations adds an
/// HL2MP string to it and sees the name fact go red.
/// </summary>
public class ServerBindingTests
{
    private static readonly Assembly Server = typeof(AiBots).Assembly;

    private static string[] References() => Server.GetReferencedAssemblies().Select(a => a.Name ?? "").ToArray();

    [Fact]
    public void TheServerBindingReferencesTheCore()
        // The positive arm: the read sees the binding's references.
        => Assert.Contains("SourceSharp.Ai", References());

    [Fact]
    public void TheServerBindingReferencesNoGamesBinding()
        => Assert.DoesNotContain(References(), n => n.StartsWith("SourceSharp.Ai.", StringComparison.Ordinal) || n.StartsWith("TF2.", StringComparison.Ordinal));

    [Fact]
    public void TheServerBindingNamesNoHl2mp()
        => Assert.Empty(CoreReferenceTests.Naming(Server, "HL2MP"));

    /// <summary>A game's own entities are its binding's to read (plan_bots_teams_tf2 P6): the map reader names no team's spawn.</summary>
    [Fact]
    public void TheServerBindingNamesNoGamesTeamSpawn()
    {
        string[] hits = [.. CoreReferenceTests.Naming(Server, "info_player_combine"), .. CoreReferenceTests.Naming(Server, "info_player_rebel"),
                         .. CoreReferenceTests.Naming(Server, "info_player_teamspawn")];
        Assert.Empty(hits);
    }

    [Fact]
    public void TheHl2mpBindingBuildsOnTheServerBinding()
        => Assert.Contains(Server.GetName().Name, typeof(SourceSharp.Ai.Hl2mp.Hl2mpBotGame).Assembly.GetReferencedAssemblies().Select(a => a.Name));
}
