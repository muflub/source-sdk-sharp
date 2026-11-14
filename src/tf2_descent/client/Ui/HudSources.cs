using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

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
/// The party frames (<see cref="IDescentParty"/>): every hero this client
/// holds, by player index, the local one included. The name is the engine's
/// player info; the life is the local player's own health, or another
/// player's as the core's <c>player_manager</c> row carries it (the
/// scoreboard's health column, sent to every client); the maximum is the
/// hero's sheet. Distance is from the local player's origin to the other
/// player's entity, in whole units. The list is read once each time the
/// count is asked, so a frame's rows agree with its count.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentParty)])]
public sealed class PartySource(IHeroStates heroes, IEngineHud hud, IPlayerNames names,
                                IClientStandalones standalones, IEngineEntities entities) : IDescentParty
{
    private IReadOnlyList<HeroState> _heroes = [];

    public int MemberCount
    {
        get
        {
            _heroes = heroes.All();
            return _heroes.Count;
        }
    }

    public DescentPartyMember Member(int i)
    {
        var hero = _heroes[i];
        int local = hud.LocalPlayerIndex;
        bool isLocal = hero.PlayerIndex == local;
        string name = names.TryGet(hero.PlayerIndex, out var info) ? info.Name : "";
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
/// bar's fraction is measured against. The skill bar the server sends
/// (<c>skill_bar</c>) carries the charging slot and the tick it began, not
/// the charge's length, which the server resolves from the hero's sheet and
/// the skill's rank (<c>SkillRuntime.StagesReached</c>); 0 is "not known".
/// </summary>
public interface ISkillChargeLengths
{
    /// <summary>Ticks a full charge of <paramref name="skillId"/> takes; 0 when the client does not know.</summary>
    int TicksOf(string skillId);
}

/// <summary>
/// The client knows no charge length (see <see cref="ISkillChargeLengths"/>):
/// until the skill bar carries one, the cast bar shows the charging skill
/// with an empty meter.
/// </summary>
[SideService(Side.Client, As = [typeof(ISkillChargeLengths)])]
public sealed class UnknownChargeLengths : ISkillChargeLengths
{
    public int TicksOf(string skillId) => 0;
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
/// while a slot's key is held on a charge skill; the name is the slot's skill
/// id (the client has no skill names: the table is the server's rules); the
/// fraction is the ticks since the charge began over the charge's length,
/// clamped to 0..1, and 0 while the length is unknown. The wire says nothing
/// of an interrupted cast.
/// </summary>
[SideService(Side.Client, As = [typeof(IDescentCastBar)])]
public sealed class CastBarSource(SkillBarState bar, ISkillChargeLengths lengths, IClientTicks ticks) : IDescentCastBar
{
    public bool Casting => Charging is not null;

    public string SpellName => Charging ?? "";

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
