extern alias client;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using PartyMemberView = client::TF2.Descent.Shared.PartyMemberView;
using PartyState = client::TF2.Descent.Client.Party.PartyState;
using PartyViewState = client::TF2.Descent.Shared.PartyViewState;

namespace TF2.Descent.Tests.Ui;

/// <summary>The client's party state for the UI facts: what <c>party_view</c> told it.</summary>
internal static class ClientParties
{
    /// <summary>No party: what a client holds before it is told, or solo.</summary>
    public static PartyState Solo() => new(new ClientEngine { Strict = false });

    /// <summary>A party of these slots, the first leading; a slot of 0 is a member off this server.</summary>
    public static PartyState Of(int localSlot, params (int Slot, string Name)[] members)
    {
        var state = new PartyState(new ClientEngine { Strict = false }.Returns("Hud.LocalPlayerIndex", localSlot));
        state.OnPartyView(new PartyViewState([.. members.Select(m => new PartyMemberView(m.Slot, m.Name, 3, 5))], 0, 0, ""));
        return state;
    }
}
