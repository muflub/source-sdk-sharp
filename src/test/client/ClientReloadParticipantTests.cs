using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client reload's service tier (plan_client_novalve §3.8): every
/// <see cref="IReloadParticipant"/> in a container saved into the side's reload
/// store and restored into a fresh container's, as a reload does.
/// </summary>
public class ClientReloadParticipantTests
{
    private static (ServiceContainer Services, HudMessages Messages) Composition(RecordingEngine engine)
    {
        var messages = new HudMessages(new RecordingLog(), engine, engine);
        var services = new ServiceContainer().AddInstance<IHudMessages>(messages);
        return (services, messages);
    }

    private static HudMessageArgs Text(int channel, string text)
        => new(channel, 0.5f, 0.25f, new Color32(255, 128, 0), new Color32(0, 0, 0), 0, 0.1f, 0.5f, 30f, 0f, text);

    [Fact]
    public void The_hud_message_queue_survives_the_round_trip()
    {
        var engine = new RecordingEngine();
        var store = new ReloadStore(Side.Client);
        var (oldServices, oldMessages) = Composition(engine);
        oldMessages.Show(Text(1, "hold the line\nsecond"), now: 10f);
        oldMessages.Show(Text(4, "north door"), now: 11f);
        oldMessages.ShowTitle("HINT_DOOR", "press E", "", 0.5f, 0.6f, new Color32(1, 2, 3), 0.1f, 0.2f, 20f, now: 12f);

        var saved = new ClientReloadParticipants(oldServices, store, new RecordingLog()).Save();
        Assert.Equal(1, saved.Participants);
        Assert.Equal([ClientReloadParticipants.SectionOf(typeof(IHudMessages))], saved.Sections);

        // The new build's service, from its constructor: nothing showing.
        var (newServices, newMessages) = Composition(engine);
        Assert.Equal((0, 0), (newMessages.ActiveCount(12.5f), newMessages.ActiveTitleCount(12.5f)));

        var restored = new ClientReloadParticipants(newServices, store, new RecordingLog()).Restore();

        // Before != after: the same two channels and one title, still fading
        // on the engine's clock.
        Assert.Equal((2, 1), (newMessages.ActiveCount(12.5f), newMessages.ActiveTitleCount(12.5f)));
        Assert.Empty(restored.Failed);
        Assert.Empty(restored.Orphans);
        Assert.Empty(restored.Missing);
        Assert.Empty(store.Sections());
    }

    [Fact]
    public void A_carried_section_nobody_claims_is_reported()
    {
        var store = new ReloadStore(Side.Client);
        store.Put(ClientReloadParticipants.Prefix + "SourceSharp.Client.IRenamedAway", [1, 0], 1);
        var log = new RecordingLog();

        var restored = new ClientReloadParticipants(new ServiceContainer(), store, log).Restore();

        Assert.Equal([ClientReloadParticipants.Prefix + "SourceSharp.Client.IRenamedAway"], restored.Orphans);
        Assert.Contains(log.Lines, l => l.Warning && l.Text.Contains("IRenamedAway", StringComparison.Ordinal));
        Assert.Empty(store.Sections());
    }

    [Fact]
    public void A_section_that_will_not_read_back_fails_that_participant_only()
    {
        var engine = new RecordingEngine();
        var store = new ReloadStore(Side.Client);
        store.Put(ClientReloadParticipants.SectionOf(typeof(IHudMessages)), [1, 1, 0xFF], 1);
        var (services, messages) = Composition(engine);

        var restored = new ClientReloadParticipants(services, store, new RecordingLog()).Restore();

        Assert.Equal([ClientReloadParticipants.SectionOf(typeof(IHudMessages))], restored.Failed);
    }

    [Fact]
    public void A_reader_refuses_to_read_past_the_end()
    {
        var writer = new ReloadWriter();
        writer.Write("abc");
        writer.Write(7);
        var reader = new ReloadReader(writer.ToArray());
        Assert.Equal(("abc", 7), (reader.ReadString(), reader.ReadInt()));
        Assert.True(reader.AtEnd);
        Assert.Throws<ReloadFormatException>(() => reader.ReadInt());
    }
}
