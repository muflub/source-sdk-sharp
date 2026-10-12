using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>IServerGameEnts</c>' managed half — plan_server.md Phase 8b,
/// docs/plan-audit.md item C4.
///
/// <para>
/// <b>What these facts are for, and what they cannot be for.</b>
/// <c>CServerGameEnts::CheckTransmit</c> is the one method in that plan whose
/// regression is <i>silent and partial</i> — entities that stop being sent to
/// one client in one area — so the real gate for it is <c>tools/gate5</c>'s
/// recorded transmit set, live, against a baseline taken before the crossing.
/// These facts hold the part a live gate cannot see cheaply: the ORDER the two
/// hierarchies are asked in, and the contract of the out parameter.
/// </para>
///
/// <para>
/// <b>And they exist because the live gate was measured and found short.</b>
/// The crossing was landed and then mutated — the managed skybox answer
/// perturbed by +1 — and <c>tools/gate5</c> stayed GREEN: on <c>dm_lockdown</c>
/// at the recorded vantage no entity's area equals either value, so the
/// transmit set is byte-identical. The gate now records the area beside the
/// set, and these facts pin the answer at the source.
/// </para>
/// </summary>
public class ServerEntitiesTests
{
    private const string Sdk = "Visibility.TrySdkPlayerSkyboxArea";
    private const string Standalone = "Visibility.TryStandalonePlayerSkyboxArea";

    private static (ServerEntities Entities, RecordingEngine Engine) Build()
    {
        var engine = Fake();

        // BOTH `.area` answers are stubbed on every fixture, including the ones
        // that expect a miss. RecordingEngine's out-parameter shape reads the
        // value before it reads the return (PoseParameterRange does the same),
        // so a strict double throws on a miss it was never asked about.
        //
        // Which means these facts do NOT hold "area is untouched on a miss" --
        // that is a property of the ABI's two C++ steps and of Engine.cs, and it
        // is pinned there and by tools/gate5, not here. A managed fact asserting
        // it would be asserting something about the fixture.
        engine.Returns(Sdk + ".area", 0);
        engine.Returns(Standalone + ".area", 0);

        return (new ServerEntities(engine), engine);
    }

    [Fact]
    public void TheSdkHierarchyIsAskedFirst()
    {
        var (entities, engine) = Build();
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".area", 7);
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".area", 99);

        Assert.True(entities.SkyboxAreaForClient(1, out int area));

        // Stock only reached for CStandalonePlayer::FromEdict when
        // GetContainingEntity came back NULL, so an edict both hierarchies would
        // claim is answered by the SDK one. That order is the observable part of
        // the code this crossing replaced.
        Assert.Equal(7, area);
    }

    [Fact]
    public void TheStandaloneHierarchyIsNotEvenASKEDWhenTheSdkOneAnswered()
    {
        var (entities, engine) = Build();
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".area", 7);
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".area", 99);

        entities.SkyboxAreaForClient(1, out _);

        // Stronger than checking the value: a body that asked both and then
        // picked would return 7 as well, and would pay two crossings a frame per
        // client for it. The short circuit is the design.
        Assert.DoesNotContain(engine.Calls, c => c.Member == Standalone);
    }

    [Fact]
    public void TheStandaloneHierarchyAnswersWhenTheSdkOneDoesNot()
    {
        var (entities, engine) = Build();
        engine.Returns(Sdk, false);
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".area", 99);

        Assert.True(entities.SkyboxAreaForClient(1, out int area));
        Assert.Equal(99, area);
    }

    [Fact]
    public void ThisIsTheCaseThePortExistsFor()
    {
        // A standalone recipient resolves to no CBaseEntity. Before Phase 9a the
        // guard in C++ then transmitted NOTHING to that client — the whole world
        // invisible, an assert in debug and silence in release. Phase 9a fixed it
        // with an `#ifdef SOURCESHARP` block; C4 moved the block here.
        var (entities, engine) = Build();
        engine.Returns(Sdk, false);
        engine.Returns(Standalone, true);
        engine.Returns(Standalone + ".area", 0);

        Assert.True(entities.SkyboxAreaForClient(1, out int area));

        // AREA 0 AND STILL FOUND. The two are separate answers on purpose: area
        // 0 is a real visleaf area, and a body that used 0 to mean "no
        // recipient" would put this client back where Phase 9a found it.
        Assert.Equal(0, area);
    }

    [Fact]
    public void NeitherHierarchyMeansNoRecipient()
    {
        var (entities, engine) = Build();
        engine.Returns(Sdk, false);
        engine.Returns(Standalone, false);

        // CheckTransmit returns early on this, which is stock's
        // `if ( !pRecipientEntity && !bStandaloneRecipient ) return;`.
        Assert.False(entities.SkyboxAreaForClient(1, out _));
    }

    [Fact]
    public void BothHierarchiesAreAskedAboutTheSameEdict()
    {
        var (entities, engine) = Build();
        engine.Returns(Sdk, false);
        engine.Returns(Standalone, false);

        entities.SkyboxAreaForClient(42, out _);

        Assert.Contains(engine.Calls,
            c => c.Member == Sdk && c.Args.Length == 1 && Equals(c.Args[0], 42));
        Assert.Contains(engine.Calls,
            c => c.Member == Standalone && c.Args.Length == 1 && Equals(c.Args[0], 42));
    }

    [Fact]
    public void EdictZeroIsAskedAboutRatherThanTreatedAsNoRecipient()
    {
        // 0 is stock's FALLBACK for "the engine named no client"
        // (`pInfo->m_pClientEnt ? … : pBaseEdict`), not a sentinel. A body that
        // short-circuited on it would decline a recipient stock answers for.
        var (entities, engine) = Build();
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".area", 3);

        Assert.True(entities.SkyboxAreaForClient(0, out int area));
        Assert.Equal(3, area);
        Assert.Contains(engine.Calls, c => c.Member == Sdk);
    }

    [Fact]
    public void OneCrossingPerClientPerFrameAndNotTwo()
    {
        // The rate is the argument for keeping CheckTransmit's LOOP native: this
        // runs once per client per frame, the loop would run once per edict per
        // client per frame. A body that asked both hierarchies every time would
        // double the only part that did cross.
        var (entities, engine) = Build();
        engine.Returns(Sdk, true);
        engine.Returns(Sdk + ".area", 1);

        entities.SkyboxAreaForClient(1, out _);

        Assert.Single(engine.Calls);
    }

    // ---- stock CBasePlayer::ShouldTransmit's first arm ----

    [Fact]
    public void A_client_is_always_sent_its_own_player()
    {
        // Stock: `if ( pInfo->m_pClientEnt == edict() ) return FL_EDICT_ALWAYS;`
        // ahead of the EF_NODRAW / observer DONTSEND rule and of any PVS test,
        // so an observer watching in-eye from across the map (its own player
        // nodraw and out of its own PVS) still receives its observer fields.
        var (entities, _) = Build();
        Assert.Equal(2, entities.TransmitAlwaysForClient(2));
        Assert.Equal(1, entities.TransmitAlwaysForClient(1));
    }

    [Fact]
    public void The_no_client_fallback_names_no_player()
    {
        // Edict 0 is CheckTransmit's fallback when the engine names no client:
        // the world, which is no one's own player.
        var (entities, _) = Build();
        Assert.Equal(-1, entities.TransmitAlwaysForClient(0));
        Assert.Equal(-1, entities.TransmitAlwaysForClient(-1));
    }
}
