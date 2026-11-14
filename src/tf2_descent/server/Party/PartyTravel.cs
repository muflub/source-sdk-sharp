using SourceSharp.Abi;

namespace TF2.Descent.Server.Party;

/// <summary>
/// Who may take the party down (plan_tf2_rpg D8, 7d: "party leader picks a reached depth"; WO9):
/// the way down from the town, or a level's way down, travels the whole party (hosted: the host's
/// <c>RequestDescent</c> or <c>StairsDown</c> under the leader's lease, for the party's id), so in
/// a party of two or more only the leader asks for it, and he picks among the depths he has
/// reached (D8). A member who is in town while his party is on a level (the host's party has a
/// live instance) may ask, to join the run in progress; the host decides where he lands. Up (the
/// town portal) and the way back to a corpse are each hero's own. Unhosted every player in the
/// process travels at once (one map a process), and nothing is asked.
/// </summary>
public interface IPartyTravel
{
    /// <summary>Null when the hero in <paramref name="slot"/> may ask for <paramref name="direction"/>; else the notice token that says why not.</summary>
    string? Refusal(int slot, TravelDirection direction);
}

/// <summary><see cref="IPartyTravel"/> over the parties and the heroes here.</summary>
[SideService(Side.Server, As = [typeof(IPartyTravel)])]
public sealed class PartyTravel(IParties parties, IPartySlots slots) : IPartyTravel
{
    /// <summary>The notice to a member who asked for the way down: his leader picks it.</summary>
    public const string LeaderPicksToken = "#Descent_Party_LeaderPicks";

    public string? Refusal(int slot, TravelDirection direction)
    {
        if (direction != TravelDirection.Down || slots.HeroOf(slot) is not { } hero || parties.Of(hero) is not { Size: > 1 } party)
            return null;
        if (parties.MemberId(hero) == party.Leader || party.LiveInstance.Length > 0)
            return null;
        return LeaderPicksToken;
    }
}
