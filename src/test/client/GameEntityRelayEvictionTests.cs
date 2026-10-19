using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// game_entity_relays.h's client half leaving: GameEntity_ModelIndex,
/// GameEntity_RefHandle and GameEntity_EntIndex had no caller on client.so once
/// fork physics.cpp and ragdoll.cpp were gone (nm over the kept client objects).
/// The server's half stays, and is still called there.
/// </summary>
public class GameEntityRelayEvictionTests
{
    private static string Code(params string[] path) =>
        Regex.Replace(File.ReadAllText(RepoLayout.Repo(path)), @"//[^\n]*", "");

    private static readonly string[] Relays = { "GameEntity_ModelIndex(", "GameEntity_RefHandle(", "GameEntity_EntIndex(" };

    [Fact]
    public void NoClientFileDefinesOrCallsARelay()
    {
        string dir = RepoLayout.Repo("src", "client", "native");
        int read = 0;
        foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                                         .Where(f => f.EndsWith(".cpp", System.StringComparison.Ordinal) || f.EndsWith(".h", System.StringComparison.Ordinal)))
        {
            string code = Code("src", "client", "native", Path.GetFileName(file));
            read++;
            foreach (string relay in Relays)
                Assert.False(code.Contains(relay, System.StringComparison.Ordinal), $"{Path.GetFileName(file)} spells {relay}");
            Assert.False(code.Contains("#include \"game_entity_relays.h\"", System.StringComparison.Ordinal),
                         $"{Path.GetFileName(file)} includes game_entity_relays.h");
        }
        Assert.True(read > 50, $"read {read} client native files");
    }

    [Fact]
    public void TheServerStillDefinesAndCallsThem()
    {
        string relays = Code("src", "server", "native", "standalone_relays.cpp");
        Assert.Contains("int GameEntity_ModelIndex( const IGameEntity *pEnt )", relays);
        Assert.Contains("CBaseHandle GameEntity_RefHandle( const IGameEntity *pEnt )", relays);
        Assert.Contains("GameEntity_RefHandle( pEntity )", Code("src", "server", "native", "shell_entityapi.cpp"));
    }
}
