using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Shared;
using TF2.Server.Player;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// What adds to a hero's Now beside its sheet (gear, skills, buffs): a chain
/// every package that grants stats joins (items, skills). Nothing does yet,
/// so a hero's stats are its sheet's alone. A source whose answer changes asks
/// the hero to <see cref="HeroComponent.Recompute"/>.
/// </summary>
public interface IHeroContributionSource
{
    IEnumerable<Contribution> ContributionsOf(int playerIndex, HeroSheet sheet);
}

/// <summary>
/// Told when a hero's stats are derived again (HE3): the sheet changed, a
/// contribution did (<see cref="HeroComponent.Recompute"/>), or the hero
/// arrived; and when the hero leaves. A chain: what turns the stats into the
/// game (the TF attributes) joins it. Called on the game thread, inside the
/// component's own refresh, and only when the stats were derived again.
/// </summary>
public interface IHeroStatsWatcher
{
    /// <summary><paramref name="hero"/>'s <see cref="HeroComponent.Stats"/> are new (null when the sheet went away).</summary>
    void StatsChanged(HeroComponent hero);

    /// <summary>The hero in <paramref name="playerIndex"/> has detached.</summary>
    void HeroLeft(int playerIndex);
}

/// <summary>
/// One hero in the game (plan_descent_logic HE2): a component of the core's
/// per-player composite, from the attach (the client is in) to the detach
/// (it left, or the level ended).
///
/// <list type="bullet">
/// <item>Owns the hero's <c>tf2_descent_hero</c> entity (<see cref="HeroEntity"/>):
/// made at the attach, removed at the detach, one a hero.</item>
/// <item>Reads the sheet from <see cref="HeroSheets"/> each tick and, when it
/// has changed (or a contribution has, <see cref="Recompute"/>), derives the
/// stats (<see cref="HeroRules.Stats"/>).</item>
/// <item>Owns current MANA: full when the sheet first arrives, regenerated
/// each tick by the stats' rate up to the maximum, spent by
/// <see cref="TrySpendMana"/>. Life is the player's TF health.</item>
/// </list>
/// </summary>
public sealed class HeroComponent : IPlayerComponent
{
    /// <summary>After TF2's band, in TF2 Descent's hero block.</summary>
    public const int ComponentOrder = ModStages.First + 200;

    private readonly IEngineEntities _entities;
    private readonly IEntityDirectory _directory;
    private readonly HeroSheets _sheets;
    private readonly Heroes _heroes;
    private readonly IHooks<IHeroContributionSource> _contributions;
    private readonly IHooks<IHeroStatsWatcher> _watchers;
    private readonly ITFPlayerResupply _resupply;
    private readonly ILog _log;

    private HeroSheet? _sheet;
    private HeroStats? _stats;
    private double _mana;
    private float? _lastTick;
    private bool _stale;

    public HeroComponent(IEngineEntities entities, IEntityDirectory directory, HeroSheets sheets, Heroes heroes,
                         IHooks<IHeroContributionSource> contributions, IHooks<IHeroStatsWatcher> watchers,
                         ITFPlayerResupply resupply, ILog log)
    {
        _entities = entities;
        _directory = directory;
        _sheets = sheets;
        _heroes = heroes;
        _contributions = contributions;
        _watchers = watchers;
        _resupply = resupply;
        _log = log;
    }

    public int Order => ComponentOrder;

    /// <summary>The hero's player.</summary>
    public EntityHandle Player { get; private set; } = EntityHandle.None;

    /// <summary>The hero's player index; -1 before the attach and after the detach.</summary>
    public int PlayerIndex { get; private set; } = -1;

    /// <summary>The hero's <c>tf2_descent_hero</c> entity, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle Entity { get; private set; } = EntityHandle.None;

    /// <summary>The entity's networked state, or null when there is no entity.</summary>
    public HeroNetState? State { get; private set; }

    /// <summary>The sheet as of the last tick (or change); null while a hosted join has not answered.</summary>
    public HeroSheet? Sheet => _sheet;

    /// <summary>The stats of <see cref="Sheet"/> and what contributes to it; null with no sheet.</summary>
    public HeroStats? Stats => _stats;

    /// <summary>Current mana.</summary>
    public double Mana => _mana;

    /// <summary>The rules the hero's sheet is read and checked by.</summary>
    public TF2.Descent.Rules.TF2DescentRules Rules => _sheets.Rules;

    public void Attach(EntityHandle player)
    {
        Player = player;
        PlayerIndex = _entities.GetIndex(player);
        _heroes.Enter(PlayerIndex, this);

        Entity = _entities.Create(HeroNetState.ClassName);
        if (Entity == EntityHandle.None)
        {
            _log.Warning($"[TF2Descent/Hero] no {HeroNetState.ClassName} could be made for player {PlayerIndex}\n");
            return;
        }

        _entities.SetOrigin(Entity, _entities.GetOrigin(player));
        _entities.Spawn(Entity);
        if (_directory.Get(Entity) is not HeroEntity hero)
        {
            _log.Warning($"[TF2Descent/Hero] {Entity} is not a {HeroNetState.ClassName}; player {PlayerIndex} has no hero state\n");
            return;
        }

        State = hero.State;
        State.OwnerIndex = (byte)PlayerIndex;
        Refresh();
        Publish();
    }

    public void Tick(float now)
    {
        float dt = _lastTick is { } last && now > last ? now - last : 0f;
        _lastTick = now;

        Refresh();
        if (_stats is { } stats)
            _mana = Math.Min(stats.MaxMana, _mana + stats.ManaRegenPerSecond * dt);
        Publish();
    }

    public void Detach()
    {
        if (PlayerIndex >= 0)
        {
            _heroes.Leave(PlayerIndex, this);
            int index = PlayerIndex;
            _watchers.Each(nameof(IHeroStatsWatcher.HeroLeft), w => w.HeroLeft(index));
        }
        if (Entity != EntityHandle.None && _entities.IsValid(Entity))
            _entities.Remove(Entity);

        Entity = EntityHandle.None;
        State = null;
        Downed = false;
        PlayerIndex = -1;
        Player = EntityHandle.None;
    }

    //-------------------------------------------------------------------------
    // What other server code calls
    //-------------------------------------------------------------------------

    /// <summary>Spends <paramref name="amount"/> mana when the hero has it; false, and nothing spent, when short.</summary>
    public bool TrySpendMana(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        if (_stats is null || _mana < amount)
            return false;

        _mana -= amount;
        Publish();
        return true;
    }

    /// <summary>Sets current mana, held inside 0..max (the dev command's).</summary>
    public void SetMana(double mana)
    {
        _mana = _stats is { } stats ? Math.Clamp(mana, 0, stats.MaxMana) : 0;
        Publish();
    }

    /// <summary>
    /// Life and mana to full (ruling L3: what a level-up does): the player's
    /// TF health to its maximum (an overheal above it is kept) and mana to the
    /// stats' maximum.
    /// </summary>
    public void RefillLifeAndMana()
    {
        if (Player != EntityHandle.None)
        {
            int max = _resupply.MaxHealth(Player);
            if (_entities.GetHealth(Player) < max)
                _entities.SetHealth(Player, max);
        }

        Refresh();
        if (_stats is { } stats)
            _mana = stats.MaxMana;
        Publish();
    }

    /// <summary>
    /// Changes the hero's sheet through <see cref="HeroSheets.Apply"/>: the
    /// rules' audit runs on the result, and a change that breaks it is refused
    /// and logged. On success the stats follow at once.
    /// </summary>
    public AuditResult Apply(Func<HeroSheet, HeroSheet> change)
    {
        var result = _sheets.Apply(PlayerIndex, change);
        if (result.Ok)
            Recompute();
        return result;
    }

    /// <summary>
    /// The hero is down (R26, WO6): on the ground, bleeding out. Set by the
    /// downed state (<c>HeroDowned</c>) and read by everything a downed
    /// hero may not do (skills, the belt, scrolls, his weapons).
    /// </summary>
    public bool Downed { get; private set; }

    /// <summary>The downed state onto the hero and the wire: down from <paramref name="startTick"/> to <paramref name="endTick"/>, or up (both 0).</summary>
    public void ShowDowned(bool down, int startTick, int endTick)
    {
        Downed = down;
        if (State is not { } state)
            return;
        state.Downed = down;
        state.DownedStartTick = down ? startTick : 0;
        state.DownedEndTick = down ? endTick : 0;
        if (!down)
            ShowRevive(0, 0, 0);
    }

    /// <summary>Who is reviving the hero (a player index, 0 for nobody) and the ticks the revive began and completes at.</summary>
    public void ShowRevive(int reviverIndex, int startTick, int endTick)
    {
        if (State is not { } state)
            return;
        bool on = reviverIndex > 0;
        state.ReviverIndex = (byte)Math.Clamp(on ? reviverIndex : 0, 0, 127);
        state.ReviveStartTick = on ? startTick : 0;
        state.ReviveEndTick = on ? endTick : 0;
    }

    /// <summary>The stats again, now: a contribution changed (an item equipped, a skill bought).</summary>
    public void Recompute()
    {
        _stale = true;
        Refresh();
        Publish();
    }

    //-------------------------------------------------------------------------

    /// <summary>The sheet as the store holds it; the stats again when it (or a contribution) changed.</summary>
    private void Refresh()
    {
        if (PlayerIndex < 0)
            return;

        var sheet = _sheets.Of(PlayerIndex);
        if (ReferenceEquals(sheet, _sheet) && !_stale)
            return;

        bool first = _sheet is null;
        _stale = false;
        _sheet = sheet;
        if (sheet is null)
        {
            _stats = null;
            _mana = 0;
            Changed();
            return;
        }

        var contributions = _contributions.All.SelectMany(c => c.ContributionsOf(PlayerIndex, sheet)).ToList();
        _stats = _sheets.Rules.Heroes.Stats(sheet, contributions);
        _mana = first ? _stats.MaxMana : Math.Min(_mana, _stats.MaxMana);
        WriteSheet();
        Changed();
    }

    /// <summary>The watchers hear the new stats (HE3: the TF attributes follow them).</summary>
    private void Changed() => _watchers.Each(nameof(IHeroStatsWatcher.StatsChanged), w => w.StatsChanged(this));

    /// <summary>The sheet's fields onto the wire (the writes that do not change a value cost nothing).</summary>
    private void WriteSheet()
    {
        if (State is not { } state || _sheet is not { } sheet || _stats is not { } stats)
            return;

        var heroes = _sheets.Rules.Heroes;
        state.ClassId = (byte)HeroClasses.ToTF(sheet.ClassId);
        state.Level = (byte)sheet.Level;
        state.Xp = Int(sheet.Xp);
        state.LevelXp = Int(heroes.XpAtLevel(sheet.Level));
        state.NextLevelXp = Int(sheet.Level < heroes.LevelCap ? heroes.XpAtLevel(sheet.Level + 1) : heroes.XpAtLevel(sheet.Level));
        state.LifeMax = HeroNetState.Whole(stats.MaxLife);
        state.ManaMax = HeroNetState.Whole(stats.MaxMana);
        state.AttributePoints = (ushort)Math.Clamp(sheet.AttributePoints, 0, ushort.MaxValue);
        state.SkillPoints = (ushort)Math.Clamp(sheet.SkillPoints, 0, ushort.MaxValue);
        state.Hardcore = sheet.Hardcore;
    }

    private void Publish()
    {
        if (State is { } state)
            state.Mana = HeroNetState.Whole(_mana);
    }

    private static int Int(long value) => (int)Math.Clamp(value, 0, int.MaxValue);
}
