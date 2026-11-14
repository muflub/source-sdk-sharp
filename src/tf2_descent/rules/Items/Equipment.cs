using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Hero;

namespace TF2.Descent.Rules.Items;

/// <summary>An item in an equipment slot (inventory.json's slot id: primary, hat, trinket1 ...).</summary>
public sealed record EquippedItem(string Slot, ItemInstance Item);

/// <summary>"Give <paramref name="Attribute"/> = <paramref name="Value"/> to the item in <paramref name="Slot"/>": an affix TF already implements, for TF's attribute system.</summary>
public sealed record TFAttributeGrant(string Slot, string Attribute, double Value, string Affix);

/// <summary>An active item's unusual effect: the proc the server binds and the particle a client draws.</summary>
public sealed record ProcGrant(string Slot, string Effect, string Proc, double Chance, double Amount, string Particle);

/// <summary>Why an equipped item does not count.</summary>
public enum InactiveReason
{
    None,
    /// <summary>An attribute requirement is not met by Now (L2, C.3).</summary>
    Requirement,
    /// <summary>The hero's level is below the base's.</summary>
    Level,
    /// <summary>The item is not identified (D22: it cannot be equipped; one that is still counts for nothing).</summary>
    Unidentified,
    /// <summary>Durability 0 (2h): broken, it grants nothing and a weapon fires as its Stock base.</summary>
    Broken,
    /// <summary>Maximum durability 0 (L7 as amended): unusable, it grants nothing.</summary>
    Unusable,
}

/// <summary>An equipped item's state: Active, Inactive or Broken, why, and whether a weapon fires as its Stock base.</summary>
public sealed record EquippedState(string Slot, string BaseId, SourceState State, InactiveReason Reason, bool FiresAsStock);

/// <summary>
/// What the equipped items give a hero: the contributions the sheet reads (every equipped
/// item's, each with its source's state, so an inactive or broken item's count for nothing
/// through <see cref="ContributionSource.Counts"/>), the TF attributes for TF's attribute
/// system and the unusual procs (active items only), each item's state, and how many passes
/// the requirement resolution took.
/// </summary>
public sealed record EquipmentResult(
    IReadOnlyList<Contribution> Contributions,
    IReadOnlyList<TFAttributeGrant> TFAttributes,
    IReadOnlyList<ProcGrant> Procs,
    IReadOnlyList<EquippedState> States,
    int Passes)
{
    public EquippedState State(string slot) => States.First(s => s.Slot == slot);
}

/// <summary>
/// Equipped items to stats (plan_tf2_rpg 2b, 2h, C.3; rulings L2, L11). Requirements read Now,
/// and gear that grants an attribute can be what lets you wear other gear.
///
/// <para><b>Resolution.</b> The active set starts empty. Each pass computes Now from the
/// sheet, the other contributions given and the active items, then adds every equipped item
/// that is usable (identified, not broken, not unusable), at or below the hero's level, and
/// whose attribute requirements Now meets. It stops at the first pass that adds nothing. No
/// item may lower an attribute (a load error: implicits and affixes only add attributes), so
/// Now only rises as items are added and an added item stays met: the active set only grows,
/// and with n items the resolution ends within n + 1 passes. The result is the least fixed
/// point, independent of slot order: an item's own attributes never count toward its own
/// requirement. Output follows the order the items are given in.</para>
/// </summary>
public sealed class Equipment
{
    private readonly DescentContent _content;
    private readonly HeroRules _heroes;
    private readonly ItemRoller _roller;

    public Equipment(DescentContent content, HeroRules heroes, ItemRoller roller)
    {
        _content = content;
        _heroes = heroes;
        _roller = roller;
    }

    /// <summary>True when <paramref name="stats"/>' Now meets every attribute requirement of the item's base.</summary>
    public bool MeetsRequirements(ItemInstance item, HeroStats stats) =>
        _content.ItemBases[item.BaseId].Requires.All(r => stats.Now(r.Attribute) >= r.Min);

    /// <summary>Broken: gear worn to durability 0 with maximum above 0.</summary>
    public bool IsBroken(ItemInstance item) =>
        _content.ItemBases[item.BaseId].IsGear && item.MaxDurability > 0 && item.Durability <= 0;

    /// <summary>Unusable: gear whose maximum durability a field repair took to 0 (L7 as amended).</summary>
    public bool IsUnusable(ItemInstance item) => _content.ItemBases[item.BaseId].IsGear && item.MaxDurability <= 0;

    /// <summary>Why an item could never count, whatever the hero's stats; <see cref="InactiveReason.None"/> when it could.</summary>
    public InactiveReason Unusable(ItemInstance item) =>
        !item.Identified ? InactiveReason.Unidentified
        : IsUnusable(item) ? InactiveReason.Unusable
        : IsBroken(item) ? InactiveReason.Broken
        : InactiveReason.None;

    /// <summary>The equipped items resolved against the sheet (see the class summary).</summary>
    public EquipmentResult Resolve(HeroSheet sheet, IReadOnlyList<EquippedItem> equipped, IReadOnlyList<Contribution>? others = null)
    {
        var baseContributions = others ?? [];
        var active = new HashSet<int>();
        int passes = 0;
        while (true)
        {
            passes++;
            var stats = _heroes.Stats(sheet, [.. baseContributions, .. ActiveContributions(equipped, active)]);
            var added = new List<int>();
            for (int i = 0; i < equipped.Count; i++)
            {
                if (active.Contains(i)) continue;
                var item = equipped[i].Item;
                if (Unusable(item) == InactiveReason.None && sheet.Level >= _content.ItemBases[item.BaseId].Level
                    && MeetsRequirements(item, stats))
                    added.Add(i);
            }
            if (added.Count == 0) break;
            active.UnionWith(added);
        }

        var contributions = new List<Contribution>();
        var tf = new List<TFAttributeGrant>();
        var procs = new List<ProcGrant>();
        var states = new List<EquippedState>();
        for (int i = 0; i < equipped.Count; i++)
        {
            var (slot, item) = (equipped[i].Slot, equipped[i].Item);
            var itemBase = _content.ItemBases[item.BaseId];
            var reason = active.Contains(i) ? InactiveReason.None
                : Unusable(item) is var u and not InactiveReason.None ? u
                : sheet.Level < itemBase.Level ? InactiveReason.Level
                : InactiveReason.Requirement;
            var state = reason switch
            {
                InactiveReason.None => SourceState.Active,
                InactiveReason.Broken or InactiveReason.Unusable => SourceState.Broken,
                _ => SourceState.Inactive,
            };
            states.Add(new EquippedState(slot, item.BaseId, state, reason, itemBase.Kind == ItemKinds.Weapon && state != SourceState.Active));
            var source = new ContributionSource(SourceKind.Item, slot, state);
            contributions.AddRange(Contributions(item, source));
            if (state != SourceState.Active) continue;
            foreach (var ra in item.Affixes)
            {
                var a = _content.Affixes[ra.Id];
                if (a.IsTF) tf.Add(new TFAttributeGrant(slot, a.Stat, _roller.Value(item, ra), a.Id));
            }
            if (item.UnusualEffect is { } effect && _content.UnusualEffects.TryGet(effect, out var e))
                procs.Add(new ProcGrant(slot, e.Id, e.Proc, e.ProcChance, e.ProcAmount, e.Particle));
        }
        return new EquipmentResult(contributions, tf, procs, states, passes);
    }

    private IEnumerable<Contribution> ActiveContributions(IReadOnlyList<EquippedItem> equipped, HashSet<int> active)
    {
        for (int i = 0; i < equipped.Count; i++)
            if (active.Contains(i))
                foreach (var c in Contributions(equipped[i].Item, new ContributionSource(SourceKind.Item, equipped[i].Slot)))
                    yield return c;
    }

    /// <summary>
    /// The Descent stats an item gives the hero, from <paramref name="source"/>: the base's
    /// implicits and each Descent affix's value. TF affixes go to TF's attribute system instead,
    /// and the stats that act on the item itself (its wear) are not the hero's.
    /// </summary>
    public IEnumerable<Contribution> Contributions(ItemInstance item, ContributionSource source)
    {
        var local = _content.ItemSettings.ItemLocalStats;
        foreach (var s in _content.ItemBases[item.BaseId].Implicits)
            if (!local.Contains(s.Stat))
                yield return new Contribution(s.Stat, s.Amount, source);
        foreach (var ra in item.Affixes)
        {
            var a = _content.Affixes[ra.Id];
            if (!a.IsTF && !local.Contains(a.Stat))
                yield return new Contribution(a.Stat, _roller.Value(item, ra), source);
        }
    }
}
