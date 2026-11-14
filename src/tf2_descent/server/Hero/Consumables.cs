using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Rules.Spells;
using TF2.Descent.Server.Items;

namespace TF2.Descent.Server.Hero;

/// <summary>A town portal's answer: opened, or why not.</summary>
public readonly record struct PortalAnswer(bool Opened, string? Why);

/// <summary>
/// Opens a town portal for a hero (ruling L10: an Engineer teleporter; WO4 builds it). A
/// Teleporter Blueprint scroll is used up only when the portal opened.
/// </summary>
public interface ITownPortal
{
    PortalAnswer Open(int playerIndex);
}

/// <summary>The stand-in until WO4: no portal opens, and the scroll is kept. WO4 replaces this registration.</summary>
[SideService(Side.Server, As = [typeof(ITownPortal)])]
public sealed class NoTownPortal : ITownPortal
{
    public const string Why = "no portal yet";

    public PortalAnswer Open(int playerIndex) => new(false, Why);
}

/// <summary>Why a use was refused (beside the rules' and the ledger's own words).</summary>
public static class UseRefusals
{
    public const string NotUsable = "not_usable";
    public const string NeedsTarget = "needs_target";
    public const string Portal = "portal";
}

/// <summary>A use of a backpack item: whether it was used, why not, and what it did.</summary>
public readonly record struct ItemUse(bool Ok, string? Refusal, string? Base = null, string? Did = null);

/// <summary>
/// SCROLLS AND TOMES used from the backpack (plan_descent_logic HE8; plan_tf2_rpg 2e, R27),
/// the client's <c>tf2_descent_use &lt;backpack slot&gt; [target slot]</c>. The rules' belt
/// holds potions only (<see cref="TF2.Descent.Rules.Inventory.InventoryRules.ToBelt"/>), so a
/// scroll is used where it is carried.
/// <list type="bullet">
/// <item><b>Identify</b> (the Dossier): the economy's identify recipe through the ledger over
/// the target and the Dossier: one Dossier is used, the target identified (RU9).</item>
/// <item><b>Town portal</b> (the Teleporter Blueprint): <see cref="ITownPortal.Open"/>; one is
/// used only when a portal opened, else the hero is told why and keeps it.</item>
/// <item><b>A tome</b>: <see cref="SpellRules.ReadTome"/> over the hero's sheet. A tome of a
/// spell at its highest rank is refused and kept (the sixth); else one is used through the
/// ledger, and then the sheet takes the rank through its audited change.</item>
/// </list>
/// Only a living hero uses anything. A refusal is told to the hero and logged.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroConsumables(TF2DescentRules rules, Heroes heroes, HeroKeys keys, IItemLedger ledger, ITownPortal portal,
                                    IBeltWorld world, ClientMessages messages, ILog log)
{
    public async Task<ItemUse> Use(int playerIndex, int slot, int target = -1)
    {
        if (heroes.Of(playerIndex) is not { Sheet: { } sheet } hero || keys.Of(playerIndex) is not { } key
            || ledger.Find(key) is not { } held)
            return Refuse(playerIndex, slot, BeltRefusals.NoHero);
        if (!world.IsAlive(hero.Player))
            return Refuse(playerIndex, slot, BeltRefusals.Dead);
        if (!held.Inventory.Backpack.TryGetValue(slot, out var item))
            return Refuse(playerIndex, slot, BeltRefusals.Empty);

        string baseId = item.Item.BaseId;
        var consumables = rules.Content.Consumables;
        if (consumables.Scroll(baseId) is { } scroll)
        {
            if (scroll.Use == ScrollUses.Identify)
            {
                if (target < 0 || target == slot || !held.Inventory.Backpack.ContainsKey(target))
                    return Refuse(playerIndex, slot, UseRefusals.NeedsTarget, baseId);
                var identified = await ledger.Craft(key, rules.Content.EconomySettings.IdentifyRecipe, [target, slot]);
                return identified.Ok
                    ? Done(playerIndex, slot, baseId, $"identified backpack {target}")
                    : Refuse(playerIndex, slot, identified.Refusal!, baseId);
            }

            var opened = portal.Open(playerIndex);
            if (!opened.Opened)
                return Refuse(playerIndex, slot, UseRefusals.Portal, baseId, opened.Why);
            var used = await ledger.Consume(key, slot);
            return used.Ok ? Done(playerIndex, slot, baseId, "opened a town portal") : Refuse(playerIndex, slot, used.Refusal!, baseId);
        }

        if (consumables.Tome(baseId) is { } tome)
        {
            var read = rules.Spells.ReadTome(sheet, tome.Spell);
            if (!read.Ok)
                return Refuse(playerIndex, slot, read.Refusal == TomeRefusal.AtMaxRank ? "at_max_rank" : "unknown_spell", baseId,
                              $"{rules.Spells.Find(tome.Spell)?.Name ?? tome.Spell} is at its highest rank: the tome is kept");
            var used = await ledger.Consume(key, slot);
            if (!used.Ok)
                return Refuse(playerIndex, slot, used.Refusal!, baseId);
            var audit = hero.Apply(s => rules.Spells.ReadTome(s, tome.Spell).Sheet);
            if (!audit.Ok)
                log.Warning($"[TF2Descent/Belt] {playerIndex} read {baseId}, used up, but the sheet refused the rank: {audit.Rule}\n");
            return Done(playerIndex, slot, baseId, $"{tome.Spell} rank {SpellRules.Rank(hero.Sheet!, tome.Spell)}");
        }

        return Refuse(playerIndex, slot, UseRefusals.NotUsable, baseId);
    }

    private ItemUse Done(int playerIndex, int slot, string baseId, string did)
    {
        log.Msg($"[TF2Descent/Belt] {playerIndex} used {baseId} from backpack {slot}: {did}\n");
        return new ItemUse(true, null, baseId, did);
    }

    private ItemUse Refuse(int playerIndex, int slot, string why, string? baseId = null, string? words = null)
    {
        log.Msg($"[TF2Descent/Belt] {playerIndex} use of backpack {slot}{(baseId is null ? "" : " (" + baseId + ")")} refused: {why}" +
                $"{(words is null ? "" : " (" + words + ")")}\n");
        if (why != BeltRefusals.NoHero)
            messages.Notice(playerIndex, words ?? $"Cannot use that: {why}");
        return new ItemUse(false, why, baseId, words);
    }
}
