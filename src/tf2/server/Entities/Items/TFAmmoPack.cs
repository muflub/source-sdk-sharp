//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Player;
using TF2.Shared;

namespace TF2.Server.Entities.Items;

/// <summary>
/// <c>tf_ammo_pack</c> — the pack a TF2 death leaves behind, stock's
/// <c>CTFAmmoPack</c> (tf_ammo_pack.cpp). A throwaway physics prop that
/// throws itself where they fell, flies for three quarters of a second,
/// then lets players walk into it: the normal pack restocks the picker's
/// primary and secondary to half their own maximum plus the dead player's
/// snapshot of metal and grenades; the lunchbox pack heals instead of
/// giving, and an empty pack empties itself for nothing.
///
/// <para>
/// This is what TF2 dying drops — where HL2MP drops the weapon itself,
/// TF2's <c>CTFPlayer::Event_Killed</c> drops this (tf_player.cpp:13103)
/// and never touches a weapon pickup; <see cref="TF2.Server.Player.TFDeathDrops"/>
/// makes one over the core's death hook and switches the core's drop off.
/// </para>
/// </summary>
[ScriptedEntity(Class, ScriptedEntityKind.Prop)]
public sealed class TFAmmoPack(ITFMapPlayers players, ITFPickupNotices notices,
                               TFWeaponRagdollVelocityMinConVar velocityMin, TFWeaponRagdollVelocityMaxConVar velocityMax,
                               TFWeaponRagdollMaxSpeedConVar ragdollMaxSpeed,
                               IUniformRandom random, IEngineEntityRender render,
                               ITFPlayerStats? stats = null) : ScriptedEntity
{
    public const string Class = "tf_ammo_pack";

    /// <summary><c>CGameStats::Event_PlayerAmmokitPickup</c> (:434).</summary>
    private readonly ITFPlayerStats? _stats = stats;

    /// <summary>
    /// The model <c>DropAmmoPack</c> names at the call (<c>CTFAmmoPack::Create</c>,
    /// tf_player.cpp:13103) — the same one every normal pack wears. The
    /// halloween and christmas models (tf_ammo_pack.cpp:79-98) are deferred
    /// (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public const string Model = "models/items/ammopack_medium.mdl";

    /// <summary>The <c>FlyThink</c> delay (:50): the flight every pickup waits out.</summary>
    public const float FlyTime = 0.75f;

    /// <summary>The <c>DieContext</c> schedule (:71): thirty seconds is enough for anyone.</summary>
    public const float Lifetime = 30f;

    // The flag board of Spawn (:54-68): m_flCreationTime, m_flAmmoRatio, the
    // four pickup flags (m_bAllowOwnerPickup, m_bNoPickup, m_bHealthInstead,
    // m_bEmptyPack) and m_iAmmo — the last split into the four entries a pack
    // is ever asked to hold (the loop at :355-363 also walks the three
    // grenade slots no pack here fills; they are the zeros this class starts
    // with). Plus what InitAmmoPack stored for the skin and the lunchbox
    // decision, and the stock GetCreationTime/GetOwnerEntity reads.
    [SaveField] private uint _owner = EntityHandle.None.Value;
    [SaveField] private float _creationTime;
    [SaveField] private float _ammoRatio = 0.5f;
    [SaveField] private bool _allowOwnerPickup;
    [SaveField] private bool _noPickup;
    [SaveField] private bool _healthInstead;
    [SaveField] private bool _emptyPack;
    [SaveField] private int _ammoPrimary;
    [SaveField] private int _ammoSecondary;
    [SaveField] private int _ammoMetal;
    [SaveField] private int _ammoGrenades1;
    [SaveField] private int _weaponId;
    [SaveField] private int _skin;

    /// <summary><c>GetOwnerEntity()</c>: whose death this pack is.</summary>
    public EntityHandle PackOwner => new(_owner);

    /// <summary><c>GetCreationTime()</c>: what <c>AmmoPackCleanUp</c> oldest-sorts on.</summary>
    public float CreationTime => _creationTime;

    /// <summary><c>m_flAmmoRatio</c>: the fraction of the PICKER's maximum the pack restocks.</summary>
    public float AmmoRatio => _ammoRatio;

    /// <summary><c>m_bAllowOwnerPickup</c>: false until <c>FlyThink</c>.</summary>
    public bool AllowOwnerPickup => _allowOwnerPickup;

    /// <summary><c>m_bNoPickup</c>.</summary>
    public bool NoPickup => _noPickup;

    /// <summary><c>m_bHealthInstead</c>: the lunchbox's heal pack (<c>SetHealthInstead</c>).</summary>
    public bool HealthInstead => _healthInstead;

    /// <summary><c>m_bEmptyPack</c> (<c>MakeEmptyPack</c>, tf_ammo_pack.h:47).</summary>
    public bool EmptyPack => _emptyPack;

    // The pack's own m_iAmmo, as its GiveAmmo member (:240-251) stores it —
    // read by the give loop and by these getters (the facts' snapshot check).
    /// <summary><c>m_iAmmo[TF_AMMO_PRIMARY]</c> — stored, then recomputed at touch.</summary>
    public int AmmoPrimary => _ammoPrimary;

    /// <summary><c>m_iAmmo[TF_AMMO_SECONDARY]</c>.</summary>
    public int AmmoSecondary => _ammoSecondary;

    /// <summary><c>m_iAmmo[TF_AMMO_METAL]</c>: the dead player's snapshot, clamped 5..100.</summary>
    public int AmmoMetal => _ammoMetal;

    /// <summary><c>m_iAmmo[TF_AMMO_GRENADES1]</c>: zero for every pack this port fills.</summary>
    public int AmmoGrenades1 => _ammoGrenades1;

    /// <summary>
    /// The weapon whose death this is (<c>InitAmmoPack</c>'s
    /// <c>pWeapon-&gt;GetWeaponID()</c>): read for the lunchbox decision, and
    /// what the skin look would dress from (:181, deferred) — kept as the
    /// stock member is kept.
    /// </summary>
    public int WeaponId => _weaponId;

    /// <summary><c>m_nSkin</c>, set from <c>InitAmmoPack</c> (:181).</summary>
    public int Skin => _skin;

    /// <summary>
    /// <c>CTFAmmoPack::Spawn</c> (:43-77): the model, the fly-and-die think
    /// schedule, and the creation stamp the cleanup oldest-sorts on. The
    /// debris collision group (:184) lands HERE rather than in
    /// <see cref="Init"/>: the Prop kind hands the solver an object when the
    /// managed spawn returns (EntityPhysicsCreation's <c>Spawned</c>), and
    /// the group has to be the one that object is born with — the same
    /// placement <c>Grenade</c> makes for the same reason. The holiday
    /// precache (:79-98) and the X360 <c>EF_ITEM_BLINK</c> removal are not
    /// ported (docs/plans/tf2/deferred.tsv); <c>m_takedamage = DAMAGE_YES</c>
    /// (:185) is the Prop kind's own default (<see cref="ScriptedEntity.TakesDamage"/>).
    /// </summary>
    public override void OnSpawn()
    {
        SetModel(Model);                       // :46, :81
        SetNextThink(FlyTime);                 // :49-50
        SetContextThink("DieContext", Lifetime); // :71
        _creationTime = GameTime;              // :54
        _ammoRatio = 0.5f;                     // :57
        _allowOwnerPickup = false;             // :60-63
        _noPickup = false;
        _healthInstead = false;
        _emptyPack = false;
        _ammoPrimary = _ammoSecondary = _ammoMetal = _ammoGrenades1 = 0; // memset m_iAmmo, :68
        SetCollisionGroup(CollisionGroup.Debris); // :184, at spawn — see the note above
    }

    /// <summary>
    /// <c>InitAmmoPack</c> (:116-189), the state <c>DropAmmoPack</c> hands
    /// the fresh pack: what it stores as ammo, whether it heals or empties,
    /// and the throw that makes it leave the corpse — the random push along
    /// the eye up and right, scaled into the ragdoll-velocity band (the
    /// <c>tf_weapon_ragdoll_*</c> convars, :112-114), riding on the pack's own
    /// velocity (zero fresh out of <c>Create</c>) and capped at maxspeed;
    /// mass 25, a random yaw spin, health 900.
    /// </summary>
    /// <param name="owner">The dying player — the pack's owner.</param>
    /// <param name="weaponId">The dropped weapon's id, for the lunchbox arm.</param>
    /// <param name="ownerClass">The owner's class, for the lunchbox arm.</param>
    /// <param name="primary">Their primary count; the pack stores at least 5.</param>
    /// <param name="secondary">Their secondary count; the pack stores at least 5.</param>
    /// <param name="metal">Their metal; clamped 5..100.</param>
    /// <param name="skin"><c>m_nSkin</c>; the skin look (tf_player.cpp:13070-13085)
    /// is deferred, so the drop lands stock's caller default.</param>
    /// <param name="empty"><c>bEmpty</c>: an empty pack (<c>MakeEmptyPack</c>).</param>
    /// <param name="isSuicide"><c>bIsSuicide</c> — stock reads it only in the
    /// holiday arms (:123-134, deferred), so the port records the arm and carries the value.</param>
    /// <param name="ammoRatio"><c>flAmmoRatio</c>: stock's default, half.</param>
    public void Init(EntityHandle owner, int weaponId, TFClass ownerClass,
                     int primary, int secondary, int metal,
                     int skin = 0, bool empty = false, bool isSuicide = false,
                     float ammoRatio = 0.5f)
    {
        _owner = owner.Value;
        _weaponId = weaponId;
        _ammoRatio = ammoRatio; // :118

        if (!empty)
        {
            // The holiday-pack draw (:123-134) is deferred with the holiday
            // calendar and m_PackType; isSuicide is read only there.
            _ = isSuicide;

            // Fill the pack with the dead player's ammo, at a stock minimum (:137-139).
            StoreAmmo(Math.Max(5, primary), TFAmmo.Primary);       // :142, "recalculated in PackTouch"
            StoreAmmo(Math.Max(5, secondary), TFAmmo.Secondary);   // :143
            StoreAmmo(Math.Clamp(metal, 5, 100), TFAmmo.Metal);    // :144
            _healthInstead = weaponId == (int)TFWeaponId.Lunchbox
                             && ownerClass == TFClass.HeavyWeapons; // :145
        }
        else
        {
            _emptyPack = true; // MakeEmptyPack (tf_ammo_pack.h:47)
        }

        // The throw (:153-179): the unit push along the eye right/up times a
        // ragdoll-band speed, riding on the pack's own velocity, capped at
        // maxspeed. Stock guards the mass and instantaneous velocity on
        // VPhysicsGetObject(); the Prop kind hands the solver an object at
        // spawn and the hook Inits after that spawn, so the guard's arm is
        // always the taken one and the port takes it directly.
        var (_, right, up) = Angles.Vectors(Entities.GetEyeAngles(Handle));
        var impulse = up * random.RandomFloat(-0.25f, 0.25f)
                    + right * random.RandomFloat(-0.25f, 0.25f);
        impulse = impulse.Normalized;
        impulse *= random.RandomFloat(velocityMin.Value, velocityMax.Value);
        impulse += Entities.GetVelocity(Handle);

        float speed = impulse.Length;
        float maxSpeed = ragdollMaxSpeed.Value;
        if (speed > maxSpeed)
            impulse *= maxSpeed / speed;

        Mass = 25f; // :174
        Entities.SetVelocity(Handle, impulse);                                                      // :176
        Entities.SetAngularVelocity(Handle, new Vector(0f, random.RandomFloat(0f, 100f), 0f));     // :175
        // SetInitialVelocity (:179) feeds the respawn system — deferred with it.

        _skin = skin; // :181, m_nSkin
        render.SetSkin(Handle, skin);

        Health = 900; // :186, "so that trains can destroy it"
        // SetBodygroup(1, 1) (:188) needs the bodygroup seam — deferred.
    }

    /// <summary>
    /// <c>FlyThink</c> (:265-269), scheduled once at spawn: the pack was in
    /// flight and nobody could take it — least of all the owner, who is the
    /// one person it hides from while it still belongs to the corpse. It
    /// never reschedules: this runs once, 0.75 s out, and the rest of its
    /// life is on the ground.
    /// </summary>
    public override void OnThink()
    {
        _allowOwnerPickup = true;
        _noPickup = false;
    }

    /// <summary><c>DieContext</c> (:71): <c>SUB_Remove</c>, thirty seconds out.</summary>
    public override void OnThink(string context)
    {
        if (context == "DieContext")
            Remove();
    }

    /// <summary>
    /// <c>PackTouch</c> (:271-446): who may take it, what they get, and the
    /// pack going when they do. The world z-damp (:275-280), the spy's
    /// stealthed-empty skip (:302-304), the knife and charge arms
    /// (:371-391 — TFPlayerShared meters this build does not port), the
    /// objgib achievement (:358-361) and the holiday arms (:399-430) are
    /// deferred (docs/plans/tf2/deferred.tsv).
    /// </summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (!Entities.IsPlayer(toucher))
            return; // :282

        if (!players.IsAlive(toucher))
            return; // :285

        if (_noPickup)
            return; // :288

        // :291-294: you may throw ammo to someone, but you may not touch your
        // own while it still flies.
        if (toucher == PackOwner && !_allowOwnerPickup)
            return;

        if (_emptyPack)
        {
            // :299-311: the fakeout is pickable — and it is full of nothing.
            StoreAmmo(1, TFAmmo.Primary); // :308 — the member store; the picker gets no arm
            Remove();
            return;
        }

        if (_healthInstead)
        {
            // :314-345: the lunchbox hides itself from everyone until it lands.
            if (!_allowOwnerPickup)
                return; // :317

            int amount = players.ClassOf(toucher) == TFClass.Scout ? 75 : 50; // :321, the scout movie
            players.TakeHealth(toucher, amount);                              // :322
            notices.PlayerHealOnHit(toucher, amount);                         // :323-330
            notices.PlayerStealsSandvich(PackOwner, toucher);                 // :332-341
            Remove();
            return;
        }

        // :347-351: the pack's primary and secondary become the picker's OWN
        // half-maximum (the dead player's snapshot only ever rode metal and
        // the walk-out loop), through the pack's GiveAmmo member.
        StoreAmmo((int)MathF.Ceiling(players.MaxAmmo(toucher, TFAmmo.Primary) * _ammoRatio), TFAmmo.Primary);
        StoreAmmo((int)MathF.Ceiling(players.MaxAmmo(toucher, TFAmmo.Secondary) * _ammoRatio), TFAmmo.Secondary);

        int taken = 0; // :353-363
        taken += players.GiveAmmo(toucher, _ammoPrimary, TFAmmo.Primary);
        taken += players.GiveAmmo(toucher, _ammoSecondary, TFAmmo.Secondary);
        taken += players.GiveAmmo(toucher, _ammoMetal, TFAmmo.Metal);
        taken += players.GiveAmmo(toucher, _ammoGrenades1, TFAmmo.Grenades1);

        if (players.AddToSpyCloakMeter(toucher, 100f * _ammoRatio)) // :366-369 — cloak rides the pack, as the ammo does
            taken++;

        if (players.ClassOf(toucher) == TFClass.Engineer) // :393-397
            taken += players.GiveAmmo(toucher,
                (int)MathF.Ceiling(players.MaxAmmo(toucher, TFAmmo.Grenades1) * _ammoRatio), TFAmmo.Grenades1);

        if (taken > 0) // :432-445 — the quirk kept: gave nothing, keep the pack.
        {
            notices.ItemPickupEvent(toucher, Class);
            _stats?.Increment(toucher, TFStat.Ammokits); // :434 — Event_PlayerAmmokitPickup
            Remove();
        }
    }

    /// <summary>The stock <c>UTIL_Remove</c>s of this file, exposed for the arms' callers.</summary>
    new public void Remove() => base.Remove();

    /// <summary>
    /// <c>CTFAmmoPack::GiveAmmo</c> (:240-251) — the MEMBER store: it only
    /// assigns <c>m_iAmmo[type] = count</c> (the guard there rejects indices
    /// outside the table, which the four fields cannot express). The stock
    /// comment's "recalculated in PackTouch" is why the recompute goes
    /// through here too rather than onto the picker.
    /// </summary>
    private void StoreAmmo(int count, TFAmmo type)
    {
        switch (type)
        {
            case TFAmmo.Primary: _ammoPrimary = count; break;
            case TFAmmo.Secondary: _ammoSecondary = count; break;
            case TFAmmo.Metal: _ammoMetal = count; break;
            case TFAmmo.Grenades1: _ammoGrenades1 = count; break;
        }
    }
}
