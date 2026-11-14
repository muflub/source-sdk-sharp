using TF2.Descent.Server.Party;

namespace TF2.Descent.Tests.Party;

/// <summary>
/// A roster in which every hero is one party: what the facts written before parties (WO9) meant
/// by "every hero near the kill", so each keeps its meaning. A fact about the party itself uses
/// <see cref="PartyRoster"/> over real parties.
/// </summary>
internal sealed class OneParty : IPartyRoster
{
    public string? PartyOf(int slot) => "one";

    public IReadOnlyList<int> MatesOf(int slot) => [slot];

    public bool SameParty(int a, int b) => true;

    public string MachineKey(int slot) => "party one";
}
