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
/// What the party window asks of the server (docs/plans/descent_ui.md §4 names
/// <c>IDescentPartyActions</c>; the shared sources file does not define it, so
/// the party lane owned the narrow form).
/// <see cref="PartyCode"/> is the code the field shows (what the player typed,
/// once keyboard editing lands with input in U3); <see cref="Join"/> hands it
/// to the server.
/// </summary>
public interface IDescentPartyActions
{
    string PartyCode { get; }
    void Invite();
    void Leave();
    void Join(string code);
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
