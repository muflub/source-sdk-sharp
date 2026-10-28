using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A weapon lying in the world — every weapon_* classname a map can place.
///
/// <para>
/// ONE CLASS, A NAME PER ROW, and the names come from the same table as everything
/// else about a weapon: each attribute below must have a row in
/// <see cref="WeaponTypes.All"/>, which is where the model on the ground and
/// the clip it hands over are declared. WeaponTests pins the two lists to each
/// other.
/// </para>
///
/// <para>
/// This replaces the native shell's registration path outright. The old
/// CSourceSharpWeapon was both the pickup on the ground AND the thing in the
/// hands; on the hierarchy those are two different things — this Item, and
/// <see cref="SsWeapon"/> — because they always were: one is walked into and
/// one is aimed.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.WeaponCrowbar,  ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponPistol,   ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.Weapon357,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponSmg1,     ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponAr2,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponShotgun,  ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponCrossbow, ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponRpg,      ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponFrag,     ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponStunstick, ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponSlam,     ScriptedEntityKind.Item)]
[ScriptedEntity(EntityNames.WeaponPhyscannon, ScriptedEntityKind.Item)]
public sealed class WeaponPickup : PickupItem
{
    private readonly Arsenal _arsenal;
    private readonly IEngineWorldQuery _world;

    // This side's weapon table, injected: the pickup for a weapon has to read
    // its world model from the SAME list the server gave from, and that list
    // now carries the mods' rows — a static here would stop at the core rows
    // and dress a mod weapon in a pistol's model.
    private readonly WeaponCatalog _weapons;

    public WeaponPickup(Arsenal arsenal, IEngineWorldQuery world, PlacedObjects placed, WeaponCatalog? weapons = null)
        : base(world, placed)
    {
        _arsenal = arsenal;
        _world = world;
        _weapons = weapons ?? WeaponCatalog.Stock();
    }

    /// <summary>The w_ model, from the weapon's own row.</summary>
    protected override string Model
        => _weapons.For(ClassName)?.WorldModel ?? "models/weapons/w_pistol.mdl";

    /// <summary>FlWeaponRespawnTime: sv_hl2mp_weapon_respawn_time, not the items' time.</summary>
    protected override float RespawnDelay => Placed.WeaponRespawnTime;

    /// <summary>The rules check a weapon on the weapons' time.</summary>
    protected override bool IsPlacedWeapon => true;

    /// <summary>
    /// CWeaponHL2MPBase::Materialize: a respawning weapon's first spot on
    /// the floor is its home, and it joins the rules' placed objects (again,
    /// after a respawn). One keyed not to respawn joins nothing.
    /// </summary>
    protected override void OnPhysical()
    {
        if (!DropsToFloor)
            return;

        if (!HasHome)
            SetHome();
        Placed.Add(this);
    }

    /// <summary>CBaseCombatWeapon::Equip: RemoveLevelDesignerPlacedObject.</summary>
    protected override void OnTaken() => Placed.Remove(this);

    /// <summary>CBaseCombatWeapon::Spawn: UseTriggerBounds( true, 36 ).</summary>
    protected override float TriggerBloat => 36f;

    /// <summary>DefaultTouch gives a weapon it cannot see; only the output waits on the sight line.</summary>
    protected override bool SightGatesPickup => false;

    /// <summary>
    /// CWeaponHL2MPBase::Materialize: VPhysicsInitNormal( SOLID_BBOX, flags |
    /// FSOLID_TRIGGER ), so traces meet its box, not its hull; nothing marks it
    /// not-standable but CWeaponStunStick::Spawn.
    /// </summary>
    protected override (SolidType Type, SolidFlags Flags) PhysicalSolidity
        => (SolidType.BoundingBox,
            SolidFlags.Trigger | (ClassName == EntityNames.WeaponStunstick ? SolidFlags.NotStandable : SolidFlags.None));

    /// <summary>A weapon has no CItem physgun reach.</summary>
    protected override bool CannonWidensReach => false;

    /// <summary>
    /// CWeaponHL2MPBase::ObjectCaps (weapon_hl2mpbase.cpp): HL2MP takes the
    /// impulse use CBaseCombatWeapon adds back off, so +USE passes a weapon by.
    /// </summary>
    public override UseCaps ObjectCaps => base.ObjectCaps & ~UseCaps.ImpulseUse;

    /// <summary>SF_NORESPAWN: a weapon keyed not to respawn falls as physics instead.</summary>
    public const int NoRespawnFlag = 1 << 30;

    /// <summary>How far down the drop looks for a floor (UTIL_DropToFloor).</summary>
    public const float DropDistance = 256f;

    /// <summary>
    /// The model's box, which SetModel installs and the trigger box then
    /// replaces: the box the drop sweeps, as stock's SOLID_BBOX is the
    /// model's. Saved because a reload does not run OnSpawn again.
    /// </summary>
    [SaveField] private Vector _hullMins;
    [SaveField] private Vector _hullMaxs;

    /// <summary>
    /// HL2MP's respawning map weapons drop before they are physical
    /// (CWeaponHL2MPBase::FallInit's UTIL_DropToFloor, then Materialize once
    /// on the ground); a CItem falls as physics from where the map put it.
    /// </summary>
    private bool DropsToFloor => (SpawnFlags & NoRespawnFlag) == 0;

    protected override void OnModelSet()
    {
        if (DropsToFloor)
            (_hullMins, _hullMaxs) = Entities.GetBounds(Handle);
    }

    /// <summary>A weapon is no CItem: its object takes MASK_SOLID, player clips not included.</summary>
    public override uint PhysicsSolidMask => EnginePushWorld.MaskSolid;

    /// <summary>CBaseCombatWeapon::FallInit: the fall waits for its think.</summary>
    protected override bool PhysicalAtSpawn => false;

    /// <summary>FallInit's think: Materialize a tenth of a second after the spawn.</summary>
    protected override float FallDelay => 0.1f;

    // The spawn's drop, for the think that follows it: whether it found ground.
    private bool? _landed;

    /// <summary>FallInit: the drop to the floor runs in the spawn.</summary>
    protected override void PrepareFall()
    {
        if (DropsToFloor)
            _landed = Drop();
    }

    protected override bool ReadyToFall()
    {
        if (!DropsToFloor)
            return true;

        // The spawn already dropped it; a respawn drops it again.
        if (_landed is { } landed)
        {
            _landed = null;
            return landed;
        }

        return Drop();
    }

    /// <summary>
    /// UTIL_DropToFloor (util.cpp) as HL2MP compiles it, with no start-solid
    /// check: the model's box swept down <see cref="DropDistance"/> as
    /// COLLISION_GROUP_NONE, since FallInit runs before the weapon's
    /// collision group is set. Stock's FallThink materializes only once that
    /// found ground; with none in reach, or starting all solid, the weapon
    /// stays a trigger where it is.
    /// </summary>
    private bool Drop()
    {
        Vector from = Origin;
        Vector to = from - new Vector(0f, 0f, DropDistance);
        EntityHullTrace tr = _world.EntityTraceHull(Handle, from, to, _hullMins, _hullMaxs,
                                                    EnginePushWorld.MaskSolid, (int)CollisionGroup.None);

        if (tr.AllSolid || tr.Fraction >= 1f)
            return false;

        Origin = tr.EndPos;
        return true;
    }

    protected override string Describe() => ClassName;

    /// <summary>
    /// Hands the weapon over. False for a player who already owns one of these
    /// AND has nothing to top up — the item stays for somebody it can help,
    /// which is how every Source pickup behaves.
    /// </summary>
    protected override bool TryGive(EntityHandle player)
        => _arsenal.GiveWeapon(player, ClassName);
}
