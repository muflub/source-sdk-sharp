using SourceSharp.Abi;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The HUD sources the wire cannot answer yet (plan_descent_logic UI2). Each
// answers "nothing to show", never sample data, and names what the server
// must send for it to go live; the package that sends it replaces the body.
//=============================================================================

/// <summary>
/// The potion belt (<see cref="IDescentBelt"/>), empty: four slots holding
/// nothing, ready. Live when the server sends the hero's belt to its owner
/// (HE8): each slot's item name and uses left, and the shared cooldown's end
/// tick, as the skill bar sends its slots.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentBelt)])]
public sealed class BeltSource : IDescentBelt
{
    /// <summary>The belt's fixed slots (D16).</summary>
    public const int Slots = 4;

    public int SlotCount => Slots;

    public string SlotName(int i) => "";

    public int SlotCountOf(int i) => 0;

    public bool SlotReady => true;
}

/// <summary>
/// The boss bar (<see cref="IDescentBoss"/>), absent. Live when the boss body
/// (HO13) networks its name, life, maximum life and phase to the heroes on
/// its arena.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentBoss)])]
public sealed class BossSource : IDescentBoss
{
    public bool Present => false;

    public string Name => "";

    public int Life => 0;

    public int LifeMax => 0;

    public int PhaseCount => 0;

    public int Phase => 0;
}

/// <summary>
/// The gear warning (<see cref="IDescentGearWarning"/>), none. Live when the
/// owner's client learns its equipped items' durability (HE9 sends the wear
/// deltas; the item views of E4 carry durability and maximum): the most worn
/// equipped item under the warning line, its name and its fraction left.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentGearWarning)])]
public sealed class GearWarningSource : IDescentGearWarning
{
    public bool Present => false;

    public string ItemName => "";

    public float Fraction => 0f;
}

/// <summary>
/// The automap and the HUD's corner minimap (<see cref="IDescentAutomap"/>),
/// nothing revealed. Live when the server sends each room's outline, its
/// doors and markers as the hero reveals it, and the hero's storey (UI5,
/// WO1, RU12).
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentAutomap)])]
public sealed class AutomapSource : IDescentAutomap
{
    public int RoomCount => 0;

    public float RoomX(int room) => 0f;

    public float RoomY(int room) => 0f;

    public float RoomW(int room) => 0f;

    public float RoomH(int room) => 0f;

    public int RoomStorey(int room) => 0;

    public bool RoomCurrentStorey(int room) => false;

    public int DoorStubCount => 0;

    public float DoorStubX(int i) => 0f;

    public float DoorStubY(int i) => 0f;

    public float DoorStubW(int i) => 0f;

    public float DoorStubH(int i) => 0f;

    public int MarkerCount => 0;

    public DescentMarker Marker(int i) => default;

    public int StoreyCount => 0;

    public int HeroStorey => 0;

    public float HeroX => 0f;

    public float HeroY => 0f;

    public float HeroHeadingDeg => 0f;

    public int FocusStorey => 0;
}

/// <summary>The automap's viewer prefs (<see cref="IAutomapPrefs"/>): room labels start off.</summary>
[SideService(Side.Client, As = [typeof(IAutomapPrefs)])]
public sealed class AutomapPrefsSource : IAutomapPrefs
{
    public bool ShowLabelsByDefault => false;
}

/// <summary>Room names on the automap (<see cref="IAutomapRoomLabels"/>), none. Live with the level's room names (WO1).</summary>
[SideService(Side.Client, As = [typeof(IAutomapRoomLabels)])]
public sealed class AutomapRoomLabelsSource : IAutomapRoomLabels
{
    public string RoomLabel(int room) => "";
}

/// <summary>
/// The downed overlay (<see cref="IDescentDowned"/>), never down. The hero
/// state carries <c>Downed</c> and <c>DownedEndTick</c>, which nothing sets
/// yet; live with the downed package (WO6, UI7): the bleed-out's end and
/// total, the reviver's index and progress, and the nearest party member.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentDowned)])]
public sealed class DownedSource : IDescentDowned
{
    public bool Downed => false;

    public float BleedOutSecondsLeft => 0f;

    public float BleedOutTotal => 0f;

    public bool BeingRevived => false;

    public string ReviverName => "";

    public float ReviveFraction => 0f;

    public string NearestMemberName => "";

    public int NearestMemberDistance => 0;
}

/// <summary>
/// The party window's actions (<see cref="IDescentPartyActions"/>): no code,
/// and inviting, leaving and joining do nothing. Live with the host's parties
/// (WO9): client commands the server answers.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentPartyActions)])]
public sealed class PartyActions : IDescentPartyActions
{
    public string PartyCode => "";

    public void Invite() { }

    public void Leave() { }

    public void Join(string code) { }
}

/// <summary>
/// The hover popup every subject screen anchors (<see cref="IDescentHintTip"/>),
/// inactive. Each screen's own hover source answers its rows once its package
/// builds the hint's content from the rules (UI3, UI4).
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentHintTip)])]
public sealed class HintTipSource : InactiveHint;
