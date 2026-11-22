using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.MaterialProxies;
using SourceSharp.Client.Standalone;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The eyes proxyplayer.cpp's proxies measure through: the local player's
/// standalone out of the directory, the view the prediction published, the
/// team the player resource rows by entity index (what stock's
/// <c>GetTeamNumber</c> was — the row read that makes the ctf_2fort banner
/// flip), and the bound player's decoded speed.
/// </summary>
public class PlayerProxyEyesTests
{
    /// <summary>A directory holding entities by bound index; OfClass comes from the interface's default over Live().</summary>
    private sealed class Directory : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = [];
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;

        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
    }

    private static (PlayerProxyEyes Eyes, Directory St, ViewReadout View) Eyes(int local = 1)
        => Eyes(Fake(), local);

    private static (PlayerProxyEyes Eyes, Directory St, ViewReadout View) Eyes(RecordingEngine engine, int local = 1)
    {
        engine.Returns("Hud.LocalPlayerIndex", local);
        var st = new Directory();
        var view = new ViewReadout();
        return (new PlayerProxyEyes(st, engine, view, engine), st, view);
    }

    private static ClientSsPlayer Player(int index, Vector velocity = default)
    {
        var player = new ClientSsPlayer(Fake()) { Handle = new EntityHandle(0x0001_0000u | (uint)index) };
        player.BindIndex(index);
        player.State.BindLocal();
        player.State.Velocity = velocity;
        return player;
    }

    private static ClientPlayerResource Resource(params (int Index, byte Team)[] teams)
    {
        var core = new ClientPlayerResource(Fake());
        core.State.BindLocal();
        foreach ((int index, byte team) in teams)
            core.State.Team[index] = team;
        return core;
    }

    [Fact]
    public void TheEyesAnswerTheLocalPlayersStandaloneAndNobodyWithoutALocalIndex()
    {
        var (eyes, st, _) = Eyes(local: 1);
        var player = Player(1);
        st.Entities.Add(player);
        Assert.Equal(player, eyes.LocalPlayer());

        var (stranger, _, _) = Eyes(local: 0);
        Assert.Null(stranger.LocalPlayer());
    }

    [Fact]
    public void TheEyesAnswerTheAnglesThePredictionPublished()
    {
        var (eyes, st, view) = Eyes();
        st.Entities.Add(Player(1));
        view.PublishAngles(30f, 120f);

        Assert.Equal(new Vector(30f, 120f, 0f), eyes.LocalAngles()!.Value);
    }

    [Fact]
    public void TheEyesAnswerNoAnglesBeforeThereIsALocalPlayer()
    {
        var (eyes, _, view) = Eyes(local: 0);
        view.PublishAngles(30f, 120f);

        Assert.Null(eyes.LocalAngles());
    }

    [Fact]
    public void TheEyesAnswerTheTeamRowTheResourceHoldsForAPlayer()
    {
        var (eyes, st, _) = Eyes();
        st.Entities.Add(Player(5));
        st.Entities.Add(Resource((5, 2)));

        // Stock's GetTeamNumber for a player: the player resource's row.
        Assert.Equal(2, eyes.TeamNumber(5));
    }

    [Fact]
    public void TheEyesAnswerUnassignedForAnEntityThatIsNotAPlayer()
    {
        var (eyes, st, _) = Eyes();
        st.Entities.Add(Player(5));
        st.Entities.Add(Resource((5, 2)));

        Assert.Equal(0, eyes.TeamNumber(7));                       // nothing at that index
        Assert.Equal(0, eyes.TeamNumber(4));                       // a world entity's index
    }

    [Fact]
    public void TheEyesAnswerNoTeamForAPlayerWhileNoResourceHasArrived()
    {
        var (eyes, st, _) = Eyes();
        st.Entities.Add(Player(5));

        Assert.Equal(0, eyes.TeamNumber(5));
    }

    [Fact]
    public void TheEyesAnswerTheBoundPlayersSpeedAndNothingForAnybodyElse()
    {
        var (eyes, st, _) = Eyes();
        st.Entities.Add(Player(2, velocity: new Vector(30, 40, 0)));

        Assert.Equal(50f, eyes.EntitySpeed(2));
        Assert.Null(eyes.EntitySpeed(9));
    }

    /// <summary>
    /// WorldSpaceCenter's offset is the model box's centre turned with the
    /// entity: a box centred 50 forward and 10 up, yawed 90, sits 50 along +Y.
    /// </summary>
    [Fact]
    public void TheCentreOffsetIsTheModelBoxCentreTurnedWithTheEntity()
    {
        var engine = Fake();
        engine.Returns("WorldQuery.ClientEntityModelBounds", true);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", new Vector(0f, -10f, 0f));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", new Vector(100f, 10f, 20f));
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", new Vector(0f, 90f, 0f));
        var (eyes, _, _) = Eyes(engine);

        Vector offset = eyes.CentreOffset(7)!.Value;

        Assert.Equal(0f, offset.X, 3);
        Assert.Equal(50f, offset.Y, 3);
        Assert.Equal(10f, offset.Z, 3);
    }

    /// <summary>No model answers: no offset, and the proximity measures from the origin.</summary>
    [Fact]
    public void TheCentreOffsetIsNullWhenNoModelAnswers()
    {
        var engine = Fake();
        engine.Returns("WorldQuery.ClientEntityModelBounds", false);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Mins", Vector.Zero);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Maxs", Vector.Zero);
        engine.Returns("WorldQuery.ClientEntityModelBounds.Angles", Vector.Zero);
        var (eyes, _, _) = Eyes(engine);

        Assert.Null(eyes.CentreOffset(7));
    }
}
