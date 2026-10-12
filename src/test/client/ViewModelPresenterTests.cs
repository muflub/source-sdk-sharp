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
