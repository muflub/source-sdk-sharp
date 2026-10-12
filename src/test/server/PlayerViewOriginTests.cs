using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// WHERE A PLAYER'S EYES ARE, ANSWERED IN MANAGED CODE — plan_entity.md step 3c,
/// §3.10.
///
/// <para>
/// One value with two engine readers. <c>ClientSetupVisibility</c> adds it to
/// the PVS; <c>ClientEarPosition</c> puts the sound listener at it. In stock
/// they are <c>EyePosition()</c> and <c>EarPosition()</c>, and the second is
/// <c>return EyePosition();</c> — so a build where the two could differ has a
/// bug nobody would see, because both answers are well-formed positions in
/// roughly the right place.
/// </para>
///
/// <para>
/// <b>WHAT REGRESSES HERE.</b> The arithmetic (a view origin that forgot the
/// view offset puts the listener at the player's FEET, which sounds like nothing
/// at all until a wall is between the feet and the ears); and the routing — the
/// managed arm existing but never being reached would put the crossing round
/// trip back with every test still green, because the two native steps behind it
/// answer the same value.
/// </para>
/// </summary>
public class PlayerViewOriginTests
{
    private const string Sdk = "Players.TrySdkPlayerEarPosition";
    private const string Standalone = "Players.TryStandalonePlayerEarPosition";

    /// <summary>
    /// A directory that answers <c>OfClass(ss_player)</c> with exactly the
    /// players a test put in it, and nothing else with anything.
    /// </summary>
    private sealed class PlayersOnly : IEntityDirectory
    {
        public readonly List<ScriptedEntity> Players = new();

        public ScriptedEntity? Get(EntityHandle handle) => null;

        public ScriptedEntity? FindByName(string name) => null;

        public IEnumerable<ScriptedEntity> OfClass(string className)
            => className == EntityNames.SsPlayer ? Players : Array.Empty<ScriptedEntity>();

        public bool IsRegistered(string className) => className == EntityNames.SsPlayer;

        public IReadOnlyList<ScriptedClassInfo> DescribeClasses() => [];
    }

    /// <summary>
    /// A player bound at one edict index, with its two view words set.
    ///
    /// <para>
    /// The handle's ENTRY is the edict index and its serial is not, which is the
    /// whole of what the routing matches on — a fixture with serial 0 would let
    /// a body that compared whole handles pass.
    /// </para>
    /// </summary>
    private static SsPlayer PlayerAt(int edictIndex, Vector origin, Vector viewOffset)
    {
        var player = new SsPlayer(Fake(), Fake(), Impacts(Fake()), Fluid())
        {
            Handle = new EntityHandle((uint)((5 << 16) | edictIndex)),
        };

        player.State.BindLocal();
        player.State.Origin = origin;
        player.State.ViewOffset = viewOffset;

        return player;
    }

    private static RecordingEngine BothNativeStepsMiss()
    {
        var engine = Fake();

        // Stubbed on every fixture even where a miss is expected:
        // RecordingEngine reads the out-parameter before it reads the return,
        // so a strict double throws on a miss it was never asked about.
        // ServerClientsTests says the same thing about the same shape.
        engine.Returns(Sdk + ".ears", default(Vector));
        engine.Returns(Standalone + ".ears", default(Vector));
        engine.Returns(Standalone, false);
        engine.Returns(Sdk, false);

        return engine;
    }

    //-------------------------------------------------------------------------
    // SsPlayer.ViewOrigin — the arithmetic.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheViewOriginIsTheOriginPlusTheViewOffset()
    {
        SsPlayer player = PlayerAt(1, new Vector(10f, 20f, 30f), new Vector(0f, 0f, 64f));

        Assert.Equal(new Vector(10f, 20f, 94f), player.ViewOrigin);
    }

    [Fact]
    public void TheViewOriginIsNotTheOrigin()
    {
        // The mistake that would look right nearly everywhere: eyes at the feet
        // are 64 units low, which is inside the same room and usually the same
        // PVS cluster.
        SsPlayer player = PlayerAt(1, new Vector(10f, 20f, 30f), new Vector(0f, 0f, 64f));

        Assert.NotEqual(player.State.Origin, player.ViewOrigin);
    }

    [Fact]
    public void DuckingMovesTheViewOrigin()
    {
        // The view offset is what ducking changes (VEC_DUCK_VIEW against
        // VEC_VIEW), so a body that cached the standing offset would leave a
        // ducked player hearing and seeing from where their head used to be.
        SsPlayer player = PlayerAt(1, new Vector(0f, 0f, 0f), new Vector(0f, 0f, 64f));
        Vector standing = player.ViewOrigin;

        player.State.ViewOffset = new Vector(0f, 0f, 28f);

        Assert.NotEqual(standing, player.ViewOrigin);
        Assert.Equal(new Vector(0f, 0f, 28f), player.ViewOrigin);
    }

    [Fact]
    public void TheViewOriginMovesWithThePlayer()
    {
        SsPlayer player = PlayerAt(1, new Vector(0f, 0f, 0f), new Vector(0f, 0f, 64f));

        player.State.Origin = new Vector(100f, -50f, 8f);

        Assert.Equal(new Vector(100f, -50f, 72f), player.ViewOrigin);
    }

    //-------------------------------------------------------------------------
    // ServerClients.EarPositionForClient — the routing.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheManagedPlayerAnswersForItsOwnEdict()
    {
        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(1, new Vector(1f, 2f, 3f), new Vector(0f, 0f, 64f)));

        var clients = new ServerClients(BothNativeStepsMiss(), directory);

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(new Vector(1f, 2f, 67f), ears);
    }

    [Fact]
    public void NeitherNativeStepIsAskedWhenTheManagedPlayerAnswered()
    {
        // STRONGER THAN THE VALUE CHECK ABOVE, and it is the whole point of the
        // step: the two native steps answer the SAME position, and the
        // standalone one gets there by crossing back into native code. A body
        // that asked them anyway would return the right Vector and pay the round
        // trip this arm exists to remove.
        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(1, new Vector(1f, 2f, 3f), new Vector(0f, 0f, 64f)));

        var engine = BothNativeStepsMiss();
        new ServerClients(engine, directory).EarPositionForClient(1, out _);

        Assert.DoesNotContain(engine.Calls, c => c.Member == Standalone);
        Assert.DoesNotContain(engine.Calls, c => c.Member == Sdk);
    }

    [Fact]
    public void APlayerAtAnotherEdictDoesNotAnswer()
    {
        // The routing matches on the handle's ENTRY. A body that answered with
        // "the first ss_player in the directory" would be right with one player
        // and would give both clients the same ears with two.
        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(2, new Vector(1f, 2f, 3f), new Vector(0f, 0f, 64f)));

        var engine = BothNativeStepsMiss();

        Assert.False(new ServerClients(engine, directory).EarPositionForClient(1, out _));
        Assert.Contains(engine.Calls, c => c.Member == Standalone);
    }

    [Fact]
    public void TheRightPlayerAnswersWhenTwoAreConnected()
    {
        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(1, new Vector(1f, 0f, 0f), new Vector(0f, 0f, 64f)));
        directory.Players.Add(PlayerAt(2, new Vector(2f, 0f, 0f), new Vector(0f, 0f, 64f)));

        var clients = new ServerClients(BothNativeStepsMiss(), directory);

        Assert.True(clients.EarPositionForClient(2, out Vector ears));
        Assert.Equal(new Vector(2f, 0f, 64f), ears);
    }

    [Fact]
    public void TheNativeStepsStillAnswerWhenNoManagedPlayerIsBound()
    {
        // The HLTV and Replay masters, and every client between connect and
        // ClientPutInServer: clients with no managed player. The two native
        // steps are unchanged and still in their order.
        var engine = BothNativeStepsMiss();
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".ears", new Vector(7f, 7f, 7f));

        var clients = new ServerClients(engine, new PlayersOnly());

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(new Vector(7f, 7f, 7f), ears);
    }

    [Fact]
    public void NoDirectoryAtAllIsTheOldBehaviourExactly()
    {
        // The container registers IEntityDirectory before ServerClients, so the
        // null case is not the shipping one -- but the parameter is optional and
        // a build that stopped registering it must degrade to the two native
        // steps rather than answering nothing.
        var engine = BothNativeStepsMiss();
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".ears", new Vector(4f, 5f, 6f));

        var clients = new ServerClients(engine);

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(new Vector(4f, 5f, 6f), ears);
    }

    [Fact]
    public void TheContainerFillsTheDirectoryRatherThanTakingTheDefault()
    {
        // THE CLAIM THE FACT ABOVE MAKES IN PROSE, RUN. ServerModule does not
        // write `new ServerClients(...)` at all -- it asks the container, with
        // _services.Create<ServerClients>() -- and an optional parameter the
        // container cannot fill falls back to its declared null SILENTLY. Every
        // other fact in this file would stay green through that, because they
        // hand the directory in by hand and because the two native steps behind
        // the managed arm answer the same value in the game. This is the only
        // one that fails if the registration order moves or the registration
        // goes away.
        var engine = BothNativeStepsMiss();

        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(1, new Vector(1f, 2f, 3f), new Vector(0f, 0f, 64f)));

        var services = new ServiceContainer();
        services.AddInstance<IEnginePlayers>(engine);
        services.AddInstance<IEntityDirectory>(directory);

        ServerClients clients = services.Create<ServerClients>();

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(new Vector(1f, 2f, 67f), ears);
    }

    [Fact]
    public void TheModuleRegistersTheDirectoryBeforeItBuildsTheClients()
    {
        // THE OTHER HALF OF THE WIRING, AND THE FACT ABOVE CANNOT SEE IT. That
        // one proves the CONTAINER fills an optional parameter it can resolve;
        // this one proves the container CAN resolve it at the moment
        // ServerModule asks. Both registrations are one line each in one method
        // and their ORDER is the whole contract: AddInstance after Create is a
        // ServerClients built with a null directory, the managed arm dead, and
        // every behavioural fact in this file still green -- because the two
        // native steps behind it answer the same value in the game.
        string module = StripLineComments(File.ReadAllText(Path.Combine(
            RepoLayout.SdkTree,
            "src", "sourcesharp", "managed", "SourceSharp.Server", "ServerModule.cs")));

        int registered = module.IndexOf("AddInstance<IEntityDirectory>", StringComparison.Ordinal);
        int created = module.IndexOf("Create<ServerClients>", StringComparison.Ordinal);

        // NON-VACUITY BOTH WAYS. Two -1s compare equal-ish and would let a file
        // that had lost both lines pass a naive `registered < created`.
        Assert.True(registered >= 0, "ServerModule.cs no longer registers IEntityDirectory");
        Assert.True(created >= 0, "ServerModule.cs no longer builds ServerClients from the container");

        Assert.True(registered < created,
            $"ServerModule.cs registers IEntityDirectory at {registered} but builds "
            + $"ServerClients at {created}; the directory is not there to be injected yet");
    }

    /// <summary>
    /// <c>//</c> to end of line removed, so that the check above is about code.
    ///
    /// <para>
    /// The subject explains itself in prose and the prose names both symbols:
    /// <c>ServerClients.cs</c>'s own doc comment says the container registers
    /// <c>IEntityDirectory</c> before <c>ServerClients</c>. A search that
    /// matched a comment would go green across the deletion it exists to
    /// notice.
    /// </para>
    /// </summary>
    private static string StripLineComments(string text)
    {
        var stripped = new System.Text.StringBuilder(text.Length);

        foreach (string line in text.Split('\n'))
        {
            int at = line.IndexOf("//", StringComparison.Ordinal);
            stripped.Append(at < 0 ? line : line[..at]).Append('\n');
        }

        return stripped.ToString();
    }

    [Fact]
    public void EdictZeroIsNeverAPlayer()
    {
        // Index 0 is worldspawn's. ClientEarPosition passes 0 for a NULL edict
        // on purpose -- so that the caller's Assert(0) fires -- and a routing
        // that treated 0 as a slot would answer for whatever sat at entry 0.
        var directory = new PlayersOnly();
        directory.Players.Add(PlayerAt(0, new Vector(9f, 9f, 9f), Vector.Zero));

        var engine = BothNativeStepsMiss();

        Assert.False(new ServerClients(engine, directory).EarPositionForClient(0, out _));
    }
}
