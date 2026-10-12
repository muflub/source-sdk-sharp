using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That declaring a user message and handling one are separable, and that the
/// separation is what the registry acts on.
///
/// <para>
/// The list of user messages is protocol: a message travels as an INDEX into it,
/// so every declaration must occupy its position whether or not managed code
/// reads the payload. Most do not — 26 of the 31 standard messages are read by a
/// native HUD element that gets its own copy of the same bytes — and under the
/// old <c>abstract class UserMessage</c> each of those had to carry an empty
/// <c>OnReceived</c> override, which reads as a feature somebody left half
/// finished. <see cref="IUserMessage"/> is optional, so its absence is now the
/// accurate statement: <i>this message exists on the wire and managed code does
/// not read it</i>.
/// </para>
/// </summary>
public class UserMessageDeclarationTests
{
    // No probe classes in this assembly: a [UserMessage] anywhere the registry
    // can reach would take a wire index. The one handler-free declaration the
    // facts need is compiled apart (UserMessageFixtures).
    private static UserMessageRegistry Registered(bool receives = true)
    {
        var registry = new UserMessageRegistry(new RecordingLog(), receives);
        registry.RegisterAll(typeof(FadeMessage).Assembly);
        return registry;
    }

    [Fact]
    public void AReceivingRegistryDispatchesToTheContainersHandler()
    {
        // The handlers are built into the client's container before the HUD
        // is, and RegisterAll must reuse THOSE instances: a handler it built for
        // itself would decode into an object no subscriber holds.
        var services = new ServiceContainer();
        UserMessageRegistry.AddHandlersTo(services, typeof(FadeMessage).Assembly, new RecordingLog());
        var registry = new UserMessageRegistry(new RecordingLog(), receives: true);
        registry.RegisterAll(typeof(FadeMessage).Assembly, services);

        var damage = registry.Messages.Single(m => m.Name == "Damage");
        Assert.Same(services.Require<DamageMessage>(), damage.Handler);

        DamageArgs? heard = null;
        services.Require<IUserMessageEvent<DamageArgs>>().Fired += args => heard = args;

        byte[] payload = { 7, 93, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        registry.Dispatch(damage.Index, payload);

        Assert.Equal(new DamageArgs(7, 93, 4), heard);
    }

    [Fact]
    public void TwoContainersDoNotShareAHandler()
    {
        var first = new ServiceContainer();
        var second = new ServiceContainer();
        UserMessageRegistry.AddHandlersTo(first, typeof(FadeMessage).Assembly, new RecordingLog());
        UserMessageRegistry.AddHandlersTo(second, typeof(FadeMessage).Assembly, new RecordingLog());

        Assert.NotSame(first.Require<DamageMessage>(), second.Require<DamageMessage>());
    }

    private static (UserMessageRegistry Registry, UserMessageDeclaration Bare) WithBare(bool receives = true)
    {
        var (types, bare) = UserMessageFixtures.WithBareDeclaration();
        var registry = new UserMessageRegistry(new RecordingLog(), receives);
        registry.Register(types, typeof(FadeMessage).Assembly);
        return (registry, registry.Messages.Single(m => m.Type == bare));
    }

    [Fact]
    public void ADeclarationWithoutTheInterfaceStillTakesItsWireIndex()
    {
        var (registry, bare) = WithBare();

        Assert.True(registry.At(bare.Index) == bare,
            "a declaration with no IUserMessage occupies its index like any other");
        Assert.True(!typeof(IUserMessage).IsAssignableFrom(bare.Type),
            "the fixture is declared without IUserMessage, which is what makes this a test");
    }

    [Fact]
    public void ADeclarationWithoutTheInterfaceIsNeverInstantiated()
    {
        var (_, bare) = WithBare();

        Assert.True(bare.Handler is null, "a declaration with no IUserMessage has nothing to construct");
    }

    [Fact]
    public void ADeclarationWithoutTheInterfaceIsNeverDispatched()
    {
        var (registry, bare) = WithBare();

        // The payload is real and the index is right: the only reason nothing
        // happens is that there is no handler. Dispatch must swallow it rather
        // than throw.
        var ex = Record.Exception(() => registry.Dispatch(bare.Index, new byte[] { 1, 2, 3, 4 }));

        Assert.True(ex is null,
            $"dispatching to a declaration-only message must be a no-op, not {ex?.GetType().Name}");
    }

    [Fact]
    public void EveryProductionMessageHasAPayloadAndAHandler()
    {
        // The end state of the payload model: every message is managed end to
        // end, so none is declaration-only.
        var bare = Registered().Messages
            .Where(m => !m.Type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessage<>)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(Registered().Messages.Count > 30, "the production list was read");
        Assert.True(bare.Count == 0, "messages with no payload model: " + string.Join(", ", bare));
    }

    [Fact]
    public void AHandlerIsStillReachedByItsOwnIndex()
    {
        // The other half of the pair, so a Dispatch that simply returned early
        // for everything could not pass this file.
        var registry = Registered();

        int fade = registry.Messages.Single(m => m.Name == "Fade").Index;
        registry.Dispatch(fade, new byte[] { 0x10, 0x00, 0x20, 0x00, 0x03, 0x00, 1, 2, 3, 4 });

        var handler = (FadeMessage)registry.Messages[fade].Handler!;

        Assert.True(handler.Last.Duration == 0x10 && handler.Last.HoldTime == 0x20 && handler.Last.Flags == 3,
            "a message that does implement IUserMessage is still handed its payload");
    }

    [Fact]
    public void TheServerInstantiatesNoHandlersAtAll()
    {
        var server = Registered(receives: false);

        Assert.True(server.Messages.Count > 0,
            "the server must still register the whole list: the index is the protocol");
        Assert.True(server.HandlerCount == 0,
            $"the server receives no user message, so it constructs no handler; built {server.HandlerCount}");
    }

    [Fact]
    public void TheClientInstantiatesEveryHandlerAndNothingElse()
    {
        var (client, _) = WithBare();

        int implementers = client.Messages.Count(m => typeof(IUserMessage).IsAssignableFrom(m.Type));

        Assert.True(client.HandlerCount == implementers,
            $"the client builds exactly the {implementers} declarations that implement IUserMessage, " +
            $"built {client.HandlerCount}");
        Assert.True(client.HandlerCount < client.Messages.Count,
            "at least one declaration is handler-free, or this check proves nothing");
    }

    [Fact]
    public void EveryDeclarationRegistersWhetherOrNotItIsHandled()
    {
        var registry = Registered();

        int declared = typeof(FadeMessage).Assembly.GetTypes()
            .Count(t => t.GetCustomAttribute<UserMessageAttribute>() is not null);

        Assert.True(registry.Messages.Count == declared,
            $"all {declared} attributed classes must take a slot; skipping one shifts every later index");
    }

    [Fact]
    public void TheAttributeDocumentationDoesNotCiteAPropertyThatDoesNotExist()
    {
        // The doc on UserMessageAttribute said ordering was explicit "which is
        // why <see cref="Order"/> is explicit" for as long as there has been no
        // Order property. A cref to a member that was never there survives the
        // compiler, so it takes a check to keep it gone.
        Assert.True(typeof(UserMessageAttribute).GetProperty("Order") is null,
            "there is no Order property on UserMessageAttribute and there never was");

        // UserMessage.cs left Abi for Shared in Phase 1 run 2 (fd127991), and
        // this read skipped silently from then on: a missing file is a failure.
        string? file = RepoLayout.FindSdkFile(
            Path.Combine("src", "sourcesharp", "managed", "SourceSharp.Shared", "UserMessage.cs"));
        Assert.True(file is not null, "UserMessage.cs is found in SourceSharp.Shared");

        string text = File.ReadAllText(file!);
        Assert.True(!text.Contains("<see cref=\"Order\"/>"),
            "UserMessage.cs must not cref an Order property: the cref resolves to nothing "
            + "and the sentence describes a design that changed");
    }
}
