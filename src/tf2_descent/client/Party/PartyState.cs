using SourceSharp.Abi;
using SourceSharp.Client;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Party;

/// <summary>
/// The local hero's party as the server told it (<c>party_view</c>, plan_descent_logic WO9):
/// who is in it, who leads, the invitation waiting. What the party frames, the party window and
/// the automap's party marks read. Nothing of another party reaches this client.
/// </summary>
public interface IPartyState
{
    /// <summary>Counts every change.</summary>
    int Version { get; }

    PartyViewState View { get; }

    /// <summary>Whether the player in <paramref name="playerIndex"/> is in the local hero's party (the local hero is, when he is in one).</summary>
    bool IsMember(int playerIndex);

    /// <summary>Whether the local hero leads his party.</summary>
    bool LocalLeads { get; }
}

/// <summary>
/// <see cref="IPartyState"/> over <c>party_view</c>: the newest view replaces the last. It is
/// kept across a level change (the party outlives it): the server sends it again at every
/// spawn, so a client on a new server is told that server's party, or none.
/// </summary>
[SideService(Side.Client, As = [typeof(IPartyState)])]
public sealed class PartyState(IEngineHud hud) : IPartyState, IPartyViewHook, IOrderedHook
{
    /// <summary>TF2 Descent's party block on the client (WO9).</summary>
    public int HookOrder => 1501;

    public int Version { get; private set; }

    public PartyViewState View { get; private set; } = PartyViewState.Empty();

    public void OnPartyView(PartyViewState view)
    {
        View = view;
        Version++;
    }

    public bool IsMember(int playerIndex) => playerIndex > 0 && View.Members.Any(m => m.Slot == playerIndex);

    public bool LocalLeads => View.Leader >= 0 && View.Leader < View.Members.Count && View.Members[View.Leader].Slot == hud.LocalPlayerIndex;
}

/// <summary>The party view's dispatch on the client.</summary>
[SideService(Side.Client)]
public sealed class PartyClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IPartyViewHook>();
}
