extern alias client;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Server.Party;

using Xunit;

using ClientPartyRegistrar = client::TF2.Descent.Client.Party.PartyClientRegistrar;

namespace TF2.Descent.Tests;

public sealed partial class HookOrderTests
{
    /// <summary>
    /// The party's chain members (WO9): the change chain's (the views, the notices, and every seam
    /// that hears a change), and the core chains the party joins (the verbs among the client
    /// commands, the views among the spawn hooks, the parties' frame and disconnect) each have an
    /// order no other Descent member of the chain holds, after TF2's band.
    /// </summary>
    [Fact]
    public void The_partys_chain_members_have_orders_of_their_own()
    {
        var assembly = typeof(Parties).Assembly;
        Type[] chains = [typeof(IPartyHook), typeof(IClientCommandHook), typeof(IPlayerSpawnHook), typeof(IFrameLifecycle), typeof(IClientLifecycle)];

        // The stimulus: the party's members are among those compared.
        Assert.Contains(typeof(IPartyHook), HooksOf(new PartyRegistrar()));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IPartyHook)), m => m.Name == nameof(PartySync));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IPartyHook)), m => m.Name == nameof(PartyNotices));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IClientCommandHook)), m => m.Name == nameof(PartyVerbs));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IPlayerSpawnHook)), m => m.Name == nameof(PartySync));
        Assert.Contains(Implementations(assembly, Side.Server, typeof(IFrameLifecycle)), m => m.Name == nameof(Parties));

        AssertDistinct(assembly, Side.Server, chains);
        foreach (var chain in chains)
            Assert.All(Implementations(assembly, Side.Server, chain), m => Assert.True(m.Order >= ModStages.First, $"{chain.Name}: {m.Name} at {m.Order}"));
    }

    [Fact]
    public void The_client_party_view_hook_is_registered_and_its_members_have_orders_of_their_own()
    {
        var hooks = ClientHooksOf(new ClientPartyRegistrar());

        Assert.Equal([typeof(client::TF2.Descent.Shared.IPartyViewHook)], hooks);
        ClientAssertDistinct(typeof(ClientPartyRegistrar).Assembly, Side.Client, hooks);
    }
}
