using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_side_isolation §2.2a: the user-message registry, its server send and
/// the client's dispatch with its native hooks, all managed. Each fact is one
/// behaviour the native usermessage_* gtests used to pin (the commit lists them
/// before and after); the forwarders themselves have their own gtest,
/// usermessage_forwarders_test.
/// </summary>
public sealed class UserMessageRoutingTests
{
    // No probe classes of this suite's own (UserMessageDeclarationTests says
    // why): a [UserMessage] in this assembly joins every scan in the test
    // process. The subjects are the real declarations: Shake (13 bytes, a
    // handler), Fade, Damage (a handler with an event), VGUIMenu (no handler).
    /// <summary>
    /// This fact's log: every subject a helper builds is handed it (the
    /// composition's ILog in the game), so a fact reads what its own subjects
    /// said. xUnit builds the class once per fact.
    /// </summary>
    private readonly RecordingLog _log = new();

    private UserMessageRegistry Registered(bool receives = true, IServiceLocator? services = null)
    {
        var registry = new UserMessageRegistry(_log, receives);
        registry.RegisterAll(typeof(SourceSharp.Shared.FadeMessage).Assembly, services);
        return registry;
    }

    /// <summary>The warnings this fact's subjects said while <paramref name="act"/> ran.</summary>
    private List<string> Warnings(Action act)
    {
        int before = _log.Lines.Count;
        act();
        return _log.Lines.Skip(before).Where(l => l.Warning).Select(l => l.Text).ToList();
    }

    //-------------------------------------------------------------------------
    // The registry: the only store.
    //-------------------------------------------------------------------------

    [Fact]
    public void AKeysIdIsThePositionItsDeclarationSitsAt()
    {
        var registry = Registered();

        Assert.NotEmpty(registry.Messages);
        foreach (var declaration in registry.Messages)
            Assert.Same(declaration, registry.At(registry.IdOf(declaration.Name)));
    }

    [Fact]
    public void AnUndeclaredKeyHasNoId()
    {
        var registry = Registered();

        Assert.True(registry.IdOf("Shake") >= 0, "the positive arm: a declared key has an id");
        Assert.Equal(-1, registry.IdOf("NoSuchMessage"));
    }

    [Fact]
    public void AnIdTheRegistryDoesNotHoldHasNoDeclaration()
    {
        var registry = Registered();

        Assert.NotNull(registry.At(registry.Messages.Count - 1));
        Assert.Null(registry.At(registry.Messages.Count));
        Assert.Null(registry.At(-1));
    }

    //-------------------------------------------------------------------------
    // The server's managed-built area: GetUserMessageInfo.
    //-------------------------------------------------------------------------

    private static unsafe (byte Ok, string Name, int Size) Info(UserMessagesBinding binding, int id, int buffer = 64)
    {
        var name = new byte[buffer];
        int size = -99;
        byte ok;
        fixed (byte* p = name)
            ok = binding.GetUserMessageInfo(id, p, buffer, &size);
        int end = Array.IndexOf(name, (byte)0);
        return (ok, Encoding.UTF8.GetString(name, 0, end < 0 ? name.Length : end), size);
    }

    [Fact]
    public void GetUserMessageInfoAnswersTheKeyAndTheSizeFromTheRegistry()
    {
        var registry = Registered();
        var binding = new UserMessagesBinding(registry);

        var (ok, name, size) = Info(binding, registry.IdOf("Shake"));

        Assert.Equal(1, ok);
        Assert.Equal("Shake", name);
        Assert.Equal(13, size);
    }

    [Fact]
    public void GetUserMessageInfoRefusesAnIdTheRegistryDoesNotHold()
    {
        var registry = Registered();
        var binding = new UserMessagesBinding(registry);

        Assert.Equal(1, Info(binding, 0).Ok);
        Assert.Equal(0, Info(binding, registry.Messages.Count).Ok);
    }

    [Fact]
    public void GetUserMessageInfoTruncatesTheKeyToTheEnginesBuffer()
    {
        var registry = Registered();
        var binding = new UserMessagesBinding(registry);

        var (ok, name, _) = Info(binding, registry.IdOf("Shake"), buffer: 4);

        Assert.Equal(1, ok);
        Assert.Equal("Sha", name);
    }

    // AValveSendersNameIsAnsweredWithItsId, ANameSentBeforeTheListIsRegistered...
    // and AnUnknownNameAfterRegistration... WENT with LookupUserMessage
    // (user-message payload model, 2026-10-04): no native code names a message
    // any more, so there is no name to answer or refuse. A C# sender's unknown
    // key is refused by UserMessageSender (below).

    //-------------------------------------------------------------------------
    // The server's send.
    //-------------------------------------------------------------------------

    [Fact]
    public void ASendHandsTheEngineTheKeysIdAndThePayload()
    {
        var registry = Registered(receives: false);
        var engine = new RecordingUserMessageEngine();

        new UserMessageSender(registry, engine, _log).Send("Fade", new byte[] { 1, 2, 3 }, player: 4, reliable: false);

        var sent = Assert.Single(engine.Sent);
        Assert.Equal(registry.IdOf("Fade"), sent.Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, sent.Payload);
        Assert.Equal(4, sent.Player);
        Assert.False(sent.Reliable);
    }

    [Fact]
    public void AnUndeclaredKeyIsNotSent()
    {
        var engine = new RecordingUserMessageEngine();
        var sender = new UserMessageSender(Registered(receives: false), engine, _log);

        var warnings = Warnings(() => sender.Send("NoSuchMessage", new byte[] { 1 }));

        Assert.Empty(engine.Sent);
        Assert.Contains(warnings, w => w.Contains("'NoSuchMessage' is not registered; not sent"));
    }

    [Fact]
    public void APayloadOverTheEnginesLimitIsNotSent()
    {
        var engine = new RecordingUserMessageEngine();
        var sender = new UserMessageSender(Registered(receives: false), engine, _log);

        sender.Send("Fade", new byte[UserMessageSender.MaxPayloadBytes]);
        Assert.Single(engine.Sent);

        var warnings = Warnings(() => sender.Send("Fade", new byte[UserMessageSender.MaxPayloadBytes + 1]));

        Assert.Single(engine.Sent);
        Assert.Contains(warnings, w => w.Contains("over the 255-byte limit"));
    }

    [Fact]
    public void AMessageForAnEmptySlotIsReportedAndNeverBroadcast()
    {
        var engine = new RecordingUserMessageEngine();
        engine.EmptySlots.Add(5);
        var sender = new UserMessageSender(Registered(receives: false), engine, _log);

        var warnings = Warnings(() => sender.Send("Fade", new byte[] { 1 }, player: 5));

        Assert.Empty(engine.Sent);
        Assert.Contains(warnings, w => w.Contains("aimed at player 5, who is not in that slot"));
    }

    //-------------------------------------------------------------------------
    // The client's dispatch and the native hooks.
    //-------------------------------------------------------------------------

    // The server's table, written by a server registry over the same
    // declarations, so a client fact can name an id by IdOf.
    private FakeStringTable ServerTable()
    {
        var table = new FakeStringTable();
        Registered(receives: false).WriteTable(table);
        return table;
    }

    private (UserMessageRegistry Registry, RecordingUserMessageEngine Engine, UserMessageReceiver Receiver) Client(
        IServiceLocator? services = null, FakeStringTable? table = null)
    {
        var registry = Registered(receives: true, services);
        var engine = new RecordingUserMessageEngine();
        return (registry, engine, new UserMessageReceiver(registry, table ?? ServerTable(), _log));
    }

    // Damage's handler raises its event through the container's instance.
    // A message's payload as native hands it over: one BitBlock (C9). A
    // handle the test gave no payload carries none.
    private static byte[] Block(RecordingUserMessageEngine engine, nint handle)
    {
        byte[] payload = engine.Payloads.TryGetValue(handle, out var p) ? p : [];
        var block = new byte[BitBlock.HeaderBytes + payload.Length];
        BitBlock.SetBits(block, payload.Length * 8);
        payload.CopyTo(block, BitBlock.HeaderBytes);
        return block;
    }

    private (ServiceContainer Services, List<DamageArgs> Heard) DamageEvents(Action<DamageArgs>? also = null)
    {
        var services = new ServiceContainer();
        UserMessageRegistry.AddHandlersTo(services, typeof(SourceSharp.Shared.FadeMessage).Assembly, _log);
        var heard = new List<DamageArgs>();
        services.Require<IUserMessageEvent<DamageArgs>>().Fired += args => { heard.Add(args); also?.Invoke(args); };
        return (services, heard);
    }

    [Fact]
    public void DispatchHandsTheManagedHandlerThePayload()
    {
        var (services, heard) = DamageEvents();
        var (registry, engine, receiver) = Client(services);
        engine.Payloads[0x10] = new byte[] { 7, 93, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.True(receiver.Dispatch(registry.IdOf("Damage"), Block(engine, 0x10)));

        Assert.Equal(new DamageArgs(7, 93, 4), Assert.Single(heard));
    }

    /// <summary>
    /// The client's live path counts what arrives, handled or not: the dev
    /// API's received counts (lane player-msgs) read these, and the registry's
    /// own Dispatch is not what the engine calls.
    /// </summary>
    [Fact]
    public void TheReceiverCountsEachMessageThatArrives()
    {
        var (services, _) = DamageEvents();
        var (registry, engine, receiver) = Client(services);
        engine.Payloads[0x10] = new byte[18];
        Assert.Empty(registry.ReceivedCounts());

        receiver.Dispatch(registry.IdOf("Damage"), Block(engine, 0x10));
        receiver.Dispatch(registry.IdOf("Damage"), Block(engine, 0x10));

        Assert.Equal(("Damage", 2L), Assert.Single(registry.ReceivedCounts()));
    }

    [Fact]
    public void AMessageWithNoManagedHandlerIsNeverRead()
    {
        var (types, bare) = UserMessageFixtures.WithBareDeclaration();
        var server = new UserMessageRegistry(_log, receives: false);
        server.Register(types, typeof(SourceSharp.Shared.FadeMessage).Assembly);
        var table = new FakeStringTable();
        server.WriteTable(table);

        var registry = new UserMessageRegistry(_log, receives: true);
        registry.Register(types, typeof(SourceSharp.Shared.FadeMessage).Assembly);
        var engine = new RecordingUserMessageEngine();
        var receiver = new UserMessageReceiver(registry, table, _log);

        string key = registry.Messages.Single(m => m.Type == bare).Name;

        Assert.True(receiver.Dispatch(registry.IdOf(key), Block(engine, 0x20)));
    }

    [Fact]
    public void AnIdNoDeclarationHoldsIsRefusedAndRunsNothing()
    {
        var (registry, engine, receiver) = Client();

        var warnings = Warnings(() => Assert.False(receiver.Dispatch(registry.Messages.Count, Block(engine, 0x20))));
        Assert.Contains(warnings, w => w.Contains($"user message id {registry.Messages.Count} is not in the server's SsUserMessages table"));
    }

    [Fact]
    public void AThrowingHandlerIsSaidAndNotPropagated()
    {
        var (services, heard) = DamageEvents(_ => throw new InvalidOperationException("the subscriber failed"));
        var (registry, engine, receiver) = Client(services);
        engine.Payloads[0x10] = new byte[] { 7, 93, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        var warnings = Warnings(() => Assert.True(receiver.Dispatch(registry.IdOf("Damage"), Block(engine, 0x10))));

        Assert.Single(heard);
        Assert.Contains(warnings, w => w.Contains("'Damage' handler threw"));
    }

    //-------------------------------------------------------------------------
    // The client's managed-built area forwards into the receiver.
    //-------------------------------------------------------------------------

    [Fact]
    public unsafe void TheClientAreaHandsDispatchToTheReceiver()
    {
        var (registry, engine, receiver) = Client();
        var binding = new UserMessagesClientBinding(new Crossings(), receiver);

        // The payload arrives as one BitBlock: 80 bits, no read back.
        byte[] block = new byte[BitBlock.HeaderBytes + 10];
        BitBlock.SetBits(block, 80);
        fixed (byte* p = block)
        {
            Assert.Equal(1, binding.DispatchUserMessage(registry.IdOf("Fade"), p, block.Length));
            Assert.Equal(0, binding.DispatchUserMessage(registry.Messages.Count, p, block.Length));
            Assert.Equal(0, binding.DispatchUserMessage(registry.IdOf("Fade"), p, 2));   // shorter than its prefix
        }
    }

    [Fact]
    public void ABlockPayloadReachesTheHandlerWholeAndAnOversizedOneIsSaid()
    {
        var (services, heard) = DamageEvents();
        var (registry, engine, receiver) = Client(services);

        // A Damage payload, as the server writes it, in a block.
        byte[] payload = { 7, 93, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        byte[] block = new byte[BitBlock.HeaderBytes + payload.Length];
        BitBlock.SetBits(block, payload.Length * 8);
        payload.CopyTo(block, BitBlock.HeaderBytes);
        Assert.True(receiver.Dispatch(registry.IdOf("Damage"), block));
        Assert.Single(heard);

        // Native's "too large": the prefix is minus the block size it needed.
        byte[] tooBig = new byte[BitBlock.HeaderBytes + 8];
        BitBlock.SetBits(tooBig, -(BitBlock.HeaderBytes + 600));
        var warnings = Warnings(() => Assert.True(receiver.Dispatch(registry.IdOf("Damage"), tooBig)));
        Assert.Single(heard);
        Assert.Contains(warnings, w => w.Contains("'Damage' is 600 bytes, over the 8-byte dispatch limit"));
    }

    //-------------------------------------------------------------------------
    // P2b: key order, the SsUserMessages table, and the late refusal.
    //-------------------------------------------------------------------------

    [Fact]
    public void IdsAreTheOrdinalOrderOfTheKeys()
    {
        var names = Registered().Messages.Select(m => m.Name).ToList();

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
        Assert.Equal(Enumerable.Range(0, names.Count), Registered().Messages.Select(m => m.Index));
    }

    [Fact]
    public void TheScanOrderDoesNotMoveAnId()
    {
        var types = typeof(FadeMessage).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(UserMessageAttribute), false).Length > 0).ToList();
        var forward = new UserMessageRegistry(_log, receives: false);
        var backward = new UserMessageRegistry(_log, receives: false);

        forward.Register(types, typeof(FadeMessage).Assembly);
        types.Reverse();
        backward.Register(types, typeof(FadeMessage).Assembly);

        Assert.True(forward.Messages.Count > 30, "the positive arm: the shared declarations registered");
        Assert.Equal(forward.Messages.Select(m => m.Name), backward.Messages.Select(m => m.Name));
    }

    [Fact]
    public void TheServerWritesEachKeyAtItsId()
    {
        var registry = Registered(receives: false);
        var table = new FakeStringTable();

        Assert.Equal(registry.IdCount, registry.WriteTable(table));

        Assert.Equal(registry.IdCount, table.Strings.Count);
        foreach (var declaration in registry.Messages)
            Assert.Equal(declaration.Name, table.Strings[declaration.Index]);
    }

    [Fact]
    public void ARegistrationAfterTheTableIsWrittenIsRefused()
    {
        var registry = Registered(receives: false);
        registry.WriteTable(new FakeStringTable());

        var refused = Assert.Throws<InvalidOperationException>(
            () => registry.RegisterAll(typeof(FadeMessage).Assembly));

        Assert.Contains("registered after the SsUserMessages table was written", refused.Message);
    }

    [Fact]
    public void ARegistrationBeforeTheTableIsWrittenIsAccepted()
    {
        var registry = Registered(receives: false);

        Assert.False(registry.TableWritten);
        Assert.True(registry.RegisterAll(typeof(FadeMessage).Assembly) > 0);
    }

    [Fact]
    public void AReloadedServerTakesItsIdsFromTheLiveTable()
    {
        var registry = Registered(receives: false);
        var live = new FakeStringTable();
        live.Strings.AddRange(registry.Messages.Select(m => m.Name).Reverse());

        Assert.Equal(registry.Messages.Count, registry.AdoptTable(live));

        for (int id = 0; id < live.Strings.Count; id++)
            Assert.Equal(id, registry.IdOf(live.Strings[id]));
        Assert.True(registry.TableWritten);
    }

    [Fact]
    public void AKeyTheLiveTableLacksIsRefusedUntilRestart()
    {
        var registry = Registered(receives: false);
        var live = new FakeStringTable();
        live.Strings.AddRange(registry.Messages.Select(m => m.Name).Where(n => n != "Fade"));

        var warnings = Warnings(() => registry.AdoptTable(live));

        Assert.Equal(-1, registry.IdOf("Fade"));
        Assert.Contains(warnings, w => w.Contains("'Fade' was declared after the SsUserMessages table was sent"));
    }

    [Fact]
    public void AnIdNothingDeclaresAnyMoreStaysAHoleAndKeepsTheRestInPlace()
    {
        var registry = Registered(receives: false);
        var live = new FakeStringTable();
        live.Strings.Add("Gone:message");
        live.Strings.AddRange(registry.Messages.Select(m => m.Name));
        registry.AdoptTable(live);

        Assert.Null(registry.At(0));
        Assert.Equal(1, registry.IdOf(live.Strings[1]));

        var next = new FakeStringTable();
        Assert.Equal(registry.IdCount, registry.WriteTable(next));
        Assert.Equal("#retired0", next.Strings[0]);
        Assert.Equal(live.Strings[^1], next.Strings[registry.IdCount - 1]);
    }

    [Fact]
    public void WithNoTableYetThereIsNothingToAdopt()
    {
        var registry = Registered(receives: false);
        var before = registry.Messages.Select(m => (m.Name, m.Index)).ToList();

        Assert.Equal(-1, registry.AdoptTable(new FakeStringTable { Present = false }));

        Assert.Equal(before, registry.Messages.Select(m => (m.Name, m.Index)));
        Assert.False(registry.TableWritten);
    }

    [Fact]
    public void TheClientMapsAnIdThroughTheServersTableNotItsOwnOrder()
    {
        // The server's order is the client's reversed: only the table can say
        // which declaration an id is.
        var table = new FakeStringTable();
        table.Strings.AddRange(Registered().Messages.Select(m => m.Name).Reverse());
        var (registry, engine, receiver) = Client(table: table);
        engine.Payloads[0x20] = new byte[10];

        int serverFade = table.Strings.IndexOf("Fade");
        Assert.NotEqual(registry.IdOf("Fade"), serverFade);

        int before = _log.Lines.Count;
        Assert.True(receiver.Dispatch(serverFade, Block(engine, 0x20)));
        Assert.Contains(_log.Lines.Skip(before), l => l.Text.Contains($"'Fade' first arrived as id {serverFade}: managed handler"));
    }

    [Fact]
    public void AKeyTheClientDoesNotDeclareIsDroppedAndSaidOnce()
    {
        var table = ServerTable();
        table.Strings.Add("Elsewhere:message");
        var (_, engine, receiver) = Client(table: table);
        int id = table.Strings.Count - 1;

        var warnings = Warnings(() =>
        {
            Assert.False(receiver.Dispatch(id, Block(engine, 0x20)));
            Assert.False(receiver.Dispatch(id, Block(engine, 0x20)));
        });
        Assert.Single(warnings, w => w.Contains("'Elsewhere:message' (id " + id + ") is not one this client declares"));
    }

    [Fact]
    public void TheIdMapIsReadOnceAndAgainAfterForget()
    {
        var table = ServerTable();
        var (registry, engine, receiver) = Client(table: table);
        engine.Payloads[0x20] = new byte[10];

        receiver.Dispatch(registry.IdOf("Fade"), Block(engine, 0x20));
        int afterFirst = table.Reads;
        receiver.Dispatch(registry.IdOf("Fade"), Block(engine, 0x20));

        Assert.Equal(table.Strings.Count, afterFirst);
        Assert.Equal(afterFirst, table.Reads);

        receiver.Forget();
        receiver.Dispatch(registry.IdOf("Fade"), Block(engine, 0x20));
        Assert.Equal(2 * table.Strings.Count, table.Reads);
    }

    [Fact]
    public void AKeysFirstArrivalIsSaidOncePerLevel()
    {
        var (registry, engine, receiver) = Client();
        engine.Payloads[0x20] = new byte[10];
        int id = registry.IdOf("Fade");

        int before = _log.Lines.Count;
        receiver.Dispatch(id, Block(engine, 0x20));
        receiver.Dispatch(id, Block(engine, 0x20));
        receiver.Forget();
        receiver.Dispatch(id, Block(engine, 0x20));
        var lines = _log.Lines.Skip(before).Select(l => l.Text).ToList();

        Assert.Equal(2, lines.Count(l => l.Contains($"'Fade' first arrived as id {id}: managed handler\n")));
    }

    [Fact]
    public void AMalformedPayloadIsSaidThroughTheCompositionsLogUnderItsKey()
    {
        // The bridge answers false and dispatches nothing; the receiver that
        // dispatched it says so through the log it was handed.
        var (services, heard) = DamageEvents();
        var (registry, engine, receiver) = Client(services);
        engine.Payloads[0x10] = new byte[] { 7, 93 };

        var warnings = Warnings(() => Assert.True(receiver.Dispatch(registry.IdOf("Damage"), Block(engine, 0x10))));

        Assert.Empty(heard);
        Assert.Contains(warnings, w => w.Contains("user message 'Damage' ended mid-member or was malformed; not dispatched"));

        // And the registry's own dispatch says it the same way.
        var direct = Registered(services: services);
        var before = Warnings(() => direct.Dispatch(direct.IdOf("Damage"), new byte[] { 7 }));
        Assert.Contains(before, w => w.Contains("user message 'Damage' ended mid-member or was malformed"));
    }
}
