using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Hero;
using TF2.Descent.Rules.Skills;
using TF2.Descent.Server.Hero;
using TF2.Descent.Shared;

namespace TF2.Descent.Server.Skills;

/// <summary>
/// One cast as the binders carry it out: the hero, the skill as the rules
/// resolved it at the hero's rank, the charge stages it was released at, the
/// tick it went off, and the effects its hits carry to what they hit (a
/// debuff with no radius beside a projectile: EMP Charge's stun).
/// </summary>
public sealed record SkillCast(int PlayerIndex, EntityHandle Hero, SkillDef Def, ResolvedSkill Skill, int Stages, int Tick,
                               IReadOnlyList<ResolvedEffect> OnHit)
{
    public string SkillId => Def.Id;

    /// <summary>The weapon name the cast's damage is dealt as.</summary>
    public string WeaponName => SkillHits.WeaponNameOf(Def.Id);
}

/// <summary>A charge held on a slot: since which tick.</summary>
public readonly record struct SkillCharge(int Slot, string SkillId, int StartTick);

/// <summary>
/// Something a cast does later: a shell landing at <paramref name="At"/> after
/// its delay, the <paramref name="Index"/>th rocket of a volley, a heal's next
/// share of <paramref name="Amount"/> on <paramref name="Target"/>.
/// </summary>
public sealed record SkillPending(int Tick, SkillCast Cast, ResolvedEffect Effect, int Index, int Of, Vector At,
                                  EntityHandle Target = default, float Amount = 0f);

/// <summary>
/// A hero's casting state between ticks (plan_descent_logic HE6): the rules'
/// <see cref="CasterState"/> (mana, the clock in seconds, cooldowns, Orders,
/// toggles, BlastJumping) beside what the runtime keeps in SERVER TICKS: the
/// tick each skill is ready again and each Order ends, a held charge, work
/// still to do, and what each trigger and notice last saw.
/// </summary>
public sealed class SkillCaster(int playerIndex, EntityHandle player)
{
    public int PlayerIndex { get; } = playerIndex;

    public EntityHandle Player { get; } = player;

    /// <summary>The rules' state; <see cref="CasterState.Now"/> is the tick times the interval.</summary>
    public CasterState State { get; set; } = new();

    /// <summary>The tick each skill can be cast again (a skill with no cooldown is absent).</summary>
    public Dictionary<string, int> ReadyTick { get; } = new(StringComparer.Ordinal);

    /// <summary>The tick each Order in force ends.</summary>
    public Dictionary<string, int> OrderEndTick { get; } = new(StringComparer.Ordinal);

    public SkillCharge? Charging { get; set; }

    public List<SkillPending> Pending { get; } = [];

    /// <summary>The last cast of each skill: what its hits carry.</summary>
    public Dictionary<string, SkillCast> LastCast { get; } = new(StringComparer.Ordinal);

    /// <summary>The tick each (skill, refusal) was last told to the player.</summary>
    public Dictionary<string, int> Told { get; } = new(StringComparer.Ordinal);

    /// <summary>The tick each triggered effect last fired, for its per-second cap.</summary>
    public Dictionary<string, List<int>> TriggerTicks { get; } = new(StringComparer.Ordinal);

    public bool BlastJumping { get; set; }

    /// <summary>The weapon family in the hands as last seen; null for none or a weapon in no family.</summary>
    public string? Family { get; set; }

    public bool FamilySeen { get; set; }

    /// <summary>The tick the hero last took damage.</summary>
    public int LastHurtTick { get; set; }

    /// <summary>A dash in flight: the effects its landing fires, armed once the hero has left the ground.</summary>
    public SkillCast? Dashing { get; set; }

    public bool DashLeftGround { get; set; }

    /// <summary>The bar as last sent to the owner.</summary>
    public string? SentBar { get; set; }

    /// <summary>What the held triggers and the toggles were last counted at (BlastJumping, life missing, rested, toggles on).</summary>
    public string HeldKey { get; set; } = "";

    /// <summary>The stats the held triggers and the toggles grant now.</summary>
    public IReadOnlyList<Contribution> HeldStats { get; set; } = [];
}

/// <summary>
/// Every hero's <see cref="SkillCaster"/> by player index, from the attach to the detach. The
/// toggles a hero has on when his caster leaves are kept for his slot and his next caster starts
/// with them: a level change detaches every player and attaches him again on the next level, and a
/// toggle (Lamplighter, the user's ruling L26) stays as he left it. His client leaving the server
/// forgets them (<see cref="OnClientDisconnected"/>, after the core's detach).
/// </summary>
[SideService(Side.Server)]
public sealed class SkillCasters : IClientLifecycle
{
    /// <summary>In TF2 Descent's skills block, after the core's player components (which detach first).</summary>
    public const int Order = ModStages.First + 650;

    public int ClientLifecycleOrder => Order;

    private readonly Dictionary<int, SkillCaster> _byIndex = new();
    private readonly Dictionary<int, ImmutableSortedSet<string>> _kept = new();

    public SkillCaster? Of(int playerIndex) => _byIndex.GetValueOrDefault(playerIndex);

    public IReadOnlyCollection<SkillCaster> All => _byIndex.Values;

    /// <summary>The toggles kept for <paramref name="playerIndex"/> while he has no caster (none: empty).</summary>
    public IReadOnlyCollection<string> KeptToggles(int playerIndex) => _kept.TryGetValue(playerIndex, out var t) ? t : [];

    public SkillCaster Enter(int playerIndex, EntityHandle player)
    {
        var caster = new SkillCaster(playerIndex, player);
        if (_kept.Remove(playerIndex, out var toggles))
            caster.State = caster.State with { Toggles = toggles };
        _byIndex[playerIndex] = caster;
        return caster;
    }

    public void Leave(int playerIndex, SkillCaster caster)
    {
        if (!_byIndex.TryGetValue(playerIndex, out var held) || !ReferenceEquals(held, caster))
            return;
        _byIndex.Remove(playerIndex);
        if (caster.State.Toggles.Count > 0)
            _kept[playerIndex] = caster.State.Toggles;
    }

    public void OnClientDisconnected(int entIndex) => _kept.Remove(entIndex);
}

/// <summary>
/// Which skill is in which of a hero's <see cref="SkillBar.Slots"/> slots: the
/// sheet's skill bar (<see cref="HeroSheet.SkillSlots"/>, sheet schema 3), read
/// from the hero's sheet and changed through the hero's audited apply
/// (<see cref="HeroComponent.Apply"/>, or <see cref="HeroSheets.Apply"/> for a
/// sheet with no hero in the world), so it lives as long as the sheet does:
/// unhosted through respawns, level changes and reconnects, hosted in the
/// checkpoints the host keeps. A respec empties it (the rules' respec).
/// </summary>
[SideService(Side.Server)]
public sealed class SkillSlotStore(Heroes heroes, HeroSheets sheets)
{
    /// <summary>The slots of the hero in <paramref name="playerIndex"/>, slot 1 first; empty slots null (all null with no sheet).</summary>
    public IReadOnlyList<string?> Of(int playerIndex)
    {
        var bar = sheets.Of(playerIndex)?.SkillSlots ?? [];
        var slots = new string?[SkillBar.Slots];
        for (int i = 0; i < slots.Length && i < bar.Length; i++)
            slots[i] = bar[i].Length > 0 ? bar[i] : null;
        return slots;
    }

    /// <summary>The skill in <paramref name="slot"/> (1-based), or null.</summary>
    public string? In(int playerIndex, int slot) =>
        slot is >= 1 and <= SkillBar.Slots ? Of(playerIndex)[slot - 1] : null;

    /// <summary>
    /// Puts <paramref name="skillId"/> in <paramref name="slot"/> (null empties it) on the
    /// hero's sheet; a skill already in another slot leaves it. The sheet's audit decides:
    /// a refusal leaves the bar as it was.
    /// </summary>
    public AuditResult Set(int playerIndex, int slot, string? skillId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slot, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, SkillBar.Slots);
        HeroSheet Change(HeroSheet sheet) => sheets.Rules.Heroes.WithSlot(sheet, slot, skillId);
        return heroes.Of(playerIndex) is { } hero ? hero.Apply(Change) : sheets.Apply(playerIndex, Change);
    }
}

/// <summary>
/// The hero's skills in the per-player composite: a <see cref="SkillCaster"/>
/// attached with the player and ticked every server frame, after the hero
/// component (its mana and stats are this tick's).
/// </summary>
public sealed class SkillComponent(SkillRuntime runtime) : IPlayerComponent
{
    private int _index = -1;

    public int Order => 0;

    public void Attach(EntityHandle player)
    {
        _index = player.EntryIndex;
        runtime.Attach(_index, player);
    }

    public void Tick(float now) => runtime.Tick(_index);

    public void Detach()
    {
        if (_index >= 0)
            runtime.Detach(_index);
        _index = -1;
    }
}

/// <summary>A <see cref="SkillComponent"/> for each player who is a hero (the Descent mode is on, and no bot).</summary>
[SideService(Side.Server)]
public sealed class SkillComponents(TF2.Descent.Server.Mode.IDescentModeState mode, IBotPlayers bots, SkillRuntime runtime)
    : IPlayerComponentSource, IOrderedHook
{
    /// <summary>After the hero's components (HeroComponents, 1,000,200), in TF2 Descent's skills block.</summary>
    public const int Order = ModStages.First + 600;

    public int HookOrder => Order;

    public IEnumerable<IPlayerComponent> Create(EntityHandle player)
    {
        if (!mode.Active || bots.IsBot(player))
            yield break;

        yield return new SkillComponent(runtime);
    }
}
