namespace TF2.Descent.Client.Ui;

// THE NARROW SOURCES ONE SCREEN READS: each was declared beside its screen by
// the lane that built it (CraftBench, SkillTree, Party, Inventory, AutoMap,
// AutomapCanvas) and moved here with the rest of the contract
// (plan_descent_logic UI1), so the game's sources and the held screen
// assembly name one type.

/// <summary>
/// What the bench is short of, per stack (view-model-built by the wiring; the
/// plan's <see cref="IDescentCraft"/> carries no per-metal deficiency, only
/// <c>CanCraft</c>). Optional: unwired, the bench just says "not enough
/// metal" instead of naming the stacks.
/// </summary>
public interface IDescentCraftShortage
{
    int ScrapShort { get; }
    int ReclaimedShort { get; }
    int RefinedShort { get; }
}

/// <summary>
/// A town window's status line (plan_descent_logic UI4): the answer to the hero's last verb at
/// this window, as the server told it (a refusal's reason, or what a buy, a repair or a craft
/// did), drawn inside the window because a HUD notice hides under an open one. An empty token
/// draws nothing.
/// </summary>
public interface IDescentTownStatus
{
    string StatusToken { get; }                   // "#Descent_Town_not_enough_australium"; "" for none
    string StatusData { get; }                    // ui.Data: the item's name, a price, an unworded code
    bool StatusError { get; }                     // a refusal (Danger), else what was done (Positive)
}

/// <summary>
/// What the vendor window tells the game beyond <see cref="IDescentVendorActions"/> (UI4): the
/// buy-back tab's own action (its rows are the sold list, not the stock), and the item the
/// service stance has picked, which <see cref="IDescentVendor.ServiceFee"/> then prices.
/// </summary>
public interface IDescentVendorPicks
{
    void BuyBack(int soldIndex);
    void PickService(int itemIndex);
}

/// <summary>Which group heading a Mercenary card sits under; the tab answers per group index.
/// The shared <see cref="IDescentSkillTab"/> has no group titles, so the screen carries this
/// narrow extension itself (flagged for the shared interface).</summary>
public interface IDescentSkillGroups
{
    string GroupTitleToken(int group);
}

/// <summary>Which tab the player was last on (host state in the game, sample-fixed in previews).
/// Not on <see cref="IDescentSkills"/>, so the screen carries this narrow extension itself.</summary>
public interface IDescentSkillTabSelection
{
    int SelectedTab { get; }
}

/// <summary>The skill window's tab row reports the tab clicked (plan_descent_logic UI3); the selection answers it next frame.</summary>
public interface IDescentSkillTabActions
{
    void SelectTab(int tab);
}

/// <summary>
/// What the party window shows beside the members (plan_descent_logic WO9):
/// which member leads (a place in <see cref="IDescentParty"/>'s list, -1 for no
/// party), whether the local hero does, the hero whose invitation waits
/// (ui.Data; empty for none), whether the local hero may invite (in no party,
/// or leading one with room), and the heroes on this server he may
/// invite (ui.Data names).
/// </summary>
public interface IDescentPartyRoster
{
    int LeaderIndex { get; }
    bool LocalLeads { get; }
    string InviterName { get; }
    bool CanInvite { get; }
    int CandidateCount { get; }
    string CandidateName(int i);
}

/// <summary>
/// What the party window asks of the server (docs/plans/descent_ui.md §4;
/// WO9): each a client verb the server decides and answers with a notice.
/// <see cref="Invite"/> takes a place in <see cref="IDescentPartyRoster"/>'s
/// candidates, <see cref="Kick"/> a place in <see cref="IDescentParty"/>'s
/// members. The host has no party codes (SDK 2.6.0), so there is no code join.
/// </summary>
public interface IDescentPartyActions
{
    void Invite(int candidate);
    void Accept();
    void Decline();
    void Leave();
    void Kick(int member);
}

/// <summary>The Australium the character carries (D12). The vendor carries its
/// own for vendors, and §4 has no shared purse interface yet.</summary>
public interface IDescentInvPurse
{
    long Australium { get; }
}

/// <summary>The automap's viewer prefs: the caller's data says whether room
/// labels start on (the screen owns the toggle after that).</summary>
public interface IAutomapPrefs
{
    bool ShowLabelsByDefault { get; }
}

/// <summary>Room names the automap canvas may draw (supplied by the caller — the
/// canvas itself reads only the map shape).</summary>
public interface IAutomapRoomLabels
{
    string RoomLabel(int room);
}

/// <summary>
/// The hero's six skill-bar slots as the server last sent them (HE6's skill bar): what the
/// HUD's skill strip and the skill window's bar row draw. Slot <c>i</c> is key <c>4 + i</c>.
/// </summary>
public interface IDescentSkillSlots
{
    int SlotCount { get; }

    /// <summary>The slot's skill as its #name token; "" for an empty slot.</summary>
    string SlotNameToken(int slot);

    /// <summary>The share of the slot's cooldown still to run: 0 ready, 1 just cast.</summary>
    float SlotCooldown(int slot);

    /// <summary>The slot's key is held on a charge skill.</summary>
    bool SlotCharging(int slot);
}

/// <summary>Putting a learned castable skill (a card's id) into a skill-bar slot (0-based); the server decides and tells a refusal.</summary>
public interface IDescentSkillSlotActions
{
    void Assign(int slot, int skillId);
}

/// <summary>Dropping the item in a backpack cell at the hero's feet; the server decides and tells a refusal.</summary>
public interface IDescentItemDrop
{
    void Drop(int cell);
}

/// <summary>
/// The status line an open Descent window shows (the character sheet, the skill tree, the
/// inventory): the last notice the server sent (a refusal's reason, a recovery), for a few
/// seconds, so it is not lost under the window as the HUD's line is.
/// </summary>
public interface IDescentWindowStatus
{
    bool Showing { get; }

    /// <summary>The notice as it reads (localised): ui.Data.</summary>
    string Text { get; }
}
