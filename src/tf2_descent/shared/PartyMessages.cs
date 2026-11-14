using SourceSharp.Abi;

// Compiled into both TF2.Descent.Server and TF2.Descent.Client, as Messages.cs.

namespace TF2.Descent.Shared;

//=============================================================================
// THE PARTY ON THE WIRE (plan_descent_logic WO9). Each hero's client is told
// his own party, and nobody else's: who is in it (each member's client slot,
// 0 for a member who is not on this server, with the name, class and level
// the frames show for him), who leads, and the newest invitation he has not
// answered. It is sent to a hero whenever his party or his invitations
// change, and again at each spawn. One message fits 255 bytes (a fact).
//=============================================================================

/// <summary>One member as the client is told him: his slot (0 off this server), name, TF2 class number and level.</summary>
public readonly record struct PartyMemberView(int Slot, string Name, int ClassId, int Level);

/// <summary>A hero's party as his client holds it: the members (him among them), the leader's place in the list, and the invitation waiting.</summary>
/// <param name="Leader">The leader's place in <paramref name="Members"/>; -1 for no party.</param>
/// <param name="InviterSlot">The slot of the hero whose invitation waits; 0 for none.</param>
public sealed record PartyViewState(IReadOnlyList<PartyMemberView> Members, int Leader, int InviterSlot, string InviterName)
{
    /// <summary>No party and no invitation: what a client holds before it is told.</summary>
    public static PartyViewState Empty() => new([], -1, 0, "");

    public bool InParty => Members.Count > 0;

    public bool Invited => InviterSlot > 0 || InviterName.Length > 0;
}

public interface IPartyViewHook
{
    void OnPartyView(PartyViewState view);
}

/// <summary>A hero's party (<c>party_view</c>).</summary>
public partial struct PartyView
{
    /// <summary>The most members a view carries: the rules' party size.</summary>
    public const int MaxMembers = 4;

    /// <summary>The longest name a member or an inviter carries.</summary>
    public const int MaxName = 24;

    /// <summary>The leader's place in the members; 255 for no party.</summary>
    public partial byte Leader { get; set; }

    [UserMessageArray(MaxCount = MaxMembers)]
    public partial byte[] Slot { get; set; }

    [UserMessageArray(MaxCount = MaxMembers)]
    public partial byte[] ClassId { get; set; }

    [UserMessageArray(MaxCount = MaxMembers)]
    public partial byte[] Level { get; set; }

    /// <summary>The members' names, each cut to <see cref="MaxName"/>, one a line.</summary>
    [UserMessageString(MaxLength = MaxMembers * (MaxName + 1))]
    public partial string Names { get; set; }

    /// <summary>The slot of the hero whose invitation waits; 0 for none (or an inviter not on this server).</summary>
    public partial byte InviterSlot { get; set; }

    [UserMessageString(MaxLength = MaxName)]
    public partial string InviterName { get; set; }

    /// <summary>A name as the wire carries it: cut to <see cref="MaxName"/>, with no line breaks.</summary>
    public static string Cut(string name)
    {
        string one = name.Replace('\n', ' ').Replace('\r', ' ');
        return one.Length > MaxName ? one[..MaxName] : one;
    }

    public PartyViewState View()
    {
        byte[] slots = Slot ?? [], classes = ClassId ?? [], levels = Level ?? [];
        string[] names = (Names ?? "").Split('\n');
        int count = Math.Min(slots.Length, Math.Min(classes.Length, levels.Length));
        var members = new PartyMemberView[count];
        for (int i = 0; i < count; i++)
            members[i] = new PartyMemberView(slots[i], i < names.Length ? names[i] : "", classes[i], levels[i]);
        int leader = Leader < count ? Leader : -1;
        return new PartyViewState(members, leader, InviterSlot, InviterName ?? "");
    }
}

[UserMessage(Name)]
public sealed class PartyViewMessage(IHooks<IPartyViewHook>? hooks) : IUserMessage<PartyView>
{
    public PartyViewMessage() : this(null) { }

    public const string Name = "party_view";

    public void OnReceived(PartyView message)
    {
        var view = message.View();
        hooks?.Each(nameof(IPartyViewHook.OnPartyView), h => h.OnPartyView(view));
    }
}
