using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>IServerGameClients</c>' managed half — plan_server.md Phase 7,
/// docs/plan-audit.md item C5.
///
/// <para>
/// <b>What these facts are for.</b> The whole content of the crossing is the
/// ORDER the two hierarchies are asked in, and here the order is not a matter
/// of taste: the SDK arm's miss path is the <c>Assert(0)</c> that Phase 10
/// recorded as FIRING once the managed weapon machine began emitting sounds on
/// standalone players. Asking the SDK hierarchy first would put that assert
/// back for every standalone player and every trigger pull.
/// </para>
///
/// <para>
/// <b>And that order is the REVERSE of
/// <see cref="ServerEntities.SkyboxAreaForClient"/>'s.</b> Two bodies written
/// the two ways round, both reproduced rather than reconciled — so a fact that
/// merely said "one hierarchy is asked first" would pass for either crossing
/// and would be the check that cannot fail. These name which.
/// </para>
/// </summary>
public class ServerClientsTests
{
    private const string Sdk = "Players.TrySdkPlayerEarPosition";
    private const string Standalone = "Players.TryStandalonePlayerEarPosition";

    private static (ServerClients Clients, RecordingEngine Engine) Build()
    {
        var engine = Fake();

        // BOTH `.ears` answers are stubbed on every fixture, including the ones
        // that expect a miss: RecordingEngine's out-parameter shape reads the
        // value before it reads the return, so a strict double would throw on a
        // miss it was never asked about. ServerEntitiesTests says the same
        // thing about the same shape.
        engine.Returns(Sdk + ".ears", default(Vector));
        engine.Returns(Standalone + ".ears", default(Vector));

        return (new ServerClients(engine), engine);
    }

    [Fact]
    public void TheStandaloneHierarchyIsAskedFirst()
    {
        var (clients, engine) = Build();
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".ears", new Vector(1f, 2f, 3f));
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".ears", new Vector(9f, 9f, 9f));

        Assert.True(clients.EarPositionForClient(1, out Vector ears));

        // ClientEarPosition's body reached for CStandalonePlayer::FromEdict
        // BEFORE UTIL_SDKPlayerFromEdict. That order is the observable part of
        // the code this crossing replaced.
        Assert.Equal(new Vector(1f, 2f, 3f), ears);
    }

    [Fact]
    public void TheOrderIsTheOppositeOfTheSkyboxCrossings()
    {
        // The one fact that would catch a lane "tidying" the two crossings into
        // agreement. ServerEntities asks the SDK hierarchy first; this asks the
        // standalone one first. Both are driven here, in one place, so that
        // making them agree cannot pass.
        var clients = Build();
        clients.Engine.Returns(Standalone, true);
        clients.Engine.Returns(Standalone + ".ears", new Vector(1f, 2f, 3f));
        clients.Engine.Returns(Sdk, true);
        clients.Clients.EarPositionForClient(1, out _);
        Assert.Equal(Standalone, clients.Engine.Calls[0].Member);

        var entities = Fake();
        entities.Returns("Visibility.TrySdkPlayerSkyboxArea.area", 7);
        entities.Returns("Visibility.TryStandalonePlayerSkyboxArea.area", 9);
        entities.Returns("Visibility.TrySdkPlayerSkyboxArea", true);
        entities.Returns("Visibility.TryStandalonePlayerSkyboxArea", true);
        new ServerEntities(entities).SkyboxAreaForClient(1, out _);
        Assert.Equal("Visibility.TrySdkPlayerSkyboxArea", entities.Calls[0].Member);
    }

    [Fact]
    public void TheSdkHierarchyIsNotEvenASKEDWhenTheStandaloneOneAnswered()
    {
        var (clients, engine) = Build();
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".ears", new Vector(1f, 2f, 3f));
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".ears", new Vector(9f, 9f, 9f));

        clients.EarPositionForClient(1, out _);

        // Stronger than checking the value: a body that asked both and then
        // picked would return the same Vector and would pay two crossings for
        // it. The short circuit is the design.
        Assert.DoesNotContain(engine.Calls, c => c.Member == Sdk);
    }

    [Fact]
    public void TheSdkHierarchyAnswersWhenTheStandaloneOneDoesNot()
    {
        var (clients, engine) = Build();
        engine.Returns(Standalone, false);
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".ears", new Vector(9f, 8f, 7f));

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(new Vector(9f, 8f, 7f), ears);
    }

    [Fact]
    public void TheWorldOriginIsAPositionAndNotASentinel()
    {
        // A standalone player standing at the map origin has ears at the map
        // origin. A body that used vec3_origin to mean "no client" would assert
        // on it — which is the assert this crossing exists to keep quiet.
        var (clients, engine) = Build();
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".ears", default(Vector));

        Assert.True(clients.EarPositionForClient(1, out Vector ears));
        Assert.Equal(default(Vector), ears);
    }

    [Fact]
    public void NeitherHierarchyMeansTheCallerAsserts()
    {
        var (clients, engine) = Build();
        engine.Returns(Standalone, false);
        engine.Returns(Sdk, false);

        // ClientEarPosition Assert(0)s and writes vec3_origin on this, which is
        // stock's own `else` arm. The assert is NOT managed code's: it is a
        // diagnostic about the ENGINE naming a client this game cannot
        // describe.
        Assert.False(clients.EarPositionForClient(1, out _));
    }

    [Fact]
    public void BothHierarchiesAreAskedAboutTheSameEdict()
    {
        var (clients, engine) = Build();
        engine.Returns(Standalone, false);
        engine.Returns(Sdk, false);

        clients.EarPositionForClient(42, out _);

        Assert.Contains(engine.Calls,
            c => c.Member == Standalone && c.Args.Length == 1 && Equals(c.Args[0], 42));
        Assert.Contains(engine.Calls,
            c => c.Member == Sdk && c.Args.Length == 1 && Equals(c.Args[0], 42));
    }
}
