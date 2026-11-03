using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Econ;

namespace TF2.Tests.Weapons;

/// <summary>The carrier a fact composes: classes, conditions and the rest, set per player.</summary>
internal sealed class FakeOwners : ITFWeaponOwners
{
    public Dictionary<int, TFClass> Classes { get; } = new();
    public Dictionary<int, TFConditions> Conditions { get; } = new();
    public Dictionary<int, IAttributeProvider> Items { get; } = new();

    /// <summary>Items by weapon row, for the name-checked asks (the burn's active-weapon arm); read before <see cref="Items"/>.</summary>
    public Dictionary<int, Dictionary<string, IAttributeProvider>> ByName { get; } = new();

    /// <summary>The weapon row each player holds, for <see cref="ActiveItemOf"/>; absent or empty is no weapon.</summary>
    public Dictionary<int, string> Held { get; } = new();

    /// <summary>The player's own attribute managers (stock's m_AttributeManager), for the max-ammo hook facts.</summary>
    public Dictionary<int, IAttributeProvider> Managers { get; } = new();
    public float Mult { get; set; } = 1f;
    public bool RoundWin { get; set; }
    public bool Underwater { get; set; }

    /// <summary>The no-attack window the one player's decloak stamped; 0 is none.</summary>
    public float Expire { get; set; }

    /// <summary>Whether the one player is off the ground, for the jump-unzoom facts.</summary>
    public bool Airborne { get; set; }

    public TFClass ClassOf(int playerIndex) => Classes.TryGetValue(playerIndex, out var c) ? c : TFClass.Undefined;

    public TFConditions? ConditionsOf(int playerIndex) => Conditions.TryGetValue(playerIndex, out var c) ? c : null;

    public float CritMult(int playerIndex) => Mult;

    public bool WinsCrits(int playerIndex) => RoundWin;

    public IAttributeProvider? ItemOf(int playerIndex, string weaponName)
        => ByName.TryGetValue(playerIndex, out var byName) && byName.TryGetValue(weaponName, out var named) ? named
           : Items.TryGetValue(playerIndex, out var i) ? i
           : null;

    public IAttributeProvider? ActiveItemOf(int playerIndex)
        => Held.TryGetValue(playerIndex, out var name) && name.Length > 0 ? ItemOf(playerIndex, name) : null;

    public IAttributeProvider? ManagerOf(int playerIndex) => Managers.TryGetValue(playerIndex, out var m) ? m : null;

    public bool OffGround(int playerIndex) => Airborne;
    public bool EyesUnderwater(int playerIndex) => Underwater;
    public bool DuckingOf(int playerIndex) => Ducking;

    public float NoAttackExpire(int playerIndex) => Expire;

    /// <summary>
    /// The round's ranged and random-crit damage the crit roll measures the
    /// observed rate from, as the server's stats would answer; null (the
    /// default) is the client's side: it compares the stored value alone.
    /// </summary>
    public (int Total, int RandomCrit)? RoundStats { get; set; }

    public (int Total, int RandomCrit)? RangedCritDamageOf(int playerIndex) => RoundStats;

    /// <summary>Whether the one player is crouched, for the rocket-muzzle facts.</summary>
    public bool Ducking { get; set; }

    /// <summary>The player's conditions, made authoritative on first ask.</summary>
    public TFConditions Of(int playerIndex)
    {
        if (!Conditions.TryGetValue(playerIndex, out var c))
            Conditions[playerIndex] = c = new TFConditions(authoritative: true);
        return c;
    }
}
