using SourceSharp.Abi;

using TF2.Descent.Server.Portal;

namespace TF2.Descent.Server.Party;

/// <summary>
/// Who may use a hero's town portal besides him (WO9; the death plan's Q5: any party member):
/// a hero whose party has the owner as a member. A stranger may
/// not. Read when the pad is used, so a member who leaves the party loses the use at once and
/// the portal stays its owner's. Hosted, the way back through a portal is the host's anchor,
/// and the host decides whose anchor a character may take (<c>ListPartyAnchors</c>).
/// </summary>
[SideService(Side.Server, As = [typeof(IPortalParty)])]
public sealed class PartyPortalParty(IParties parties) : IPortalParty
{
    public bool MayUse(string owner, string hero) =>
        owner == hero || (parties.Of(hero) is { } party && parties.MemberId(owner) is { } ownerId && party.Has(ownerId));
}
