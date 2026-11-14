using System.Globalization;

using SourceSharp.Abi;

namespace TF2.Descent.Server.Party;

/// <summary>
/// The parties as the game's services ask them, by client slot (WO9): which party a hero here is
/// in, and which heroes here share it. A hero in no party is solo: his mates are himself alone.
/// What each seam that shares (the downed state, loot, XP, the automap, the portal) reads.
/// </summary>
public interface IPartyRoster
{
    /// <summary>The id of the party the hero in <paramref name="slot"/> is in; null when he is in none, or no hero.</summary>
    string? PartyOf(int slot);

    /// <summary>The slots of the heroes on this server in the same party as <paramref name="slot"/>, him included, by slot; him alone solo.</summary>
    IReadOnlyList<int> MatesOf(int slot);

    /// <summary>Whether the heroes in <paramref name="a"/> and <paramref name="b"/> are one party (a hero is with himself).</summary>
    bool SameParty(int a, int b);

    /// <summary>The key one party's shared state is kept under (the downed machine): the party's id, or the hero's own slot solo.</summary>
    string MachineKey(int slot);
}

/// <summary><see cref="IPartyRoster"/> over the parties and the heroes here.</summary>
[SideService(Side.Server, As = [typeof(IPartyRoster)])]
public sealed class PartyRoster(IParties parties, IPartySlots slots) : IPartyRoster
{
    public string? PartyOf(int slot) => slots.HeroOf(slot) is { } hero ? parties.Of(hero)?.Id : null;

    public IReadOnlyList<int> MatesOf(int slot)
    {
        if (PartyOf(slot) is not { } party)
            return [slot];
        var mates = new List<int> { slot };
        foreach (int other in slots.Heroes())
            if (other != slot && PartyOf(other) == party)
                mates.Add(other);
        mates.Sort();
        return mates;
    }

    public bool SameParty(int a, int b) => a == b || (PartyOf(a) is { } party && PartyOf(b) == party);

    public string MachineKey(int slot) =>
        PartyOf(slot) is { } party ? "party " + party : "solo " + slot.ToString(CultureInfo.InvariantCulture);
}
