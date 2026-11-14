using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using TF2.Descent.Client.Belt;
using TF2.Descent.Client.Content;
using TF2.Descent.Client.Party;
using TF2.Descent.Client.Skills;

namespace TF2.Descent.Client.Ui;

//=============================================================================
// The HUD's sources over live data (plan_descent_logic UI2): what the client
// knows of the local hero (its tf2_descent_hero entity, IHeroStates), the
// local player's health, every hero's player row, and the skill bar the
// server sends its owner. What the wire does not carry yet is answered by
// the empty sources in EmptyHudSources.cs, never by sample data.
//=============================================================================

/// <summary>
/// The two orbs (<see cref="IDescentVitals"/>): life is the local player's
/// own health, as the engine reports it to the HUD, over the hero's sheet
/// maximum (<c>HeroNetState.LifeMax</c>); mana and its maximum are the
/// hero's, as the server rounds them. No hero entity for the local player:
/// no hero, and the orbs draw nothing.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentVitals)])]
public sealed class VitalsSource(IHeroStates heroes, IEngineHud hud) : IDescentVitals
{
    public bool HasHero => heroes.Local() is not null;

    public int Life => heroes.Local() is null ? 0 : Math.Max(0, hud.LocalPlayerHealth);

    public int LifeMax => heroes.Local()?.LifeMax ?? 0;

    public int Mana => heroes.Local()?.Mana ?? 0;

    public int ManaMax => heroes.Local()?.ManaMax ?? 0;
}

/// <summary>
/// The XP bar (<see cref="IDescentXp"/>): the experience inside the current
/// level and the level's span, so the bar's fill is
/// <see cref="HeroState.LevelProgress"/>; at the level cap the span is 0 and
/// the bar reads full. No hero: level 0, which the bar draws as nothing.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentXp)])]
public sealed class XpSource(IHeroStates heroes) : IDescentXp
{
    public int Level => heroes.Local()?.Level ?? 0;

    public long Xp => heroes.Local() is { } h ? Math.Max(0, h.Xp - h.LevelXp) : 0;

    public long XpToNext => heroes.Local() is { } h ? Math.Max(0, h.NextLevelXp - h.LevelXp) : 0;

    public int AttributePoints => heroes.Local()?.AttributePoints ?? 0;

    public int SkillPoints => heroes.Local()?.SkillPoints ?? 0;
}

/// <summary>
/// The party frames (<see cref="IDescentParty"/>): the local hero's party as
/// the server told it (<see cref="IPartyState"/>, WO9), in the order its
/// members joined, him among them; solo, the local hero alone. A member on
/// this server is read from his hero: the name is the engine's player info;
/// the life is the local player's own health, or another player's as the
/// core's <c>player_manager</c> row carries it (the scoreboard's health
/// column, sent to every client); the maximum is the hero's sheet. Distance is
/// from the local player's origin to the other player's entity, in whole
/// units. A member who is not on this server (hosted: in town, or on another
/// level) is <see cref="DescentPartyMember.OffLevel"/>, with the name, class
/// and level the server sent and no life. Another hero who is not in the party
/// is not listed. The list is read once each time the count is asked, so a
/// frame's rows agree with its count.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentParty)])]
public sealed class PartySource(IHeroStates heroes, IEngineHud hud, IPlayerNames names,
                                IClientStandalones standalones, IEngineEntities entities, IPartyState party) : IDescentParty
{
    private readonly List<DescentPartyMember> _rows = [];

    public int MemberCount
    {
        get
        {
            _rows.Clear();
            var view = party.View;
            if (!view.InParty)
            {
                if (heroes.Local() is { } me)
                    _rows.Add(Row(me, ""));
                return _rows.Count;
            }

            int local = hud.LocalPlayerIndex;
            foreach (var m in view.Members)
            {
                _rows.Add(m.Slot > 0 && heroes.Of(m.Slot) is { } hero
                    ? Row(hero, m.Name)
                    : new DescentPartyMember(m.Name, HeroClassTokens.Of(m.ClassId), m.Level, 0, 0, false,
                                             m.Slot > 0 && m.Slot == local, false, 0, OffLevel: m.Slot == 0));
            }
            return _rows.Count;
        }
    }

    public DescentPartyMember Member(int i) => _rows[i];

    private DescentPartyMember Row(HeroState hero, string sent)
    {
        bool isLocal = hero.PlayerIndex == hud.LocalPlayerIndex;
        string name = names.TryGet(hero.PlayerIndex, out var info) && info.Name.Length > 0 ? info.Name : sent;
        return new DescentPartyMember(name, HeroClassTokens.Of(hero.ClassId), hero.Level,
                                      isLocal ? Math.Max(0, hud.LocalPlayerHealth) : HealthOf(hero.PlayerIndex), hero.LifeMax,
                                      hero.Downed, isLocal, hero.Hardcore,
                                      isLocal ? 0 : DistanceTo(hero.PlayerIndex), OffLevel: false);
    }

    /// <summary>Another player's health: the core's player resource row (0 for a dead or unknown player).</summary>
    private int HealthOf(int playerIndex)
    {
        if (!PlayerResourceNetState.InRange(playerIndex))
            return 0;
        foreach (var entity in standalones.OfClass(EntityNames.PlayerManager))
            if (entity is SourceSharp.Client.Standalone.ClientPlayerResource resource)
                return Math.Max(0, (int)resource.State.Health[playerIndex]);
        return 0;
    }

    /// <summary>Whole units from the local player to <paramref name="playerIndex"/>'s entity; 0 when either is unknown.</summary>
    private int DistanceTo(int playerIndex)
    {
        if (hud.LocalPlayerOrigin is not { } here || standalones.GetByIndex(playerIndex) is not { } other)
            return 0;
        Vector there = entities.GetOrigin(other.Handle);
        return (int)(there - here).Length;
    }
}

/// <summary>The class word a party frame and the sheet draw: a <c>#Descent_Class_*</c> token by TF2 class number.</summary>
public static class HeroClassTokens
{
    /// <summary>The token of TF2 class <paramref name="classId"/> (3 is the Soldier); "" for none.</summary>
    public static string Of(int classId) => classId switch
    {
        1 => "#Descent_Class_Scout",
        2 => "#Descent_Class_Sniper",
        3 => "#Descent_Class_Soldier",
        4 => "#Descent_Class_Demoman",
        5 => "#Descent_Class_Medic",
        6 => "#Descent_Class_Heavy",
        7 => "#Descent_Class_Pyro",
        8 => "#Descent_Class_Spy",
        9 => "#Descent_Class_Engineer",
        _ => "",
    };
}

/// <summary>
/// How long a charge skill takes to fill, in server ticks: what the cast
/// bar's fraction is measured against. The server resolves it from the hero's
/// sheet and the skill's rank and sends it on the skill bar
/// (<c>SkillBarView.ChargeTicks</c>); 0 is "not known".
/// </summary>
public interface ISkillChargeLengths
{
    /// <summary>Ticks a full charge of <paramref name="skillId"/> takes; 0 when the client does not know.</summary>
    int TicksOf(string skillId);
}

/// <summary>
/// The charge's length as the skill bar carries it (plan_descent_logic UI3):
/// the bar's <c>ChargeTicks</c> for the skill charging in its slot; 0 for any
/// other skill, or before a bar with a charge.
/// </summary>
[SideService(Side.Client, As = [typeof(ISkillChargeLengths)])]
public sealed class SkillBarChargeLengths(SkillBarState bar) : ISkillChargeLengths
{
    public int TicksOf(string skillId) =>
        bar.Bar is { ChargeSlot: > 0 } view && view.ChargeSlot <= view.Skills.Count && view.Skills[view.ChargeSlot - 1] == skillId
            ? view.ChargeTicks
            : 0;
}

/// <summary>The client's tick count: what the skill bar's server ticks are measured against.</summary>
public interface IClientTicks
{
    /// <summary>The engine's tick count now (<c>gpGlobals-&gt;tickcount</c>); 0 before the engine hands its globals over.</summary>
    int Now { get; }
}

/// <summary><see cref="IClientTicks"/> over the engine's globals.</summary>
[SideService(Side.Client, As = [typeof(IClientTicks)])]
public sealed class EngineClientTicks(IEngineEnvironment environment) : IClientTicks
{
    public int Now => environment.Globals.IsAvailable ? environment.Globals.TickCount : 0;
}

/// <summary>
/// The cast bar (<see cref="IDescentCastBar"/>) over the skill bar: casting
/// while a slot's key is held on a charge skill; the name is the skill's, from
/// the content the client embeds (its id for a skill the client does not
/// have); the fraction is the ticks since the charge began over the charge's
/// length, clamped to 0..1, and 0 while the length is unknown. The wire says
/// nothing of an interrupted cast.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCastBar)])]
public sealed class CastBarSource(SkillBarState bar, ISkillChargeLengths lengths, IClientTicks ticks, DescentCatalog catalog)
    : IDescentCastBar
{
    public bool Casting => Charging is not null;

    public string SpellName => Charging is { } id ? catalog.Skill(id)?.Name ?? id : "";

    public float Fraction
    {
        get
        {
            if (Charging is not { } id || bar.Bar is not { } view)
                return 0f;
            int length = lengths.TicksOf(id);
            if (length <= 0)
                return 0f;
            return Math.Clamp((ticks.Now - view.ChargeStartTick) / (float)length, 0f, 1f);
        }
    }

    public bool Interrupted => false;

    /// <summary>The charging slot's skill id, or null when nothing charges.</summary>
    private string? Charging
        => bar.Bar is { ChargeSlot: > 0 } view && view.ChargeSlot <= view.Skills.Count && view.Skills[view.ChargeSlot - 1].Length > 0
            ? view.Skills[view.ChargeSlot - 1]
            : null;
}

/// <summary>
/// The potion belt (<see cref="IDescentBelt"/>) from the belt the server sends
/// its owner (HE8): each slot's potion name and count, and the shared
/// cooldown, ready once the client's tick reaches the tick the server said it
/// ends. Before a belt arrives it is four empty slots, ready.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentBelt)])]
public sealed class BeltSource(IBeltState belt, IClientTicks ticks) : IDescentBelt
{
    public int SlotCount => belt.SlotCount;

    public string SlotName(int i) => belt.NameOf(i);

    public int SlotCountOf(int i) => belt.CountOf(i);

    public bool SlotReady => belt.ReadyTick == 0 || ticks.Now >= belt.ReadyTick;
}

/// <summary>
/// The gear warning (<see cref="IDescentGearWarning"/>) from the wear levels the
/// server sends its owner (HE9): the worst worn piece, its name and how much
/// of its durability is left. None while every piece is above the warning line.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentGearWarning)])]
public sealed class GearWarningSource(IGearWearState wear) : IDescentGearWarning
{
    public bool Present => wear.Warning is not null;

    public string ItemName => wear.Warning?.Name ?? "";

    public float Fraction =>
        wear.Warning is { MaxDurability: > 0 } worst ? Math.Clamp(worst.Durability / (float)worst.MaxDurability, 0f, 1f) : 0f;
}
