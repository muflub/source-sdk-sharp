using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The soundscape family, at the level where the decisions are.
///
/// <para>
/// The selection loop itself needs traces and an engine, so what is checked
/// here is everything either side of them: the radius rule that decides which
/// soundscapes are candidates at all, the identity tokens the client reads to
/// notice a change, the proxy's borrowing, and the stack of overlapping trigger
/// volumes — which is the piece with real ordering in it and the piece a player
/// would notice going wrong, because leaving one room would drop them into
/// silence instead of back into the other.
/// </para>
/// </summary>
public class SoundscapeTests
{
    private static readonly EntityHandle Player = new(0x0059_00A1u);

    /// <summary>
    /// A registry and the soundscape system its entities are built against.
    ///
    /// A fresh instance per test rather than a static reset, which is what this
    /// used to be. The reset was there because the state was global; an
    /// instance handed to the entities through the container is the same
    /// isolation without the line that has to be remembered.
    /// </summary>
    private static (ScriptedEntityRegistry Registry, Soundscapes Soundscapes) NewRegistry()
        => NewRegistry(out _);

    /// <inheritdoc cref="NewRegistry()"/>
    /// <param name="net">
    /// The double the soundscape push is sent through — plan_entity.md step
    /// 8e-sound. Handed back rather than hidden because the push IS the
    /// behaviour now: the class used to call an engine slot with an empty
    /// native body, so a test could only read the decision it had made, and it
    /// sends a <c>Soundscape</c> user message instead, which a test can read.
    /// </param>
    private static (ScriptedEntityRegistry Registry, Soundscapes Soundscapes) NewRegistry(
        out RecordingEngine net)
    {
        // A player index the push can be addressed to. Zero is EVERYBODY on the
        // send call and is one integer from "no such player", which is why
        // Soundscapes.Push checks it — so a double that answered the default 0
        // would silently exercise the guard instead of the send.
        net = Fake().Returns("Entities.GetIndex", 1);

        // Built through the container, exactly as the module builds it, so the
        // test exercises the same construction the game does.
        var soundscapes = new Soundscapes(new RecordingLog(), net, net, Fake(), Fake(), Fake());

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvSoundscape).Assembly);
        registry.Services = new ServiceContainer().AddInstance<ISoundscapes>(soundscapes);

        return (registry, soundscapes);
    }

    private static T Spawn<T>(ScriptedEntityRegistry registry, EntityHandle handle,
                              string className, params (string Key, string Value)[] keys)
        where T : ScriptedEntity
    {
        foreach (var (key, value) in keys)
            registry.KeyValue(handle, className, key, value);

        registry.Spawn(handle, className);
        return (T)registry.Get(handle)!;
    }

    /// <summary>
    /// -1 means unlimited and 0 means never, and both are load-bearing: the
    /// first is what most maps use and the reason the pass has a trace budget,
    /// the second is what keeps a triggerable out of the automatic contest.
    /// </summary>
    [Fact]
    public void RadiusRule()
    {
        Assert.True(Soundscapes.WithinRadius(500f, 100f), "a soundscape reaches inside its radius");
        Assert.True(!Soundscapes.WithinRadius(500f, 900f), "and not outside it");

        // Strictly greater, not >=, which is stock. At exactly the radius you
        // are outside it.
        Assert.True(!Soundscapes.WithinRadius(500f, 500f), "the boundary itself is outside");

        Assert.True(Soundscapes.WithinRadius(-1f, 100000f), "radius -1 reaches everywhere");
        Assert.True(!Soundscapes.WithinRadius(0f, 0.1f), "radius 0 reaches nowhere");
    }

    /// <summary>
    /// The client compares the entity token with the last one it saw to decide
    /// whether the soundscape changed. Zero is its "none", so ids start at one;
    /// two entities sharing one would make a genuine change look like none.
    /// </summary>
    [Fact]
    public void IdentityTokens()
    {
        var (registry, soundscapes) = NewRegistry();

        var first  = Spawn<EnvSoundscape>(registry, new EntityHandle(0x59_0001u),
                                          EntityNames.EnvSoundscape, ("soundscape", "cellar"));
        var second = Spawn<EnvSoundscape>(registry, new EntityHandle(0x59_0002u),
                                          EntityNames.EnvSoundscape, ("soundscape", "hall"));

        Assert.True(first.SoundscapeId == 1, "the first soundscape's token is one");
        Assert.True(second.SoundscapeId == 2, "the second gets its own");
        Assert.True(first.SoundscapeId != 0 && second.SoundscapeId != 0, "no soundscape holds the client's zero");
        Assert.True(soundscapes.All.Count == 2, "both are registered");

        // Enabling and disabling is the whole of the Enable/Disable inputs, and
        // a disabled soundscape is skipped by the pass rather than removed.
        Assert.True(first.Enabled, "a soundscape starts enabled");
        registry.Input(first.Handle, "Disable", "", 0f, default);
        Assert.True(!first.Enabled, "Disable switches it off");
        registry.Input(first.Handle, "ToggleEnabled", "", 0f, default);
        Assert.True(first.Enabled, "ToggleEnabled switches it back");

        var offAtStart = Spawn<EnvSoundscape>(registry, new EntityHandle(0x59_0003u),
                                              EntityNames.EnvSoundscape,
                                              ("soundscape", "vent"), ("StartDisabled", "1"));
        Assert.True(!offAtStart.Enabled, "StartDisabled holds one off");

        // The triggerable is excluded from the automatic contest by saying so,
        // rather than by stock's accident of a zero default radius.
        var triggerable = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0004u),
            EntityNames.EnvSoundscapeTriggerable, ("soundscape", "gallery"), ("radius", "600"));

        Assert.True(triggerable.IsTriggerable, "a triggerable says it is one");
        Assert.True(triggerable.Radius == 600f, "even when the map gave it a radius");
        Assert.True(!first.IsTriggerable, "and a plain soundscape does not");

        soundscapes.Reset();
        Assert.True(soundscapes.All.Count == 0, "reset forgets every soundscape");
    }

    /// <summary>
    /// A proxy borrows the index and the eight position names, and keeps its
    /// own place and radius. The index is what matters most: every proxy of one
    /// soundscape reports the same one, which is what stops the sounds
    /// restarting as a player moves between the rooms they cover.
    /// </summary>
    [Fact]
    public void ProxyBorrows()
    {
        var (registry, soundscapes) = NewRegistry();

        var main = Spawn<EnvSoundscape>(registry, new EntityHandle(0x59_0011u),
            EntityNames.EnvSoundscape,
            ("targetname", "main_ss"), ("soundscape", "cellar"),
            ("radius", "500"), ("position0", "drip"), ("position3", "fan"));

        Assert.True(main.PositionName(0) == "drip" && main.PositionName(3) == "fan", "the main soundscape keeps its position names");
        Assert.True(main.PositionName(1).Length == 0, "and the ones the map left out are empty");
        Assert.True(main.PositionName(8).Length == 0, "position names stop at eight");

        // The proxy's own resolve happens on its first think, because it names
        // another map entity and cannot look one up mid-parse. Without an
        // engine the lookup finds nothing, which is the honest outcome here —
        // what is checked is that the proxy asks rather than what it finds.
        var proxy = Spawn<EnvSoundscapeProxy>(registry, new EntityHandle(0x59_0012u),
            EntityNames.EnvSoundscapeProxy,
            ("MainSoundscapeName", "main_ss"), ("radius", "300"));

        Assert.True(proxy.SoundscapeName.Length == 0, "a proxy has no soundscape name of its own");
        Assert.True(proxy.SoundscapeIndex == -1, "and does not resolve one at spawn");
        Assert.True(proxy.Radius == 300f, "it keeps its own radius");
        Assert.True(proxy.SoundscapeId != main.SoundscapeId, "and its own identity token");
    }

    /// <summary>
    /// Overlapping trigger volumes. This is the piece with ordering in it, and
    /// the failure is audible: leaving the inner room would drop a player into
    /// silence instead of back into the outer one.
    /// </summary>
    [Fact]
    public void TriggerStack()
    {
        var (registry, soundscapes) = NewRegistry();

        var outer = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0021u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "outer"), ("soundscape", "hall"));

        var inner = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0022u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "inner"), ("soundscape", "vault"));

        Assert.True(soundscapes.TriggerDepth(Player) == 0, "nobody starts in a volume");

        soundscapes.EnterTrigger(Player, outer);
        Assert.True(soundscapes.CurrentId(Player) == outer.SoundscapeId, "entering one is heard");

        soundscapes.EnterTrigger(Player, inner);
        Assert.True(soundscapes.CurrentId(Player) == inner.SoundscapeId, "entering a second overrides it");
        Assert.True(soundscapes.TriggerDepth(Player) == 2, "and both are remembered");

        soundscapes.LeaveTrigger(Player, inner);
        Assert.True(soundscapes.CurrentId(Player) == outer.SoundscapeId, "leaving the inner one falls back to the outer");

        soundscapes.LeaveTrigger(Player, outer);
        Assert.True(soundscapes.CurrentId(Player) == 0, "leaving the last one is silence");
        Assert.True(soundscapes.TriggerDepth(Player) == 0, "and the stack is empty");

        // Entering the same volume twice — which happens when a player noclips
        // out and back without an end touch — must not stack it twice, or the
        // one matching leave would strand them in a volume they are not in.
        soundscapes.EnterTrigger(Player, outer);
        soundscapes.EnterTrigger(Player, outer);
        Assert.True(soundscapes.TriggerDepth(Player) == 1, "entering one twice counts once");

        soundscapes.LeaveTrigger(Player, outer);
        Assert.True(soundscapes.TriggerDepth(Player) == 0, "and one leave clears it");

        soundscapes.Forget(Player);
        Assert.True(soundscapes.CurrentId(Player) == 0, "forgetting a player drops their state");
    }

    //-------------------------------------------------------------------------
    // THE PUSH — plan_entity.md step 8e-sound.
    //
    // Everything above this line was already true when step 7e measured the
    // defect: the class decided correctly, changed its answer on entry, and
    // told the engine through IEngineSound.PlayerSetSoundscape, whose NATIVE
    // BODY WAS EMPTY. So a player heard nothing, on a map with 255 soundscapes
    // parsed, and every fact above stayed green. These are the facts about the
    // wire.
    //-------------------------------------------------------------------------

    /// <summary>Entering a volume puts a Soundscape message on the wire.</summary>
    [Fact]
    public void EnteringATriggerSendsTheSoundscapeMessage()
    {
        var (registry, soundscapes) = NewRegistry(out var net);

        var hall = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0031u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "hall"), ("soundscape", "hall"));

        soundscapes.EnterTrigger(Player, hall);

        Assert.True(net.CountOf("UserMessages.Send") == 1,
                    "entering one volume sends exactly one push");

        var sent = net.Last("UserMessages.Send")!.Value;
        Assert.Equal("SourceSharp:Soundscape", sent.Args[0]);
    }

    /// <summary>
    /// And it does NOT call the dead engine slot, which is the half a test
    /// written before the fix would have asserted the other way round.
    /// </summary>
    [Fact]
    public void EnteringATriggerNoLongerCallsTheEmptyEngineSlot()
    {
        var (registry, soundscapes) = NewRegistry(out var net);

        var hall = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0032u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "hall"), ("soundscape", "hall"));

        soundscapes.EnterTrigger(Player, hall);

        Assert.True(net.CountOf("Sound.PlayerSetSoundscape") == 0,
                    "PlayerSetSoundscape's native body is empty and calling it reached nobody");
    }

    /// <summary>
    /// The push is addressed to ONE player, and never to zero — which the send
    /// call reads as everybody.
    /// </summary>
    [Fact]
    public void ThePushIsAddressedToTheOnePlayerItWasDecidedFor()
    {
        var (registry, soundscapes) = NewRegistry(out var net);

        var hall = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0033u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "hall"), ("soundscape", "hall"));

        soundscapes.EnterTrigger(Player, hall);

        var sent = net.Last("UserMessages.Send")!.Value;
        Assert.Equal(1, sent.Args[2]);
        Assert.Equal(true, sent.Args[3]);
    }

    /// <summary>
    /// A player the engine cannot index gets no push at all, rather than one
    /// addressed to slot zero.
    /// </summary>
    /// <remarks>
    /// Zero is EVERYBODY on <c>SendUserMessage</c> and is one integer from the
    /// "no such player" answer, so the failure this refuses is a soundscape
    /// meant for one player arriving on every screen in the server — which
    /// nobody would report as a bug.
    /// </remarks>
    [Fact]
    public void AnUnindexablePlayerGetsNoPushRatherThanEverybody()
    {
        var net = Fake().Returns("Entities.GetIndex", 0);
        var soundscapes = new Soundscapes(new RecordingLog(), net, net, Fake(), Fake(), Fake());

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(EnvSoundscape).Assembly);
        registry.Services = new ServiceContainer().AddInstance<ISoundscapes>(soundscapes);

        var hall = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0034u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "hall"), ("soundscape", "hall"));

        soundscapes.EnterTrigger(Player, hall);

        Assert.True(net.CountOf("UserMessages.Send") == 0,
                    "index 0 is everybody, so a push that cannot name its player is not sent");
    }

    /// <summary>
    /// <b>Leaving the last volume sends a push too</b>, and it is the one that
    /// carries silence.
    /// </summary>
    /// <remarks>
    /// The client starts nothing unless <c>entIndex &gt; 0</c>
    /// (<c>C_SoundscapeSystem::UpdateAudioParams</c>), so an entIndex of 0 with
    /// an index of -1 is how "you have left, hear nothing" is spelled. Without
    /// this push a player who walked out of a room would keep hearing it.
    /// </remarks>
    [Fact]
    public void LeavingTheLastVolumeSendsSilenceRatherThanNothing()
    {
        var (registry, soundscapes) = NewRegistry(out var net);

        var hall = Spawn<EnvSoundscapeTriggerable>(registry, new EntityHandle(0x59_0035u),
            EntityNames.EnvSoundscapeTriggerable, ("targetname", "hall"), ("soundscape", "hall"));

        soundscapes.EnterTrigger(Player, hall);
        soundscapes.LeaveTrigger(Player, hall);

        Assert.True(net.CountOf("UserMessages.Send") == 2,
                    "one push in, one push out");
    }
}
