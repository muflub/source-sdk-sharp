using SourceSharp.Ai.TF2.Senses;

namespace SourceSharp.Ai.TF2.Trees;

/// <summary>
/// A class's terms for the shared ammo errand (round 2's S5): under <see cref="Floor"/> rounds in its pool
/// (<c>ClassPlay.AmmoPool</c>), in a lull, the bot goes for ammo, scored <see cref="Utility"/> on the class branch.
/// <see cref="Off"/> (a utility of 0) never goes: every class's default, so a class that names no terms plays as before.
/// </summary>
public readonly record struct TFAmmoErrand(int Floor, float Utility)
{
    /// <summary>No errand.</summary>
    public static TFAmmoErrand Off => default;

    /// <summary>Whether the errand is ever taken.</summary>
    public bool On => Utility > 0f && Floor > 0;
}

/// <summary>
/// The shared ammo errand (round 2's S5, lifted from the Heavy's own, research heavy.md §4.3 and §4.7.7): with its pool known
/// under its class's floor (<see cref="ClassPlay.Ammo"/>, <see cref="ClassPlay.AmmoPool"/>), no enemy seen for
/// <see cref="AmmoLullSeconds"/> and nothing carried, the bot goes to the nearest ammo pack it does not know taken, its team's
/// resupply cabinet, or a friendly dispenser it saw in the last <see cref="DispenserMemorySeconds"/>, within
/// <see cref="AmmoReach"/>. A place not reached and paid in <see cref="AmmoPatienceSeconds"/> is given up for
/// <see cref="AmmoSkipSeconds"/>. It runs on the class branch (<see cref="ClassBranch"/>, TFDecider.Classes.cs) beside the
/// class's own place. The numbers are the Heavy's (TFHeavy.cs before round 2).
/// </summary>
public sealed partial class TFDecider
{
    /// <summary>Ammo is gone for within this; a lull is no enemy seen for <see cref="AmmoLullSeconds"/>.</summary>
    public const float AmmoReach = 1500f, AmmoLullSeconds = 1f;

    /// <summary>A friendly dispenser seen within this long is an ammo place too (where the bot saw it; it may have been moved or lost since).</summary>
    public const float DispenserMemorySeconds = 60f;

    /// <summary>An ammo place not reached and paid in this long is given up for <see cref="AmmoSkipSeconds"/>.</summary>
    public const float AmmoPatienceSeconds = 12f, AmmoSkipSeconds = 30f;

    /// <summary>The verdict of a decide on the errand.</summary>
    public const string AmmoVerdict = "ammo";

    private Tick _ammoEnemySeen = new(-1_000_000);
    private bool _ammoGoing;
    private AiVector _ammoAt, _ammoSkip;
    private Tick _ammoSince, _ammoLast, _ammoSkipUntil;

    /// <summary>The ammo errands the bot set out on (a new place, or the same after a break), for the A/B harness.</summary>
    public int AmmoTrips { get; private set; }

    /// <summary>Notes an enemy in sight, which ends the lull. Before the tree, once a decide.</summary>
    private void NoteAmmoLull()
    {
        if (EnemyInSight())
            _ammoEnemySeen = _now;
    }

    /// <summary>The errand's score this decide: the class's utility with its pool known under its floor, in a lull, an ammo place in reach; else 0.</summary>
    private float AmmoErrandUtility()
    {
        TFAmmoErrand terms = _classPlay.Ammo;
        if (!terms.On || Carrying)
            return 0f;
        int pool = _classPlay.AmmoPool;
        return pool >= 0 && pool < terms.Floor && SecondsSince(_ammoEnemySeen) >= AmmoLullSeconds && AmmoPlace(out _) ? terms.Utility : 0f;
    }

    /// <summary>To the ammo place; false when there is none now (or the one gone for was just given up and no other is in reach).</summary>
    private bool GoAmmo()
    {
        if (!AmmoPlace(out AiVector at))
            return false;
        if (!_ammoGoing || at.DistanceTo(_ammoAt) > 1f || _now - _ammoLast > DecideSchedule.Period + 1)
        {
            (_ammoGoing, _ammoAt, _ammoSince) = (true, at, _now);
            AmmoTrips++;
        }
        else if (SecondsSince(_ammoSince) > AmmoPatienceSeconds)
        {
            // Not paid there in that long (the pack taken, or out of reach): another, for a while.
            (_ammoSkip, _ammoSkipUntil, _ammoGoing) = (at, _now + (int)(AmmoSkipSeconds / _tick), false);
            return false;
        }

        _ammoLast = _now;
        GoTo(at, MoveSpeed.Run);
        _verdict = AmmoVerdict;
        return true;
    }

    /// <summary>
    /// The nearest ammo pack the bot does not know taken, its team's resupply cabinet, or a friendly dispenser seen within
    /// <see cref="DispenserMemorySeconds"/>, within <see cref="AmmoReach"/>.
    /// </summary>
    private bool AmmoPlace(out AiVector at)
    {
        at = default;
        float best = AmmoReach;
        ReadOnlySpan<ItemBelief> beliefs = _memory.Board.Items;
        bool skipping = _now < _ammoSkipUntil;
        for (int i = 0; i < _map.Items.Length; i++)
        {
            ItemSpawn item = _map.Items[i];
            if (item.Kind is not (ItemKind.AmmoSmall or ItemKind.AmmoMedium or ItemKind.AmmoLarge)
                || (i < beliefs.Length && beliefs[i].Present == false) || (skipping && item.Position.DistanceTo(_ammoSkip) <= 1f))
                continue;
            float d = _self.Origin.DistanceTo(item.Position);
            if (d < best)
                (best, at) = (d, item.Position);
        }

        foreach (TeamZone z in _map.TeamZones)
        {
            if (z.Kind != ZoneKind.Resupply || z.Team != _self.Team)
                continue;
            var mid = new AiVector((z.Mins.X + z.Maxs.X) * 0.5f, (z.Mins.Y + z.Maxs.Y) * 0.5f, z.Mins.Z);
            if (skipping && mid.DistanceTo(_ammoSkip) <= 1f)
                continue;
            float d = _self.Origin.DistanceTo(mid);
            if (d < best)
                (best, at) = (d, mid);
        }

        if (NearestFriendly(TFThingType.Dispenser, DispenserMemorySeconds, out KnownBuilding dispenser, skipping, _ammoSkip))
        {
            float d = _self.Origin.DistanceTo(dispenser.At);
            if (d < best)
                (best, at) = (d, dispenser.At);
        }

        return best < AmmoReach;
    }

    /// <summary>The bot died: an errand under way is over.</summary>
    private void AmmoDied() => _ammoGoing = false;
}
