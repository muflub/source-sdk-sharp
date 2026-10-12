//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A cap on how many of one weapon may be left lying about, and a broom.
///
/// A firefight leaves guns on the floor. On a long-running server that is a
/// growing pile of entities nobody will ever pick up, so a map says "at most
/// four loose shotguns" and this removes the rest. The decision it owns is not
/// the number — that is a keyvalue — but WHICH ones are surplus, and the whole
/// point of the entity is that the answer is never "the ones the level designer
/// placed".
///
/// Stock draws that line with a flag on the weapon: CBaseCombatWeapon spawns
/// non-removeable, becomes removeable when somebody drops it, and goes back when
/// somebody picks it up. Nothing managed can see that flag, so this draws the
/// same line from a different side: whatever is already in the world when the
/// manager first looks is the map's furniture, and everything that appears after
/// is loose. A map's weapons exist at level load and a dropped one does not,
/// so the two rules pick out the same guns — and this one has the advantage of
/// being a decision C# can explain rather than a bit it has to be told.
///
/// Weapons in somebody's hands are exempt, which needs saying because stock
/// spells it as "not EF_NODRAW": a carried weapon is parented to its owner and
/// its world model hidden, so the effect flag is really asking whether the gun
/// is on the floor. This asks that directly.
/// </summary>
[ScriptedEntity(EntityNames.GameWeaponManager)]
public sealed class GameWeaponManager : ScriptedEntity
{
    public GameWeaponManager(IEngineNetworking net)
    {
        Net = net;
    }

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;
    /// <summary>What a map may set on this entity. See <see cref="GameWeaponManagerVars"/>.</summary>
    [ScriptVars] public GameWeaponManagerVars Vars = new();

    // Often enough that a pile does not build up, rarely enough that walking the
    // entity list for it does not show. Stock's interval, and stock is right.
    private const float SweepInterval = 2f;

    // The map's own weapons, learned on the first sweep. Handles rather than
    // indices, because an entity index is reused the moment something else
    // spawns into the slot.
    private readonly HashSet<uint> _mapPlaced = new();
    private bool _surveyed;

    /// <summary>How many this manager has removed.</summary>
    public int RemovedCount { get; private set; }

    /// <summary>
    /// Whether it found the weapon it was asked to manage. False means the entity
    /// has removed itself and will never sweep.
    /// </summary>
    public bool Managing { get; private set; }

    public override void OnSpawn()
    {
        // Stock creates one of the named class to find out whether the name is
        // real and whether it is a weapon, then throws it away. C# owns the
        // weapon table, so the same two questions are a lookup — and a manager
        // for a class this mod does not have is removed exactly as stock removes
        // it, rather than sweeping a name that can never appear.
        if (WeaponTypes.For(Vars.WeaponName) is null)
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}': no weapon named '{Vars.WeaponName}'; removing itself\n");
            Remove();
            return;
        }

        Managing = true;
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        SetNextThink(SweepInterval);

        foreach (var weapon in Surplus(Loose()))
        {
            Entities.Remove(weapon);
            RemovedCount++;
        }
    }

    [ScriptedEvent("SetMaxPieces")]
    private void OnSetMaxPieces(float count) => Vars.MaxPieces = (int)count;

    [ScriptedEvent("SetAmmoModifier")]
    private void OnSetAmmoModifier(float modifier) => Vars.AmmoMod = modifier;

    /// <summary>
    /// Which of the weapons on the floor are surplus.
    ///
    /// Also where the map's own weapons are learned, on the first call, which is
    /// why this takes the world rather than reading it: the survey is the part
    /// worth being sure about, and it happens once and can never be repeated.
    ///
    /// The oldest go first. They are handed over in entity-list order, so the
    /// front of the list is the gun that has been on the floor longest — which is
    /// the one least likely to be under anybody's nose.
    /// </summary>
    internal IReadOnlyList<EntityHandle> Surplus(IReadOnlyList<EntityHandle> onTheFloor)
    {
        if (!_surveyed)
        {
            _surveyed = true;

            foreach (var weapon in onTheFloor)
                _mapPlaced.Add(weapon.Value);

            // Nothing has been dropped yet by definition, so the first sweep
            // never removes anything.
            return Array.Empty<EntityHandle>();
        }

        var dropped = new List<EntityHandle>();

        foreach (var weapon in onTheFloor)
        {
            if (!_mapPlaced.Contains(weapon.Value))
                dropped.Add(weapon);
        }

        int surplus = dropped.Count - Vars.MaxPieces;
        if (surplus <= 0)
            return Array.Empty<EntityHandle>();

        return dropped.GetRange(0, surplus);
    }

    /// <summary>
    /// Every weapon of this class that is on the floor rather than in a hand.
    ///
    /// Carried is read as parented: equipping a weapon follows its owner, which
    /// is a parent link, and dropping it stops following. That is a sharper test
    /// than the position or the move type, both of which a gun that has come to
    /// rest shares with one being carried.
    /// </summary>
    private List<EntityHandle> Loose()
    {
        var loose = new List<EntityHandle>();

        foreach (var weapon in Entities.FindAllByClassname(Vars.WeaponName))
        {
            var state = Net.EntityGetState(weapon, out _);

            if ((state & EntityState.Parented) == 0)
                loose.Add(weapon);
        }

        return loose;
    }
}

/// <summary>
/// The map keys <see cref="GameWeaponManager"/> declares.
/// </summary>
[ScriptVars]
public partial class GameWeaponManagerVars
{
    /// <summary>Which weapon this one is responsible for. One class per manager.</summary>
    public string WeaponName { get; set; } = string.Empty;

    /// <summary>
    /// How many loose ones to tolerate. Zero — stock's default, since the
    /// keyfield starts zeroed — means every dropped one is swept.
    /// </summary>
    public int MaxPieces { get; set; }

    /// <summary>
    /// Kept because the key belongs to the entity, and read by nothing: see the
    /// divergence note. Stock uses it to thin out the magazine of a dropped gun.
    /// </summary>
    public float AmmoMod { get; set; } = 1f;
}
