/*
 * The viewmodel's presenter — Phase 10's render half, the policy side.
 *
 * The defect this guards against was invisible in every log: the viewmodel
 * existed, predicted byte-for-byte, and was not drawn, because nothing ever
 * put it in the renderer's viewmodel pass. What can regress here is all
 * decision-making: WHOSE viewmodel enters the pass, that everyone else's
 * stays undrawn, and that the sequence hand-off to native is edge-triggered —
 * pushed once per actual change, restarted on a parity bump and only then.
 */

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;
using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

public class ViewModelPresenterTests
{
    private const int LocalPlayer = 1;
    private const int SomebodyElse = 2;

    private static ClientSsViewModel ViewModel(RecordingEngine engine, int owner)
    {
        var viewModel = new ClientSsViewModel(engine, engine)
        {
            Handle = new EntityHandle(0x0100_0040u),
        };

        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = owner;
        viewModel.State.Sequence = 0;
        viewModel.State.AnimationParity = 0;
        viewModel.State.PlaybackRate = 1f;

        return viewModel;
    }

    private static void Decode(ClientSsViewModel viewModel)
        => viewModel.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

    //-------------------------------------------------------------------------
    // Whose eyes.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheLocalViewModelEntersTheViewmodelPass()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        var call = engine.Last("EntityRender.SetViewModel");

        Assert.True(call is not null, "the local player's viewmodel asked for the viewmodel pass");
        Assert.True(call!.Value.ArgAs<bool>(1), "and asked to ENTER it, not leave it");
        Assert.True(viewModel.IsLocal, "and knows it is the viewer's own");
    }

    [Fact]
    public void SomebodyElsesViewModelStaysUndrawn()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: SomebodyElse);
        viewModel.OnSpawn();
        Decode(viewModel);

        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 0,
            "another player's viewmodel never touches the viewmodel pass");
        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "and plays nothing — there is nothing of it to see");
        Assert.True(engine.CountOf("EntityRender.SetCustomDraw") == 1,
            "the draw-nothing placeholder from OnSpawn is all it registers");
    }

    [Fact]
    public void NoLocalPlayerMeansNoPass()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", 0);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 0,
            "with no local player there is nobody whose eyes to draw at");
    }

    [Fact]
    public void LosingTheOwnerLeavesThePassAndDrawsNothingAgain()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        viewModel.State.OwnerIndex = SomebodyElse;
        Decode(viewModel);

        var last = engine.Last("EntityRender.SetViewModel");

        Assert.True(last is not null && !last.Value.ArgAs<bool>(1),
            "a viewmodel whose owner stopped being the viewer leaves the pass");
        Assert.True(engine.CountOf("EntityRender.SetCustomDraw") == 1,
            "and goes back to drawing nothing rather than standing in the world");
    }

    [Fact]
    public void ADisownedViewModelLeavesThePass()
    {
        // Owner 0 is DEATH's signal: the server's Arsenal disowns the
        // viewmodel when the hands empty, because the entity transmits always
        // (the dormancy fix) and nothing else will take it off a dead
        // player's screen. This is the client half of that contract.
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        viewModel.State.OwnerIndex = 0;
        Decode(viewModel);

        var last = engine.Last("EntityRender.SetViewModel");

        Assert.True(last is not null && !last.Value.ArgAs<bool>(1),
            "a viewmodel that death disowned leaves the pass — the dead player's arms come down");
    }

    //-------------------------------------------------------------------------
    // What it plays. Present is called by BOTH writers — the decode and every
    // replayed command — so being edge-triggered is what makes that affordable
    // and what keeps a held prediction from restarting the animation.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARepeatedSequencePresentsOnce()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        viewModel.State.Sequence = 5;
        Decode(viewModel);

        viewModel.Present(5, 0, 1f);
        viewModel.Present(5, 0, 1f);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 1,
            "the same (sequence, parity) reaches the renderer exactly once");
    }

    [Fact]
    public void AParityBumpRestartsTheSameSequence()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        viewModel.State.Sequence = 5;
        Decode(viewModel);

        viewModel.Present(5, 1, 1f);

        var call = engine.Last("EntityRender.PlaySequence");

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 2,
            "a parity bump is a change and goes through");
        Assert.True(call!.Value.ArgAs<bool>(3),
            "and it RESTARTS the sequence — a second shot must fire the animation again");
    }

    [Fact]
    public void ANewSequenceStartsFreshRatherThanRestarting()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        viewModel.State.Sequence = 5;
        Decode(viewModel);

        viewModel.Present(7, 0, 1f);

        var call = engine.Last("EntityRender.PlaySequence");

        Assert.True(call!.Value.ArgAs<int>(1) == 7, "the new sequence is the one handed over");
        Assert.True(!call!.Value.ArgAs<bool>(3),
            "a NEW sequence needs no restart flag — it starts from its own beginning");
    }

    [Fact]
    public void APlaybackRateOfZeroIsHandedOverAsOne()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        viewModel.State.Sequence = 5;
        viewModel.State.PlaybackRate = 0f;
        Decode(viewModel);

        var call = engine.Last("EntityRender.PlaySequence");

        Assert.True(call!.Value.ArgAs<float>(2) == 1f,
            "a zero playback rate would freeze the arms; it plays at stock speed instead");
    }

    //-------------------------------------------------------------------------
    // The parity ring, and the stale packet that used to restart the animation.
    //-------------------------------------------------------------------------

    [Fact]
    public void AParityThatWentBackwardsDoesNotRestartTheAnimation()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        // The prediction replay gets ahead: two shots presented.
        viewModel.Present(4, 1, 1f);
        viewModel.Present(4, 2, 1f);

        int before = engine.CountOf("EntityRender.PlaySequence");

        // The server's decode for the FIRST of those two arrives late. It is
        // not a third shot; it is a packet the replay overtook.
        viewModel.Present(4, 1, 1f);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == before,
                    "a parity behind the one on screen is a stale packet, not a restart");
        Assert.True(viewModel.ShownParity == 2,
                    "and the presenter still stands where the replay left it");
    }

    [Fact]
    public void AParityThatWrappedPastSevenIsStillTheNextShot()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        viewModel.Present(4, 7, 1f);
        int before = engine.CountOf("EntityRender.PlaySequence");

        // Three bits: 7 + 1 is 0. Compared with a plain less-than this reads as
        // seven shots backwards, and the muzzle stops flashing the moment a
        // burst crosses the wrap.
        viewModel.Present(4, 0, 1f);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == before + 1,
                    "parity 0 arriving after parity 7 is the next shot and restarts the animation");
    }

    [Fact]
    public void ADifferentSequenceIsPresentedWhateverItsParitySays()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var viewModel = ViewModel(engine, owner: LocalPlayer);
        Decode(viewModel);

        viewModel.Present(4, 5, 1f);
        int before = engine.CountOf("EntityRender.PlaySequence");

        // A reload arriving with a parity behind the fire's. Refused on the
        // parity comparison, the wrong pose would stay on screen.
        viewModel.Present(9, 2, 1f);

        Assert.True(engine.CountOf("EntityRender.PlaySequence") == before + 1,
                    "a different sequence is a different animation whatever its parity says");
    }
    //-------------------------------------------------------------------------
    // The skin a weapon puts on its viewmodel (CWeaponCrossbow::SetSkin).
    //-------------------------------------------------------------------------

    [Fact]
    public void TheWeaponsSkinReachesTheDrawnViewModelOnce()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        var viewModel = ViewModel(engine, owner: LocalPlayer);

        viewModel.State.Skin = 1;
        Decode(viewModel);
        viewModel.ShowSkin(1);

        var skin = Assert.Single(engine.AllOf("EntityRender.SetSkin"));
        Assert.Equal(1, skin.ArgAs<int>(1));
    }

    [Fact]
    public void SomebodyElsesViewModelTakesNoSkin()
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);
        var viewModel = ViewModel(engine, owner: SomebodyElse);

        viewModel.State.Skin = 1;
        Decode(viewModel);

        Assert.False(engine.Called("EntityRender.SetSkin"), "a viewmodel nobody here looks through was skinned");
    }

    //-------------------------------------------------------------------------
    // The viewer's OWN state (the bug that was live on tf2: the spy's hand kept
    // drawing through the respawn wait, the heavy's arm through the death cam).
    // Stock re-asks the first-person question every frame inside ShouldDraw —
    // see ClientSsViewModel.ViewerMayDrawViewModel for the citations — so a
    // kill drops the arms without anything ever telling the viewmodel. Here the
    // decode rhythm needs two things: the pass decision reads the viewer's
    // (alive, observer mode), and ClientSsPlayer's edge notification re-runs
    // that decision when the pair moves.
    //-------------------------------------------------------------------------

    /// <summary>A directory holding the viewer and his viewmodel: GetByIndex answers by bound index.</summary>
    private sealed class ViewerDirectory : IClientStandalones
    {
        public readonly List<ClientStandaloneEntity> Entities = [];
        public IReadOnlyList<ClientStandaloneEntity> Live() => Entities;
        public ClientStandaloneEntity? GetByIndex(int entityIndex)
            => Entities.FirstOrDefault(e => e.Index == entityIndex);
    }

    private static ClientSsPlayer Viewer(RecordingEngine engine, ViewerDirectory directory,
                                         bool alive, ObserverMode mode)
    {
        var player = new ClientSsPlayer(engine, screen: engine, entities: directory)
        {
            Handle = new EntityHandle(0x0001_0000u | LocalPlayer),
        };
        player.BindIndex(LocalPlayer);
        player.State.BindLocal();
        player.State.Alive = alive;
        player.State.ObserverMode = (int)mode;
        directory.Entities.Add(player);
        return player;
    }

    private static (ClientSsViewModel ViewModel, ClientSsPlayer Viewer, RecordingEngine Engine)
        ViewerRig(bool alive, ObserverMode mode = ObserverMode.None)
    {
        var engine = Fake();
        engine.Returns("Hud.LocalPlayerIndex", LocalPlayer);

        var directory = new ViewerDirectory();
        var viewer = Viewer(engine, directory, alive, mode);

        var viewModel = new ClientSsViewModel(engine, engine, directory)
        {
            Handle = new EntityHandle(0x0100_0040u),
        };
        viewModel.State.BindLocal();
        viewModel.State.OwnerIndex = LocalPlayer;
        viewModel.State.Sequence = 5;
        viewModel.State.AnimationParity = 0;
        viewModel.State.PlaybackRate = 1f;

        directory.Entities.Add(viewModel);
        return (viewModel, viewer, engine);
    }

    private static void Kill(ClientSsPlayer viewer)
    {
        viewer.State.Alive = false;
        viewer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
    }

    private static void Respawn(ClientSsPlayer viewer)
    {
        viewer.State.Alive = true;
        viewer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
    }

    [Fact]
    public void TheDeadViewersArmsNeverEnterThePass()
    {
        // The respawn wait, client-side: the decode that makes the player dead
        // arrives, the viewmodel's own state has not moved — the old code still
        // handed the arms to the viewmodel pass off the owner match alone.
        var (viewModel, _, engine) = ViewerRig(alive: false);
        Decode(viewModel);

        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 0,
            "a dead viewer is looking through a death cam, not through the arms — no pass ask");
        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 0,
            "and the fire animation plays to nobody");
    }

    [Fact]
    public void TheViewersThirdPersonCameraTakesTheArmsDown()
    {
        // Alive but riding a chase camera (stock's IsThirdPerson arm — the
        // same one that hides the viewmodel behind the thirdperson key): the
        // eyes are outside the body, and the arms belong to the body.
        var (viewModel, _, engine) = ViewerRig(alive: true, mode: ObserverMode.Chase);
        Decode(viewModel);

        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 0,
            "the arms leave the pass when the viewer is not looking through them");
    }

    [Fact]
    public void TheAliveViewersArmsStillEnterThePassWithADirectory()
    {
        // The positive arm: the gate above is not the pass itself. Same rig,
        // alive and in eye mode — the arms go up exactly as before.
        var (viewModel, _, engine) = ViewerRig(alive: true);
        Decode(viewModel);

        var call = engine.Last("EntityRender.SetViewModel");
        Assert.True(call is not null && call.Value.ArgAs<bool>(1),
            "alive, first person: the directory changes nothing about the normal case");
        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 1, "and the sequence presents once");
    }

    [Fact]
    public void AKilledViewerTakesHisOwnArmsDownTheMomentTheDeathDecodes()
    {
        // The regression itself: the pass ask is edge-triggered, so nothing
        // re-runs the decision when the PLAYER moves — the player's own
        // decode has to carry it.
        var (viewModel, viewer, engine) = ViewerRig(alive: true);
        Decode(viewModel);

        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 1, "in the pass while alive");

        Kill(viewer);

        var last = engine.Last("EntityRender.SetViewModel");
        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 2,
            "the player's death decode re-ran the decision without a viewmodel update");
        Assert.True(last is not null && !last.Value.ArgAs<bool>(1),
            "and the answer was: out of the pass");

        // Edge-triggered: two more packets of the same death must not spam the
        // renderer with repeat asks.
        viewer.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        Assert.True(engine.CountOf("EntityRender.SetViewModel") == 2,
            "an unchanged (alive, mode) pair is not an edge and re-decides nothing");
    }

    [Fact]
    public void ARespawnedViewerTakesTheArmsBackUpWithTheFireAnimation()
    {
        var (viewModel, viewer, engine) = ViewerRig(alive: true);
        Decode(viewModel);

        Kill(viewer);
        Assert.True(!engine.Last("EntityRender.SetViewModel")!.Value.ArgAs<bool>(1), "the rig reached the death");

        Respawn(viewer);

        var last = engine.Last("EntityRender.SetViewModel");
        Assert.True(last is not null && last.Value.ArgAs<bool>(1),
            "the respawn decode brings the arms back into the pass — the bug's other half: a gate with no re-entry is a vanished viewmodel");
        Assert.True(engine.CountOf("EntityRender.PlaySequence") == 2,
            "and the pose replays — leaving the pass reset the shown pair, so the return presents fresh");
        Assert.True(last!.Value.ArgAs<EntityHandle>(0) == viewModel.Handle
                    && engine.Last("EntityRender.PlaySequence")!.Value.ArgAs<int>(1) == 5,
            "the asks name this viewmodel and its sequence");
    }
}

/// <summary>
/// The three-bit ring the parity counter lives on.
/// </summary>
public class AnimationParityTests
{
    [Fact]
    public void TheCounterWrapsAtTheWidthTheFieldIsNetworkedAt()
    {
        Assert.True(AnimationParity.Next(6) == 7, "six is followed by seven");
        Assert.True(AnimationParity.Next(7) == 0, "and seven by zero, because the field is three bits");
        Assert.True(AnimationParity.Modulus == 8, "eight values, which is what three bits hold");
    }

    [Fact]
    public void TheNearHalfOfTheRingIsAheadAndTheFarHalfIsBehind()
    {
        Assert.True(AnimationParity.IsAhead(2, 3), "one step on is the next shot");
        Assert.True(AnimationParity.IsAhead(2, 6), "four steps on is still ahead — a burst that got away");
        Assert.True(!AnimationParity.IsAhead(2, 7), "five steps on is five steps BACK, and is a stale packet");
        Assert.True(!AnimationParity.IsAhead(2, 1), "one step back is a stale packet");
    }

    [Fact]
    public void AParityIsNotAheadOfItself()
    {
        Assert.True(!AnimationParity.IsAhead(3, 3),
                    "the same value is a repeat; treated as ahead it would restart the animation every frame");
    }

    [Fact]
    public void AheadSurvivesTheWrap()
    {
        Assert.True(AnimationParity.IsAhead(7, 0), "zero after seven is the next shot");
        Assert.True(AnimationParity.IsAhead(6, 1), "and so is one after six");
        Assert.True(!AnimationParity.IsAhead(0, 7), "while seven after zero is the packet before it");
    }

}
