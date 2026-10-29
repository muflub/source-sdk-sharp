using SourceSharp.Abi;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The wall chargers' client half: the networked sequence started once and
/// the networked cycle held, as stock's C_BaseAnimating draws a charger whose
/// m_flCycle the server sets and nothing advances. Found beside stock
/// (2026-10-27): the client drew the charger's first frame, full, however
/// much it had given.
/// </summary>
public class ClientWallChargerTests
{
    [Fact]
    public void An_update_starts_the_networked_sequence_once_and_holds_each_cycle()
    {
        var engine = new RecordingEngine();
        var charger = new ClientHealthCharger(engine);
        charger.State.BindLocal();

        charger.State.Sequence = 2;
        charger.State.Cycle = 0.25f;
        charger.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        charger.State.Cycle = 0.5f;
        charger.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        var played = Assert.Single(engine.AllOf("EntityRender.PlaySequence"));
        Assert.Equal(2, played.ArgAs<int>(1));
        Assert.Equal([0.25f, 0.5f], engine.AllOf("EntityRender.SetCycle").Select(c => c.ArgAs<float>(1)));
    }

    [Fact]
    public void A_new_sequence_is_started_again()
    {
        var engine = new RecordingEngine();
        var charger = new ClientSuitCharger(engine);
        charger.State.BindLocal();

        charger.State.Sequence = 0;
        charger.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        charger.State.Sequence = 1;
        charger.State.Cycle = 1f;
        charger.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal([0, 1], engine.AllOf("EntityRender.PlaySequence").Select(c => c.ArgAs<int>(1)));
    }
}
