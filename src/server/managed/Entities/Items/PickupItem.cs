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
public abstract class PickupItem : ScriptedEntity, IPhysGunTarget
{
    protected PickupItem(IEngineWorldQuery world)
    {
        PickupWorld = world;
    }

    /// <summary>What the line of sight to a toucher is traced through (UTIL_ItemCanBeTouchedByPlayer).</summary>
    private readonly IEngineWorldQuery PickupWorld;

    public readonly Output OnPlayerTouch = new();

    /// <summary>A player reached it, whether or not it had anything to give (CItem's m_OnCacheInteraction).</summary>
    public readonly Output OnCacheInteraction = new();

    /// <summary><c>MASK_SOLID</c>: CONTENTS_SOLID | MOVEABLE | WINDOW | MONSTER | GRATE.</summary>
    private const uint MaskSolid = GameMovement.ContentsSolid | GameMovement.ContentsMoveable | GameMovement.ContentsWindow
                                   | GameMovement.ContentsMonster | GameMovement.ContentsGrate;

    /// <summary>ITEM_PICKUP_BOX_BLOAT: how far past its box a player reaches it.</summary>
    public const float PickupBoxBloat = 24f;

    /// <summary>How far past its box a player reaches it; a weapon's is 36.</summary>
    protected virtual float TriggerBloat => PickupBoxBloat;

    /// <summary>
    /// Whether a player who cannot see it is refused (CItem::ItemTouch). A
    /// weapon's DefaultTouch only withholds the output and gives anyway.
    /// </summary>
    protected virtual bool SightGatesPickup => true;

    /// <summary>
    /// What it falls as: CItem's SOLID_VPHYSICS trigger nothing stands on
    /// (CreateItemVPhysicsObject). A weapon is a box (Materialize).
    /// </summary>
    protected virtual (SolidType Type, SolidFlags Flags) PhysicalSolidity
        => (SolidType.VPhysics, SolidFlags.Trigger | SolidFlags.NotStandable);

    /// <summary>Whether the gravity gun's carry widens the reach (CItem's, not a weapon's).</summary>
    protected virtual bool CannonWidensReach => true;

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

    /// <summary>The think context that makes the item physical once its model is on it.</summary>
    public const string PhysicsContext = "ItemPhysics";

    /// <summary>
    /// CItem::ObjectCaps (items.h): a +USE uses it. Its Use is the carry,
    /// which stock HL2MP refuses unless hl2mp_allow_pickup is on, so a use
    /// here is heard and does nothing (PlayerPickupRules).
    /// </summary>
    public override UseCaps ObjectCaps => base.ObjectCaps | UseCaps.ImpulseUse;

    /// <summary>
    /// Called once the model is on, before the trigger box replaces its
    /// bounds: the one moment the model's own box can be read.
    /// </summary>
    protected virtual void OnModelSet() { }

    /// <summary>
    /// Readies the item to become physical, or answers false to stay a
    /// trigger where it is. A CItem falls as physics from where the map put it.
    /// </summary>
    protected virtual bool ReadyToFall() => true;

    public override void OnSpawn()
    {
        SetModel(Model);
        OnModelSet();
        MakePickup(SizeX, SizeY, SizeZ);

        // The model is applied after spawn returns, and the physics object is
        // made from the model: a think at once, as stock's CItem::Spawn runs
        // CreateItemVPhysicsObject after its subclass set the model.
        SetContextThink(PhysicsContext, 0f);
    }

    public override void OnThink(string context)
    {
        if (context == PhysicsContext)
            BecomePhysical();
    }

    /// <summary>
    /// Stock's CItem::Spawn and CreateItemVPhysicsObject: in
    /// COLLISION_GROUP_WEAPON, so players pass through it and other items and
    /// props do not; a solid physics trigger nothing stands on; simulated and
    /// awake, so it falls and settles on its model's hull. The pickup box
    /// MakePickup set stays its trigger. A model with no collision stays the
    /// box it was (stock drops it to the floor instead).
    /// </summary>
    private void BecomePhysical()
    {
        if (ReadyToFall())
        {
            SetCollisionGroup(CollisionGroup.Weapon);
            (SolidType solid, SolidFlags flags) = PhysicalSolidity;
            Entities.SetSolidity(Handle, solid, flags);

            if (!InitPhysics())
                MakePickup(SizeX, SizeY, SizeZ);
            else if (solid != SolidType.VPhysics)
                // VPhysicsInitNormal( SOLID_BBOX, ... ) keeps the box it was
                // given; the object's create here writes SOLID_VPHYSICS.
                Entities.SetSolidity(Handle, solid, flags);
        }

        // CItem::Spawn: the pickup box bloated, so a player takes it before
        // reaching its own box. After the solidity, which writes the flags.
        UseTriggerBounds(true, TriggerBloat);
    }

    /// <summary>CItem::OnPhysGunPickup: the gravity gun's carry doubles the reach, and says so.</summary>
    void IPhysGunTarget.OnPhysGunPickup(EntityHandle player, PhysGunPickup reason)
    {
        if (!CannonWidensReach)
            return;

        OnCacheInteraction.Fire(player);

        if (reason == PhysGunPickup.PickedUpByCannon)
            UseTriggerBounds(true, TriggerBloat * 2f);
    }

    /// <summary>CItem::OnPhysGunDrop: the reach back to the item's own.</summary>
    void IPhysGunTarget.OnPhysGunDrop(EntityHandle player, PhysGunDrop reason)
    {
        if (CannonWidensReach)
            UseTriggerBounds(true, TriggerBloat);
    }

    /// <summary>
    /// UTIL_ItemCanBeTouchedByPlayer: nothing solid between the item's centre
    /// and the player's eye, traced as the player moves (so other items do not
    /// count). The bloated box reaches through thin walls without it.
    /// </summary>
    private bool CanBeTouchedBy(EntityHandle player)
    {
        Vector from = Entities.GetSolidType(Handle) == SolidType.VPhysics ? Origin : Entities.GetWorldSpaceCenter(Handle);
        EntityHullTrace tr = PickupWorld.EntityTraceHull(Handle, from, Entities.GetEyePosition(player), Vector.Zero, Vector.Zero,
                                                         MaskSolid, GameMovement.CollisionGroupPlayerMovement);
        return tr.Fraction >= 1f || tr.Entity == player;
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

        // CItem::ItemTouch: a player only, with nothing in the way, and the
        // interaction said before whether there was anything to give.
        if (_taken || !Entities.IsPlayer(toucher))
            return;

        if (CanBeTouchedBy(toucher))
            OnCacheInteraction.Fire(toucher);
        else if (SightGatesPickup)
            return;

        if (!TryGive(toucher))
            return;

        _taken = true;
        OnPlayerTouch.Fire();

        // Hidden rather than removed when it will come back: a mapper may have
        // wired outputs to this entity, and those connections were bound when
        // the map loaded and cannot be rebuilt.
        if (RespawnDelay > 0f)
        {
            // CItem::Respawn: no physics object while it waits, and back where
            // the map put it.
            SetPickupEnabled(false);
            Entities.DestroyPhysics(Handle);
            Origin = SpawnOrigin;
            SetAngles(SpawnAngles);
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
        // CItem::Materialize: visible, touchable and physical again.
        _taken = false;
        SetPickupEnabled(true);
        BecomePhysical();
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
