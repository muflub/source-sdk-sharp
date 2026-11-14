using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Items;
using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Descent.Server.Hero;

/// <summary>The engine work the belt does (HE8), behind one interface so each rule has a fact over a fake world.</summary>
public interface IBeltWorld
{
    /// <summary>The server tick now.</summary>
    int Tick { get; }

    /// <summary>Seconds a server tick.</summary>
    float TickInterval { get; }

    bool IsAlive(EntityHandle player);

    /// <summary>The player carries TF's stun condition (a stunned hero cannot drink).</summary>
    bool Stunned(EntityHandle player);

    int Health(EntityHandle player);

    int MaxHealth(EntityHandle player);

    /// <summary>Life back, up to the TF maximum (no overheal); answers what was healed.</summary>
    int Heal(EntityHandle player, float amount);
}

/// <summary>The engine's <see cref="IBeltWorld"/>: the globals' clock, TF2's conditions and resupply.</summary>
[SideService(Side.Server, As = [typeof(IBeltWorld)])]
public sealed class EngineBeltWorld(IEngineEnvironment environment, IEngineEntities entities, IEnginePlayers players,
                                    ITFPlayerConditions conditions, ITFPlayerResupply resupply) : IBeltWorld
{
    public int Tick => environment.Globals is { IsAvailable: true } g ? g.TickCount : 0;

    public float TickInterval => environment.Globals is { IsAvailable: true } g ? g.IntervalPerTick : 0.015f;

    public bool IsAlive(EntityHandle player) => players.IsAlive(player);

    public bool Stunned(EntityHandle player) => conditions.InCond(player, TFCond.Stunned);

    public int Health(EntityHandle player) => entities.GetHealth(player);

    public int MaxHealth(EntityHandle player) => resupply.MaxHealth(player);

    public int Heal(EntityHandle player, float amount) => resupply.TakeHealth(player, amount);
}

/// <summary>Why a drink or a use was refused, beside the rules' and the ledger's own words.</summary>
public static class BeltRefusals
{
    public const string NoHero = "no_hero";
    public const string NoSuchSlot = "no_such_slot";
    public const string Dead = "dead";
    public const string Stunned = "stunned";
    public const string Empty = "empty";
    public const string NotAPotion = "not_a_potion";
    public const string OnCooldown = "on_cooldown";
    public const string Full = "full";
}

/// <summary>A drink: whether it went down, why not, the potion, what it restored and how much, and the tick the belt is ready again.</summary>
public readonly record struct BeltDrink(bool Ok, string? Refusal, string? Potion = null, string? Restores = null, double Restored = 0,
                                        int ReadyTick = 0);

/// <summary>
/// THE POTION BELT (plan_descent_logic HE8), per hero:
/// <list type="bullet">
/// <item>A drink from belt slot 1 to 4 (the key's <c>tf2_descent_belt &lt;slot&gt;</c>) is
/// refused, using nothing, for a hero who is dead or stunned, an empty slot, while the belt's
/// shared cooldown runs, and at a full pool (<see cref="BeltRules.Drink"/>). Else the potion
/// leaves through the ledger (one from the slot's stack; the slot refills from the backpack),
/// and only then is its restore given: life as TF health up to the TF maximum, mana up to
/// the stats' maximum; the cooldown starts, in server ticks.</item>
/// <item>The cooldown is the hero's, held here by player index, and every cooldown ends at a
/// level change (the server's tick count starts again there).</item>
/// <item>A refusal is told to the hero on his HUD and logged.</item>
/// </list>
/// Hosted, the host has no drink call: the ledger refuses, and nothing is restored.
/// </summary>
[SideService(Side.Server)]
public sealed class HeroBelt(TF2DescentRules rules, Heroes heroes, HeroKeys keys, IItemLedger ledger, IBeltWorld world,
                             ClientMessages messages, ILog log)
{
    private readonly Dictionary<int, (int Ready, int Length)> _cooldowns = new();
    private readonly HashSet<int> _changed = [];

    /// <summary>The belt's slots (inventory.json's belt).</summary>
    public int Slots => rules.Content.InventorySettings.BeltSlots;

    /// <summary>The tick the hero's belt is ready again (0: ready) and the cooldown's length in ticks.</summary>
    public (int ReadyTick, int CooldownTicks) CooldownOf(int playerIndex) =>
        _cooldowns.TryGetValue(playerIndex, out var c) && c.Ready > world.Tick ? c : (0, 0);

    /// <summary>Seconds in server ticks, to the nearest (as the skills' cooldowns are), at least one for any cooldown.</summary>
    public int Ticks(double seconds) =>
        seconds <= 0 ? 0 : Math.Max(1, (int)Math.Round(seconds / world.TickInterval, MidpointRounding.AwayFromZero));

    /// <summary>The heroes whose cooldown changed since the last <see cref="TakeChanged"/> (what the belt's message follows).</summary>
    public IReadOnlyCollection<int> TakeChanged()
    {
        var changed = _changed.ToArray();
        _changed.Clear();
        return changed;
    }

    /// <summary>Drinks from belt slot <paramref name="slot"/> (1 to <see cref="Slots"/>); see the class summary.</summary>
    public async Task<BeltDrink> Drink(int playerIndex, int slot)
    {
        if (heroes.Of(playerIndex) is not { Stats: { } stats } hero || keys.Of(playerIndex) is not { } key
            || ledger.Find(key) is not { } held)
            return Refuse(playerIndex, slot, BeltRefusals.NoHero);
        if (slot < 1 || slot > Slots)
            return Refuse(playerIndex, slot, BeltRefusals.NoSuchSlot);
        if (!world.IsAlive(hero.Player))
            return Refuse(playerIndex, slot, BeltRefusals.Dead);
        if (world.Stunned(hero.Player))
            return Refuse(playerIndex, slot, BeltRefusals.Stunned);
        if (!held.Inventory.Belt.TryGetValue(slot - 1, out var potion))
            return Refuse(playerIndex, slot, BeltRefusals.Empty);

        var (ready, _) = CooldownOf(playerIndex);
        double left = ready > 0 ? (ready - world.Tick) * (double)world.TickInterval : 0;
        var drink = rules.ItemRules.Belt.Drink(potion.Item, stats, world.Health(hero.Player), world.MaxHealth(hero.Player),
                                               hero.Mana, stats.MaxMana, left);
        if (!drink.Ok)
        {
            return Refuse(playerIndex, slot, drink.Refusal switch
            {
                DrinkRefusal.OnCooldown => BeltRefusals.OnCooldown,
                DrinkRefusal.Full => BeltRefusals.Full,
                _ => BeltRefusals.NotAPotion,
            }, potion.Item.BaseId);
        }

        var used = await ledger.Drink(key, slot - 1);
        if (!used.Ok)
            return Refuse(playerIndex, slot, used.Refusal ?? LedgerRefusals.NoHero, potion.Item.BaseId);

        double restored;
        if (drink.Restores == PotionResources.Life)
        {
            restored = world.Heal(hero.Player, (float)drink.Amount);
        }
        else
        {
            double before = hero.Mana;
            hero.SetMana(hero.Mana + drink.Amount);
            restored = hero.Mana - before;
        }

        int length = Ticks(drink.CooldownSeconds);
        int readyTick = length > 0 ? world.Tick + length : 0;
        if (length > 0)
            _cooldowns[playerIndex] = (readyTick, length);
        _changed.Add(playerIndex);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Belt] {playerIndex} drank {potion.Item.BaseId} from slot {slot}: +{restored:0.##} {drink.Restores} " +
            $"(rules {drink.Amount:0.##}), cooldown {drink.CooldownSeconds:0.###} s to tick {readyTick}\n"));
        return new BeltDrink(true, null, potion.Item.BaseId, drink.Restores, restored, readyTick);
    }

    /// <summary>The hero left: his cooldown goes with him.</summary>
    public void Forget(int playerIndex)
    {
        _cooldowns.Remove(playerIndex);
        _changed.Remove(playerIndex);
    }

    /// <summary>A level change: every cooldown ends (the tick count starts again).</summary>
    public void ForgetAll()
    {
        _cooldowns.Clear();
        _changed.Clear();
    }

    private BeltDrink Refuse(int playerIndex, int slot, string why, string? potion = null)
    {
        log.Msg($"[TF2Descent/Belt] {playerIndex} drink from slot {slot} refused: {why}\n");
        if (why != BeltRefusals.NoHero)
            messages.Notice(playerIndex, $"Belt {slot}: {Words(why)}");
        return new BeltDrink(false, why, potion);
    }

    /// <summary>A refusal as the hero is told it.</summary>
    public static string Words(string why) => why switch
    {
        BeltRefusals.Dead => "not while dead",
        BeltRefusals.Stunned => "not while stunned",
        BeltRefusals.Empty => "empty",
        BeltRefusals.OnCooldown => "not ready",
        BeltRefusals.Full => "already full",
        BeltRefusals.NoSuchSlot => "no such slot",
        BeltRefusals.NotAPotion => "nothing to drink",
        _ => why,
    };
}
