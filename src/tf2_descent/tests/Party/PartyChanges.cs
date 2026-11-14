using SourceSharp.Abi;

using TF2.Descent.Server.Party;

namespace TF2.Descent.Tests.Party;

/// <summary>A listener on the party chain: every change heard, in order.</summary>
internal sealed class PartyChanges : IPartyHook
{
    public List<PartyChange> Heard { get; } = [];

    public IHooks<IPartyHook> Hooks => new Hub(this);

    public void OnPartyChanged(in PartyChange change) => Heard.Add(change);

    /// <summary>The kinds and members told for one party.</summary>
    public List<(PartyChangeKind Kind, string Member)> Kinds(string partyId) =>
        [.. Heard.Where(c => c.PartyId == partyId).Select(c => (c.Kind, c.Member))];

    private sealed class Hub(IPartyHook hook) : IHooks<IPartyHook>
    {
        public IReadOnlyList<IPartyHook> All => [hook];
        public void Each(string what, Action<IPartyHook> call) => call(hook);
    }
}
