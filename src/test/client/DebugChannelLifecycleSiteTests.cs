using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHICH LIFECYCLE HOOK THE DEBUG CHANNEL IS ARMED AND TORN DOWN IN, held as a
/// fact about the source.
///
/// <para>
/// <b>THE BUG THIS EXISTS FOR.</b> The channel was armed from
/// <c>ClientModule.LevelInit</c>, which reads as the obvious place and is the
/// wrong one. The SERVER's <c>OnClientActive</c> — which is what sends the
/// invitation — fires BEFORE the client's <c>LevelInit</c>, and a live run
/// printed the whole thing in order:
/// </para>
/// <code>
///     [SourceSharp/Server] client 1 active
///     [SourceSharp/Server] offered client 1 the debug channel on port 27120
///     [SourceSharp/Client] debug channel open to ws://127.0.0.1:27120/...
///     [SourceSharp/DebugChannel] a client attached, protocol 1
///     [SourceSharp/Client] level init: dm_lockdown            &lt;-- the re-arm
///     [SourceSharp/DebugChannel] session 1 ended
/// </code>
/// <para>
/// Six milliseconds of channel per map, and the server's mirror received
/// nothing. Moving the re-arm into <c>LevelShutdown</c> — which runs before the
/// next level's invitation — took the same run to 150 records received and a
/// live session on the second map.
/// </para>
///
/// <para>
/// <b>WHY THIS IS A SOURCE TEST AND NOT A BEHAVIOUR ONE.</b> The constraint is
/// an ordering between two lifecycle hooks the ENGINE calls, on two different
/// modules, in a running game. There is nothing to instantiate that would
/// reproduce it — the ordering is the engine's, not this code's — so what can be
/// pinned is which hook holds the call. The tree does the same thing wherever
/// the fact lives in source rather than in an object: <c>AbiParityTests</c> reads
/// the ABI header, <c>SideAuditTests</c> reads the gamesystem's guards.
/// </para>
/// </summary>
public class DebugChannelLifecycleSiteTests
{
    /// <summary>
    /// The body of one method, by brace matching from its SIGNATURE.
    ///
    /// <para>
    /// From the signature and not from a name, because a name is a prefix of
    /// other names and of its own call sites — this tree has been caught by
    /// exactly that three times this week. <c>LevelInit</c> alone would match
    /// the <c>LevelInit</c> inside a comment, the call
    /// <c>_tempEntPlayers?.LevelInit()</c>, and <c>LevelInitPostEntity</c>.
    /// </para>
    /// </summary>
    private static string BodyOf(string source, string signature)
    {
        int at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"the source declares {signature}");

        int open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"{signature} has a body");

        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source[open..(i + 1)];
        }

        Assert.Fail($"{signature}'s body is not closed");
        return "";
    }

    private static string ClientModule()
    {
        return File.ReadAllText(
            RepoLayout.Sdk("src", "sourcesharp", "managed", "SourceSharp.Client", "ClientModule.cs"));
    }

    [Fact]
    public void TheChannelIsTornDownAtLevelShutdown()
    {
        Assert.True(BodyOf(ClientModule(), "public void LevelShutdown()")
                        .Contains("TeardownDebugChannel();", StringComparison.Ordinal),
            "a socket, a task and a handler registration that survive a level change are the " +
            "family of defect that left this tree with a world outliving its level");
    }

    /// <summary>
    /// AND RE-ARMED THERE TOO, in the same hook, because the next level's
    /// invitation arrives before that level's client LevelInit.
    /// </summary>
    [Fact]
    public void TheChannelIsRearmedAtLevelShutdownAndNotLater()
    {
        Assert.True(BodyOf(ClientModule(), "public void LevelShutdown()")
                        .Contains("WireDebugChannel();", StringComparison.Ordinal),
            "the server invites at OnClientActive, which is before the client's LevelInit — so " +
            "a client armed only at LevelInit is armed one map too late, every map");
    }

    /// <summary>
    /// AND NOT FROM LevelInit, which is the edit this whole class exists to
    /// refuse. It looks like the right hook and it destroys the channel the
    /// current level has already established.
    /// </summary>
    [Fact]
    public void TheChannelIsNotRearmedFromLevelInit()
    {
        Assert.True(!BodyOf(ClientModule(), "public void LevelInit(string mapName)")
                        .Contains("WireDebugChannel();", StringComparison.Ordinal),
            "the invitation has already arrived and been accepted by the time this hook runs; " +
            "re-arming here tears down a live session six milliseconds after it attached");
    }

    [Fact]
    public void TheChannelIsNotTornDownFromLevelInitEither()
    {
        Assert.True(!BodyOf(ClientModule(), "public void LevelInit(string mapName)")
                        .Contains("TeardownDebugChannel();", StringComparison.Ordinal),
            "the same edit with the same effect, spelled the other way");
    }

    /// <summary>
    /// The module shutdown tears it down as well, because a managed reload
    /// unloads the module without a level ever ending — and a task on the stack
    /// is what stops a collectible load context from unloading at all.
    /// </summary>
    [Fact]
    public void TheChannelIsTornDownAtModuleShutdownToo()
    {
        Assert.True(BodyOf(ClientModule(), "public void Shutdown()")
                        .Contains("TeardownDebugChannel();", StringComparison.Ordinal),
            "cs_sv_reload replaces the module with no level change, and a dial loop left " +
            "running would pin the build being replaced");
    }
}
