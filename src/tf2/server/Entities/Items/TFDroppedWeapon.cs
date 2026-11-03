//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace TF2.Server.Entities.Items;

/// <summary>
/// <c>tf_dropped_weapon</c> — the weapon itself, lying where its owner lost
/// it: stock's <c>CTFDroppedWeapon</c> (tf_dropped_weapon.cpp). The pack
/// <see cref="TFAmmoPack"/> carries the ammunition; this carries the weapon —
/// its worldmodel, its clip and its charge — until a player asks for it with
/// the use key (<c>use_action_slot_item_server</c>). It falls as debris
/// physics, keeps itself thirty seconds (<c>tf_dropped_weapon_lifetime</c>),
/// and a medigun's charge ticks down a hundredth every tenth of a second
/// while it lies there (<c>ChargeLevelDegradeThink</c>).
///
/// <para>
/// The stock members this port does not carry are the ones whose data the
/// weapons here do not have: the econ item view (the weapon's class name
/// stands in for the item def, and the item id has no econ behind it), the
/// energy charge and effect-bar regen (no energy weapon on this SDK's script
/// table), the broken flag and body group (no wearable states), the stick
/// bomb's detonated count and the charge meter (the meter arms are deferred,
/// docs/plans/tf2/deferred.tsv).
/// </para>
/// </summary>
[ScriptedEntity(Class, ScriptedEntityKind.Prop)]
public sealed class TFDroppedWeapon(ConVarRegistry convars, IUniformRandom random, IEngineEntityRender render)
    : ScriptedEntity
{
    public const string Class = "tf_dropped_weapon";

    /// <summary><c>MAX_DROPPED_WEAPON_COUNT</c> (:484): the floor keeps at most.</summary>
    public const int MaxDroppedCount = 32;

    // The GAME_DLL member block (:54-64) minus the absent-data ones the class
    // comment names: whose it was, what weapon, the clip and reserve it kept,
    // the charge, the skin.
    [SaveField] private uint _lastOwner = EntityHandle.None.Value;
    [SaveField] private string _weapon = "";
    [SaveField] private int _clip;
    [SaveField] private int _ammo;
    [SaveField] private float _chargeLevel;
    [SaveField] private int _skin;

    /// <summary><c>m_hPlayer</c>: whose it was.</summary>
    public EntityHandle LastOwner => new(_lastOwner);

    /// <summary>The weapon's class name — this port's stand-in for <c>m_Item</c>'s def.</summary>
    public string Weapon => _weapon;

    /// <summary><c>m_nClip</c>: the clip it died with.</summary>
    public int Clip => _clip;

    /// <summary><c>m_nAmmo</c>: the primary reserve its owner held.</summary>
    public int Ammo => _ammo;

    /// <summary><c>m_flChargeLevel</c>, the medigun's.</summary>
    public float ChargeLevel => _chargeLevel;

    /// <summary><c>m_nSkin</c>, the colour the death dressed it in.</summary>
    public int Skin => _skin;

    /// <summary>
    /// <c>Spawn</c> (:95-133) past the model, which the creator set on the
    /// entity before the spawn as stock's <c>SetModelName</c> did (:522): the
    /// debris collision group (:107), and the two thinks — the lifetime's
    /// <c>SUB_Remove</c> (:131) here, the physics object at the Prop kind's
    /// spawn-return as <see cref="TFAmmoPack.OnSpawn"/> records. Stock's
    /// <c>FSOLID_NOT_STANDABLE</c> and trigger-box bloat (:111, :108) ride
    /// the same physics init; the fall out of the world (:119-124) removes
    /// through the same <c>RemoveThink</c> when nothing lands under it.
    /// </summary>
    public override void OnSpawn()
    {
        SetCollisionGroup(CollisionGroup.Debris); // :107
        SetContextThink("RemoveThink", Lifetime); // :131
    }

    /// <summary>
    /// <c>InitDroppedWeapon</c> (:531-615) — the throw and the snapshot. The
    /// arm is <paramref name="thrown"/>: a player's discard hurls it along
    /// their view (:538-545); a death's drop tumbles it off the corpse on
    /// the same random right/up the pack takes, in stock's 100..150 band
    /// (:549-553). Mass 25 and the yaw spin ride both (:561-562).
    /// </summary>
    /// <param name="lastOwner">The player it came from (<c>m_hPlayer</c>).</param>
    /// <param name="weapon">The weapon's class name.</param>
    /// <param name="clip"><c>m_nClip</c>: stock keeps the energy weapon's
    /// full clip; no weapon here is one, so the clip as it lies.</param>
    /// <param name="ammo"><c>m_nAmmo</c>: the primary reserve at the loss.</param>
    /// <param name="chargeLevel"><c>m_flChargeLevel</c>: the medigun's, zero
    /// elsewhere; above zero arms the degrade think (:598-601), which is
    /// every weapon that can have one.</param>
    /// <param name="skin"><c>m_nSkin</c> (<c>pWeapon-&gt;GetSkin()</c>, :566).</param>
    /// <param name="thrown">The <c>bSwap</c> arm: a discard, not a death.</param>
    public void Init(EntityHandle lastOwner, string weapon, int clip, int ammo,
                     float chargeLevel = 0f, int skin = 0, bool thrown = false)
    {
        _lastOwner = lastOwner.Value;
        _weapon = weapon;

        // The throw (:536-556): the unit push, the arm's scale, then the
        // ride on the velocity the entity already has.
        Vector impulse;
        float scale;
        if (thrown)
        {
            var (forward, _, up) = Angles.Vectors(Entities.GetEyeAngles(Handle));
            impulse = new Vector(0f, 0f, 1.5f) + forward * 1.0f; // :542-543
            scale = 250f;                                        // :544
        }
        else
        {
            var (_, right, up) = Angles.Vectors(Entities.GetEyeAngles(Handle));
            impulse = up * random.RandomFloat(-0.25f, 0.25f)     // :550
                    + right * random.RandomFloat(-0.25f, 0.25f); // :551
            scale = random.RandomFloat(100f, 150f);              // :552
        }

        impulse = impulse.Normalized;               // :554
        impulse *= scale;                           // :555
        impulse += Entities.GetVelocity(Handle);    // :556

        Mass = 25f; // :561
        Entities.SetVelocity(Handle, impulse);                                                 // :563
        Entities.SetAngularVelocity(Handle, new Vector(0f, random.RandomFloat(0f, 100f), 0f)); // :562

        _skin = skin; // :566
        render.SetSkin(Handle, skin);

        _clip = clip;    // :568
        _ammo = ammo;    // :569
        _chargeLevel = chargeLevel; // :590-601, the medigun arm's carry

        if (chargeLevel > 0f)
            SetContextThink("ChargeLevelDegradeThink", DegradeInterval); // :600
    }

    /// <summary>
    /// <c>SUB_Remove</c> (:131) and <c>ChargeLevelDegradeThink</c>
    /// (:674-686): the lifetime's removal, and the charge losing a
    /// hundredth a tenth of a second until it pools at zero — the reschedule
    /// is the think's own last line, so a pool at zero just stops booking.
    /// </summary>
    public override void OnThink(string context)
    {
        switch (context)
        {
            case "RemoveThink":
                Remove();
                break;

            case "ChargeLevelDegradeThink":
                _chargeLevel -= DegradeStep; // :676
                if (_chargeLevel < 0f)
                {
                    _chargeLevel = 0f; // :680
                    return;            // :681, the think not rebooked
                }

                SetContextThink("ChargeLevelDegradeThink", DegradeInterval); // :685
                break;
        }
    }

    /// <summary>The stock <c>UTIL_Remove</c>s of this file, for the pickup's swap.</summary>
    new public void Remove() => base.Remove();

    /// <summary>
    /// The head of <c>CTFDroppedWeapon::Create</c> (:492-517), which every
    /// create — the death's and the pickup's swap alike — runs before
    /// spawning: one weapon of this owner's lying around already and the new
    /// one replaces it (:495-503, the item id, defindex and owner of stock's
    /// m_Item — the weapon and the owner here), and an ownerless floor past
    /// <see cref="MaxDroppedCount"/> loses the eldest the listing meets first
    /// (:506-517), the deduped excepted (:512).
    /// </summary>
    internal static void CleanUp(IEntityDirectory directory, EntityHandle owner, string weapon)
    {
        var listed = directory.OfClass(Class).OfType<TFDroppedWeapon>().ToList();

        int removed = 0;
        foreach (var dropped in listed)
        {
            if (dropped.LastOwner != owner || dropped.Weapon != weapon)
                continue; // :498

            dropped.Remove();
            removed++;
        }

        int excess = listed.Count - removed - MaxDroppedCount; // :506
        foreach (var dropped in listed)
        {
            if (excess <= 0)
                break;

            if (dropped.LastOwner == owner && dropped.Weapon == weapon)
                continue; // :512, already marked

            dropped.Remove();
            excess--;
        }
    }

    /// <summary>The lifetime, <c>tf_dropped_weapon_lifetime</c> (:27).</summary>
    public float Lifetime => ConVar<TFDroppedWeaponLifetimeConVar>().Value;

    /// <summary>The degrade clock's step and period (:676, :685).</summary>
    public const float DegradeStep = 0.01f;

    /// <summary>The tenth of a second the degrade rebooks at (:685).</summary>
    public const float DegradeInterval = 0.1f;

    /// <summary>
    /// Stock reads its lifetime live from the FCVAR_CHEAT convar it declares
    /// (:27); a missing declaration is a startup bug.
    /// </summary>
    private T ConVar<T>() where T : ConVarBase
        => convars.Get<T>() ?? throw new InvalidOperationException(
            $"tf_dropped_weapon reads {typeof(T).Name}, which is not registered");
}
