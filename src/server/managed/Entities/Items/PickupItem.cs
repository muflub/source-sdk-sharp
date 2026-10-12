using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The map item entities, in C#.
///
/// These are the classes maps place directly — health, ammo and armour lying
/// around a level. Without them a stock HL2 deathmatch map reports well over a
/// hundred spawn failures and gives the player nothing to collect.
///
/// Everything shared lives in <see cref="PickupItem"/>: a model, a trigger, and
/// what happens when it is taken. A kind is then a handful of lines saying what
/// it looks like and what it gives.
/// </summary>
public abstract class PickupItem : ScriptedEntity
{
    public readonly Output OnPlayerTouch = new();

    /// <summary>Model the item wears. Applied during spawn, so it is precached.</summary>
    protected abstract string Model { get; }

    /// <summary>Seconds before it comes back. Zero or less means it is gone for good.</summary>
    protected virtual float RespawnDelay => 20f;

    /// <summary>Half-extents of the trigger the player walks into.</summary>
    protected virtual float SizeX => 16f;
    protected virtual float SizeY => 16f;
    protected virtual float SizeZ => 32f;

    /// <summary>
    /// Gives the contents to a player. Returns false if there was nothing to
    /// give — a full player leaves the item in the world rather than consuming
    /// it for no benefit, which is how every Source item behaves.
    /// </summary>
    protected abstract bool TryGive(EntityHandle player);

    /// <summary>What the console reports when it is taken. For the log only.</summary>
    protected virtual string Describe() => GetType().Name;

    private bool _taken;

    public override void OnSpawn()
    {
        SetModel(Model);
        MakePickup(SizeX, SizeY, SizeZ);
    }

    //-------------------------------------------------------------------------
    // A trigger reports EVERY tick the player stands inside it, so this must be
    // idempotent — hence the taken flag rather than trusting one call.
    //-------------------------------------------------------------------------
    public override void OnTouch(EntityHandle toucher)
    {
        // A dead player collects nothing — asked by health rather than through
        // the player directory, because an item entity has no directory in
        // hand, and a corpse's health is 0 or below by the same rule that
        // killed it. The live bug: a death-dropped weapon landed on its own
        // corpse and was taken straight back by the dead body's touch.
        if (Entities.GetHealth(toucher) <= 0)
            return;

        if (_taken || !TryGive(toucher))
            return;

        _taken = true;
        OnPlayerTouch.Fire();

        // Hidden rather than removed when it will come back: a mapper may have
        // wired outputs to this entity, and those connections were bound when
        // the map loaded and cannot be rebuilt.
        if (RespawnDelay > 0f)
        {
            SetPickupEnabled(false);
            SetNextThink(RespawnDelay);
        }
        else
        {
            Remove();
        }

        Log.Msg($"[SourceSharp/Item] {toucher} took {Describe()}\n");
    }

    public override void OnThink()
    {
        _taken = false;
        SetPickupEnabled(true);
    }

    /// <summary>
    /// Tops a player up to a cap, and reports whether anything was actually
    /// given. Shared because health and armour both work this way.
    /// </summary>
    /// <summary>Test seam for <see cref="TopUp"/>, which is the arithmetic every
    /// health and armour item shares and the part worth testing on its own.</summary>
    internal static bool TopUpForTest(int current, int amount, int cap, Action<int> apply)
        => TopUp(current, amount, cap, apply);

    /// <summary>
    /// The HUD's pickup history entry for <paramref name="player"/>: stock's
    /// ItemPickup with the item's classname, reliable, to that player alone
    /// (CHealthKit::MyTouch, CHealthVial::MyTouch, CHL2_Player::ApplyBattery).
    /// </summary>
    protected void ReportPickup(IUserMessageSender messages, EntityHandle player, string item)
        => messages.Send(new ItemPickup { Item = item }, Entities.GetIndex(player), reliable: true);

    protected static bool TopUp(int current, int amount, int cap, Action<int> apply)
    {
        if (current >= cap)
            return false;

        apply(Math.Min(cap, current + amount));
        return true;
    }
}
