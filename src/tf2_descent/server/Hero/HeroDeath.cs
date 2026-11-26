using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Death;
using TF2.Descent.Server.Items;
using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// WHAT A HERO'S DEATH COSTS (plan_descent_logic WO6, WO5; plan_tf2_rpg 2g,
/// D6, D7, D21), whatever killed him: a bleed-out, a party wipe, a solo death,
/// a suicide.
///
/// <list type="number">
/// <item>The downed state hears it (<see cref="HeroDowned.Died"/>): a death
/// it did not decide makes him dead there, and the party falls with nobody
/// left up.</item>
/// <item>THE SHEET: <see cref="DeathRules.AfterDeath"/> through the hero's
/// audited change (<see cref="HeroComponent.Apply"/>): a hardcore hero is
/// fallen; a softcore sheet is unchanged. A death costs no XP (the rules
/// take none) and no durability (D21).</item>
/// <item>THE CORPSE: what the rules drop (<see cref="DeathRules.Die"/>: every
/// item, D6; his Australium stays with him, plan L26) goes to his corpse through the ledger
/// (<see cref="IItemLedger.RecordDeath"/>), unhosted; hosted,
/// <c>DeathService</c> records the death with the host, as it did. The
/// corpse's entity stands where he fell (<see cref="HeroCorpses.Place"/>).</item>
/// <item>THE WEAR his weapons and armour gathered below a point (HE9's batching) is
/// written first (<see cref="Gear.HeroWear.FlushHero"/>), queued on the ledger before the
/// death moves the items to the corpse, so the items keep it (found by the wear lane:
/// the death used to drop it).</item>
/// <item>THE RESPAWN: the mode's (<c>HeroRespawn</c>) at the level's
/// arrival, unhosted (the town needs the host's hop); a fallen hero is held
/// dead (<see cref="IRespawnGate"/>).</item>
/// </list>
/// </summary>
[SideService(Side.Server)]
public sealed class HeroDeathCosts(IDescentModeState mode, Heroes heroes, HeroKeys keys, IItemLedger ledger, TF2DescentRules rules,
                                   HeroDowned downed, HeroCorpses corpses, Gear.HeroWear wear, ILog log)
    : IPlayerDeathHook, IRespawnGate, IOrderedHook
{
    /// <summary>In TF2 Descent's downed block.</summary>
    public const int Order = ModStages.First + 952;

    public int HookOrder => Order;

    /// <summary>The last death's costs, as the log says them: for the dev print and the facts.</summary>
    public string LastCosts { get; private set; } = "";

    public void OnPlayerDied(PlayerDeath death)
    {
        if (!mode.Active || heroes.Of(death.EntIndex) is not { } hero)
            return;

        downed.Died(death.EntIndex);

        var sheetBefore = hero.Sheet;
        var audit = hero.Apply(DeathRules.AfterDeath);
        var sheet = hero.Sheet ?? sheetBefore;
        bool hardcore = sheet?.Hardcore == true;

        string? key = keys.Of(death.EntIndex);
        var held = key is null ? null : ledger.Find(key);
        var carried = held is null
            ? CarriedGear.Empty
            : new CarriedGear([.. held.Inventory.Equipped.Values.Select(h => Id(h.Id))],
                              [.. held.Inventory.Backpack.Values.Select(h => Id(h.Id))],
                              [.. held.Inventory.Belt.Values.Select(h => Id(h.Id))],
                              held.Inventory.Australium);
        var dying = rules.Deaths.Die(key ?? "", hardcore, carried);

        LastCosts = string.Create(CultureInfo.InvariantCulture,
            $"player {death.EntIndex} died carrying {dying.Corpse.Items.Length} item(s) [{string.Join(' ', dying.Corpse.Items)}] to the corpse, "
            + $"kept {dying.Kept.Items.Count()} item(s) and {dying.Kept.Australium} Australium; XP {sheetBefore?.Xp ?? 0} -> {sheet?.Xp ?? 0} (a death costs none); "
            + $"durability unchanged (D21); sheet audit {(audit.Ok ? "ok" : audit.Rule)}{(sheet?.Fallen == true ? "; HARDCORE: fallen" : "")}");
        log.Msg($"[TF2Descent/Death] {LastCosts}\n");

        if (key is null)
            return;
        if (ledger.Kind == LedgerKind.Hosted)
        {
            // DeathService takes the record to the host; the corpse stands for the walk back.
            corpses.Place(death.EntIndex, key, death.Origin, hardcore);
            return;
        }
        int index = death.EntIndex;
        // The ledger's death reads the sheet to decide the kit (a hardcore hero gets none, plan L26):
        // it is handed the sheet this death left, not whatever the stats watcher last wrote.
        if (sheet is not null)
            ledger.SetSheet(key, sheet);
        // The ledger takes a hero's operations in order: the wear lands on the items before they move.
        wear.FlushHero(key, "death");
        _ = Record(index, key, death.Origin, hardcore);
    }

    private async Task Record(int index, string key, Vector at, bool hardcore)
    {
        if (await ledger.RecordDeath(key, at))
            corpses.Place(index, key, at, hardcore);
        else
            log.Warning($"[TF2Descent/Death] the ledger refused player {index}'s death: nothing went to a corpse\n");
    }

    /// <summary>A fallen hero (a hardcore death, D7) never respawns.</summary>
    public bool MayRespawn(EntityHandle player, int entIndex) =>
        !mode.Active || heroes.Of(entIndex)?.Sheet is not { } sheet || DeathRules.Playable(sheet);

    private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}
