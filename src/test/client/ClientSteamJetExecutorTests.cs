using SourceSharp.Abi;
using SourceSharp.Client.Effects;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The env_steam client entity as the native draw executor sees it (lane
/// c2-sims): it builds its executor once and re-configures it after, hands
/// back the ids of a frame's spawns in spawn order, reports which ids are
/// alive, answers every puff's quad with ids descending (the executor's binary
/// search relies on it), and carries its puffs across a reload.
/// </summary>
public class ClientSteamJetExecutorTests
{
    private static readonly EntityHandle Owner = new(77u);

    private sealed class Grey : IClientWorldLight
    {
        public Vector LightForPoint(Vector position, bool clamp) => new(0.5f, 0.5f, 0.5f);
    }

    private static ClientSteamJet Jet(RecordingParticleExecutor executor)
    {
        var engine = new RecordingEngine();
        var jet = new ClientSteamJet(engine, executor, new Grey(), new RandomSeeds(7));
        jet.BindEngine(engine, engine);
        jet.State.BindLocal();
        jet.Handle = Owner;
        jet.State.Emit = true;
        jet.State.Speed = 120;
        jet.State.SpreadSpeed = 15;
        jet.State.StartSize = 10;
        jet.State.EndSize = 25;
        jet.State.Rate = 26;
        jet.State.JetLength = 80;
        jet.State.Red = 255;
        jet.State.Green = 255;
        jet.State.Blue = 255;
        return jet;
    }

    private static int Emit(IManagedParticleSource source, float dt, Span<int> ids, out ParticleEmit emit)
    {
        emit = new ParticleEmit { Dt = dt, WasDrawnPrevFrame = 1, Origin = new Vector(1, 2, 3), Angles = new Vector(0, 90, 0) };
        return source.Emit(0, ref emit, ids);
    }

    [Fact]
    public void TheExecutorIsBuiltOnceAndReconfiguredAfter()
    {
        var executor = new RecordingParticleExecutor();
        var jet = Jet(executor);

        jet.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        jet.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        jet.OnRemoved();

        Assert.Equal(["create 77 0 particle/particle_smokegrenade 25 False", "configure 77 0 25", "destroy 77 0"], executor.Calls);
    }

    [Fact]
    public void SpawnsComeBackInSpawnOrderAndQuadsWithIdsDescending()
    {
        var jet = Jet(new RecordingParticleExecutor());
        jet.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = jet;

        Span<int> ids = stackalloc int[16];
        int first = Emit(source, 0.0f, ids, out _);     // the timer fires on its first frame
        int added = Emit(source, 0.2f, ids, out var emit);

        Assert.True(first == 1 && added == 5, $"one puff on the first frame, then five in 0.2 s at rate 26 ({first}, {added})");
        Assert.True(emit.HasBounds == 1 && emit.Maxs.Y > emit.Mins.Y, "the bounds were written for SetBBox");
        for (int k = 1; k < added; k++)
            Assert.True(ids[k] > ids[k - 1], $"spawn order is id order ({ids[k - 1]} then {ids[k]})");

        var quads = new ParticleQuadRaw[16];
        int n = source.Draw(0, quads);
        Assert.True(n == 6, $"every puff is drawn ({n})");
        for (int k = 1; k < n; k++)
            Assert.True(quads[k].Id < quads[k - 1].Id, "quads come back with ids descending");

        Assert.True(source.Draw(0, new ParticleQuadRaw[2]) == 6, "a short buffer is told the count and written nothing past it");
    }

    [Fact]
    public void SimulateReportsWhichIdsDied()
    {
        var jet = Jet(new RecordingParticleExecutor());
        jet.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        IManagedParticleSource source = jet;

        Span<int> ids = stackalloc int[16];
        int added = Emit(source, 0.0f, ids, out _);
        int[] all = ids[..added].ToArray();
        Span<byte> alive = stackalloc byte[all.Length];

        Assert.True(source.Simulate(0, 0.1f, true, all, alive) == 1 && alive[0] == 1, "a young puff lives");
        Assert.True(source.Simulate(0, 1.0f, true, all, alive) == 1 && alive[0] == 0,
            "a puff past its lifetime (80 / 120 s) is reported dead");
        Assert.True(source.Simulate(0, 0.1f, false, all, alive) == 0, "stock's early return: emitting and not drawn last frame");
    }

    [Fact]
    public void AReloadCarriesThePuffsTheExecutorStillHolds()
    {
        var before = Jet(new RecordingParticleExecutor());
        before.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        Span<int> ids = stackalloc int[16];
        Emit(before, 0.0f, ids, out _);
        Emit(before, 0.2f, ids, out _);

        var record = new SavedEntity(Owner.Value, before.ClassName, string.Empty);
        before.SaveState(record);

        var executor = new RecordingParticleExecutor();
        var after = Jet(executor);
        var empty = new ParticleQuadRaw[16];
        Assert.True(((IManagedParticleSource)after).Draw(0, empty) == 0, "a rebuilt entity holds no puffs");

        after.LoadState(record);
        var quads = new ParticleQuadRaw[16];
        int n = ((IManagedParticleSource)after).Draw(0, quads);
        Assert.True(n == 6 && quads[0].Id == before.Sim.Puffs[0].Id, $"the restore carried the puffs and their ids ({n})");

        after.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);
        Assert.True(executor.Calls.SequenceEqual(["configure 77 0 25"]),
            "and knows its executor exists: it re-configures rather than building a second ("
            + string.Join("; ", executor.Calls) + ")");
    }
}
