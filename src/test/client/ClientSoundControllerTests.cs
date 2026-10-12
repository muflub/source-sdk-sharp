using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's sound envelope controller as its shim reaches it (sound
/// cluster 17, lane c2-sims): each managed-built slot counts its crossing and
/// lands on the shared controller; a step emits the ramp to the patch through
/// the client's own patch slots.
/// </summary>
public class ClientSoundControllerTests
{
    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10f;
    }

    [Fact]
    public void TheBindingDrivesTheSharedControllerAndAStepEmits()
    {
        var engine = TestServices.Fake();
        engine.Returns("Sound.SoundPatchState.entIndex", 0);
        engine.Returns("Sound.SoundPatchState.filterActive", true);
        var clock = new Clock();
        var sounds = new ClientSoundController(engine, clock, engine, engine);
        var crossings = new Crossings();
        IManagedSoundControllerClient binding = new SoundControllerClientBinding(crossings, sounds);

        sounds.SystemUpdate();   // the controller's clock starts at the first frame
        binding.ClientSoundControllerCreate(3);
        binding.ClientSoundControllerPlay(3, 0f, 100f, 0f);
        binding.ClientSoundControllerChangeVolume(3, 1f, 1f);
        clock.Now += 0.25f;
        sounds.SystemUpdate();

        Assert.True(binding.ClientSoundControllerGetVolume(3) == 0.25f, $"a quarter second of a one-second ramp ({binding.ClientSoundControllerGetVolume(3)})");
        Assert.True(crossings.Read(CrossingOp.ClientSoundControllerPlay) == 1 && crossings.Read(CrossingOp.ClientSoundControllerGetVolume) == 2,
            "each slot counts its own crossing");
        var emits = engine.Calls.Where(c => c.Member == "Sound.SoundPatchEmit").ToList();
        Assert.True(emits.Count >= 2 && (int)emits[0].Args[0]! == 3, "Play started the patch and the step changed it, by handle");
    }
    [Fact]
    public unsafe void PlayEnvelopeReadsFourFloatsPerPointAcrossTheSlot()
    {
        var engine = TestServices.Fake();
        var draws = new Queue<float>([80f, 0.25f, 120f, 0.5f]);
        engine.ReturnsEachCall("Host.RandomFloat", () => draws.Dequeue());
        var sounds = new ClientSoundController(engine, new Clock(), engine, engine);
        IManagedSoundControllerClient binding = new SoundControllerClientBinding(new Crossings(), sounds);

        float[] points = [80f, 80f, 0.25f, 0.25f, 120f, 120f, 0.5f, 0.5f];
        float total;
        fixed (float* p = points)
            total = binding.ClientSoundControllerPlayEnvelope(3, SoundEnvelopeController.CommandChangePitch, p, 2);

        Assert.True(total == 0.75f, $"two points, 0.25 s and 0.5 s, make a 0.75 s envelope ({total})");
        Assert.True(draws.Count == 0, $"both points drew an amplitude and a duration ({draws.Count} draws left)");
    }
}
