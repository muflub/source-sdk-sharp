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
/// What became of a request to change weapons.
///
/// <para>
/// A named outcome rather than a bool, because the interesting cases are all
/// refusals and they want different answers from whoever asked. A switch used
/// to return true or false and every caller threw the answer away, so a
/// scripted test driving <c>ss_slot</c> at a corpse saw the same nothing as one
/// driving it at a player who simply already held that weapon — and "weapon
/// switching is flaky" was the only report that could be made.
/// </para>
/// </summary>
public enum SwitchOutcome
{
    /// <summary>The only success that changed anything.</summary>
    Switched,

    /// <summary>Asked for the weapon already in the hands. Nothing to do, and not a fault.</summary>
    AlreadyHeld,

    /// <summary>
    /// The player is dead. Refused deliberately: death strips the inventory, so
    /// there is nothing to switch to — see <c>Arsenal.Refusal</c>.
    /// </summary>
    PlayerIsDead,

    /// <summary>Alive and holding nothing. A spawn that has not been given a loadout yet.</summary>
    NothingCarried,

    /// <summary>No inventory record at all — not a player this arsenal serves.</summary>
    NotArmed,

    /// <summary>Named a weapon this player does not own.</summary>
    NotOwned,

    /// <summary>Named a slot outside the inventory.</summary>
    NoSuchSlot,

    /// <summary>lastinv, with nothing held before this — or the weapon it named has gone.</summary>
    NoHistory,

    /// <summary>
    /// The weapon in the hands refused to be put away: stock's CanHolster,
    /// which the physcannon answers no to while it holds something, the RPG while its missile flies.
    /// </summary>
    CannotHolster,
}

/// <summary>
/// Who owns which weapons, with how much ammunition — the inventory AUTHORITY,
/// in C# (Phase 10).
///
/// <para>
/// Stock keeps this as m_hMyWeapons and m_iAmmo on CBasePlayer, forty-eight
/// entity slots and a hundred-and-something integers per player, because in
/// stock every owned weapon IS an entity. Here the inventory is data — a list
/// of owned types with their clips, and ammo pools by name — and exactly two
/// entities exist per armed player: <see cref="SsWeapon"/>, which is whatever
/// is in their hands, and <see cref="SsViewModel"/>, which is what they see of
/// it. A switch moves the identity, not the entity.
/// </para>
///
/// <para>
/// The per-command machine (<see cref="WeaponLogic"/>) runs against the
/// weapon's DECLARED state, which is the wire format and the prediction schema
/// at once; this class is everything around that — giving, switching,
/// dropping, and keeping the pools honest between commands.
/// </para>
/// </summary>
public sealed class Arsenal : IWeaponGiver, IGameLifecycle, IFrameLifecycle, ISaveCustom, IClientLifecycle
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    //-------------------------------------------------------------------------
    // Order 60 -- LAST, and it does two things rather than one: it drops what
    // players were carrying and then precaches every weapon the map may hand
    // out. Both were separate lines in ServerModule.LevelInit and they stay in
    // that order here, because precaching into a table you are about to clear
    // would be work thrown away.
    //-------------------------------------------------------------------------
    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.Arsenal;

    void IGameLifecycle.OnLevelInit(string mapName)
    {
        Reset();
        PrecacheAll();
    }

    // Inventories hold entity handles, and handles do not survive the map. The
    // arsenal is reset at BOTH ends of a level for that reason and always was:
    // once on the way out so nothing holds a stale handle across the gap, and
    // once on the way in beside the precache.
    void IGameLifecycle.OnLevelShutdown() => Reset();

    // After the soundscapes -- the order the two lines had in ServerModule.Frame.
    int IFrameLifecycle.FrameOrder => (int)FrameStage.Arsenal;

    void IFrameLifecycle.OnThink(float frameTime) => Frame();

    private readonly IEngineEntities _entities;
    private readonly IEnginePrecache _precache;
    private readonly IEnginePlayers _players;
    private readonly IEngineWorldQuery _world;
    private readonly IEngineSound _sound;
    private readonly IEngineEffects _effects;
    private readonly IEngineDamage _damage;
    private readonly IEngineEntityRender _render;
    private readonly IEntityDirectory _directory;
    private readonly IGameClock _clock;
    private readonly DamageHitGroups _hitGroups;

    // Stock's prediction filtering (PredictionSuppression) and the weapon sound
    // that obeys it. Null in a composition without them: every copy then
    // reaches everyone, the shooter included, as before the client predicted.
    private readonly PredictionSuppression? _prediction;
    private readonly IWeaponSounds? _weaponSounds;

    /// <summary>The weapons' scripts, whose SoundData the precache reads; none in a test that names no files.</summary>
    private readonly WeaponScriptDatabase? _scripts;

    // The side's tables, injected (see the constructor): the ONLY way this class
    // reaches a weapon or ammo row now. The static tables behind them hold the
    // CORE rows; these hold the core rows plus whatever the loaded mods appended,
    // and the server's inventory, the client's prediction and the wire index all
    // have to be reading one list — which a static per side can be but an
    // injected service at least makes testable.
    private readonly WeaponCatalog _weapons;
    private readonly AmmoCatalog _ammo;
    private readonly WeaponBehaviours _behaviours;

    /// <param name="weapons">
    /// This side's weapon table — the core rows plus the mods'. Null takes the
    /// stock table, which is what a composition with no mod assemblies registers
    /// (and a test's arsenal): the same rows the statics hold, through the same
    /// object the game uses, so no test reads a table the game does not.
    /// </param>
    /// <param name="ammo">This side's ammunition table; null takes the stock one.</param>
    /// <param name="behaviours">
    /// The behaviour table the per-player machines resolve their rows' keys
    /// against; null takes the empty one, which is stock for every weapon.
    /// </param>
    public Arsenal(ILog log, IEngineEntities entities, IEnginePrecache precache,
                   IEnginePlayers players, IEngineWorldQuery world,
                   IEngineSound sound, IEngineEffects effects,
                   IEngineDamage damage, IEngineEntityRender render,
                   IEntityDirectory directory, IGameClock clock,
                   DamageHitGroups hitGroups,
                   IUserMessageSender? messages = null,
                   WeaponCatalog? weapons = null, AmmoCatalog? ammo = null,
                   WeaponBehaviours? behaviours = null,
                   PredictionSuppression? prediction = null,
                   IWeaponSounds? weaponSounds = null,
                   WeaponScriptDatabase? scripts = null)
    {
        _scripts = scripts;
        _messages = messages;
        _prediction = prediction;
        _weaponSounds = weaponSounds;

        _log = log;
        _entities = entities;
        _precache = precache;
        _players = players;
        _world = world;
        _sound = sound;
        _effects = effects;
        _damage = damage;
        _render = render;
        _directory = directory;
        _clock = clock;
        _weapons = weapons ?? WeaponCatalog.Stock();
        _ammo = ammo ?? AmmoCatalog.Stock();
        _behaviours = behaviours ?? WeaponBehaviours.Stock();
        _hitGroups = hitGroups;
    }

    /// <summary>
    /// Told about every projectile as it leaves, exactly as the old shared
    /// logic's hook was: the module attributes the throw to its thrower.
    ///
    /// <para>
    /// The fourth argument is the FUSE in seconds — the firing weapon's answer,
    /// carried through the launch instead of looked up by the receiver, because
    /// a fuse belongs to the gun that fired it and not to the entity class that
    /// flies. Zero is "the stock path declared none" and lets the module's
    /// default stand; a behaviour that launched through
    /// <see cref="IWeaponArms.LaunchWith"/> named its own.
    /// </para>
    /// </summary>
    public Action<EntityHandle, EntityHandle, Vector, float>? Launched { get; set; }

    /// <summary>
    /// The side's temp entities, for the named client effects a weapon
    /// dispatches itself (the stunstick's <c>StunstickImpact</c>, its draw's
    /// sparks). Set by the composition once they exist, which is after this
    /// class is built — the same late door <see cref="Launched"/> is. Null
    /// (a test's arsenal, or before the composition reaches them) sends none.
    /// </summary>
    public ITempEnts? Temps { get; set; }

    /// <summary>
    /// The weapon table this arsenal gives from — the one list this side reads,
    /// exposed so the decorator and the console report answer "do we have that
    /// weapon" from the arsenal's own answer rather than a second lookup.
    /// </summary>
    public WeaponCatalog Weapons => _weapons;

    /// <summary>The ammunition table this arsenal fills pools from.</summary>
    public AmmoCatalog Ammo => _ammo;

    //-------------------------------------------------------------------------
    // One player's holdings.
    //-------------------------------------------------------------------------

    internal sealed class Owned
    {
        public int TypeIndex;
        public int Clip;

        // The weapon's own mode word and its clock (WeaponNetState.Special),
        // kept here while it is holstered as the clip is: the SLAM's flags live
        // on stock's weapon entity and survive a switch.
        public int Special;
        public float SpecialTime;
    }

    internal sealed class Carry
    {
        public EntityHandle Player;
        public EntityHandle Weapon;     // ss_weapon, or None until first give
        public EntityHandle ViewModel;  // ss_viewmodel, beside it
        public readonly List<Owned> Weapons = new();
        public readonly Dictionary<string, int> Ammo = new(StringComparer.OrdinalIgnoreCase);
        public int ActiveSlot = -1;

        // What was in the hands before, as a WEAPON TYPE rather than a slot.
        // Slots move: dropping the active weapon takes a row out of the list and
        // every slot after it shifts down, so a remembered index would quietly
        // start naming a different gun. -1 means nothing to go back to.
        public int LastTypeIndex = -1;
        public WeaponLogic Machine = null!;
        public ServerWeaponArms Arms = null!;

        // The RPG's: the missile it is waiting on (m_hMissile) and its laser
        // dot (m_hLaserDot). None when there is neither.
        public EntityHandle Missile = EntityHandle.None;
        public EntityHandle LaserDot = EntityHandle.None;
    }

    private readonly Dictionary<uint, Carry> _carries = new();

    /// <summary>
    /// Whether a handle is a hierarchy player — the only kind this serves.
    ///
    /// <para>
    /// <b>A DICTIONARY LOOKUP, NOT A CROSSING</b> — plan_crossings.md's D19
    /// residue, the <c>EntityGetClassName</c> row. It asked the shell for the
    /// classname and compared it to <c>ss_player</c>; the registry that answers
    /// it now is the same object that already drives this entity's spawn, its
    /// keyvalues, its thinks, its inputs and its outputs, so there is no state
    /// here that did not already have to be right for the player to exist at
    /// all. An <c>SsPlayer</c> instance IS what "the classname is ss_player"
    /// means on this side: <see cref="ScriptedEntityAttribute"/> binds the two,
    /// and the registry is keyed by the same push
    /// (<c>OnScriptedSpawn</c>/<c>OnScriptedRemoved</c>) that X5's liveness
    /// mirror rides.
    /// </para>
    ///
    /// <para>
    /// MEASURED: 6.000 <c>EntityGetClassName</c> crossings per server frame
    /// while a player stands on an <c>item_ammo_*</c> — one per touch, through
    /// <c>PickupItem.OnTouch</c> -> <c>TryGive</c> -> <c>GiveAmmo</c> -> this.
    /// The slot is KEPT as the divergence reference; <c>cs_sv_player_mirror</c>
    /// is the gate that compares the two answers on a running game.
    /// </para>
    /// </summary>
    internal bool IsStandalonePlayer(EntityHandle player)
        => _directory.Get(player) is SsPlayer;

    // What tells a player their ammo was refused (AmmoDenied). Null for an
    // arsenal built without one (a test's).
    private readonly IUserMessageSender? _messages;

    private Carry CarryFor(EntityHandle player)
    {
        if (_carries.TryGetValue(player.Value, out var carry))
            return carry;

        carry = new Carry { Player = player };
        carry.Arms = new ServerWeaponArms(_log, this, carry);
        carry.Machine = new WeaponLogic(carry.Arms, _weapons, _behaviours);
        _carries[player.Value] = carry;
        return carry;
    }

    //-------------------------------------------------------------------------
    // Giving.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Gives a weapon by classname. A player who already owns one gets its
    /// full clip's worth into their reserve instead — stock's touch-a-duplicate
    /// rule — and false means nothing changed hands at all.
    /// </summary>
    public bool GiveWeapon(EntityHandle player, string className)
    {
        if (!IsStandalonePlayer(player))
            return false;

        // A dead player's hands take nothing. The live bug this guards: death
        // drops the active weapon as a pickup AT the corpse, and the corpse's
        // own touch took it straight back — the dead player kept their weapon,
        // the HUD kept painting it, and the respawn inherited it on top of the
        // loadout. Spawn clears deadflag BEFORE the loadout hooks run
        // (shell_standalone_player.cpp), so a fresh spawn is alive here.
        if (!_players.IsAlive(player))
            return false;

        int typeIndex = _weapons.IndexOf(className);

        if (_weapons.ByIndex(typeIndex) is not WeaponType type)
        {
            _log.Warning($"[SourceSharp/Arsenal] no weapon row for '{className}'\n");
            return false;
        }

        var carry = CarryFor(player);

        foreach (var owned in carry.Weapons)
        {
            if (owned.TypeIndex != typeIndex)
                continue;

            // Already owned: the duplicate is worth what a fresh one carries
            // (Weapon_EquipAmmoOnly), its default clip, in ammunition.
            bool tookPrimary = type.AmmoName.Length > 0 && GiveAmmo(player, DefaultClipOf(in type), type.AmmoName) > 0;

            // And its secondary charge, where the row brings one: the SLAM's
            // three, Weapon_EquipAmmoOnly's GetSecondaryAmmoCount.
            bool tookSecondary = type.DefaultSecondaryAmmo > 0
                              && GiveAmmo(player, type.DefaultSecondaryAmmo, type.SecondaryAmmoName) > 0;

            return tookPrimary || tookSecondary;
        }

        // Weapon_Equip: the default clip into the clip, what it holds past the
        // clip's size into the pool, and all of it into the pool for a weapon
        // with no clip.
        int clipSize = ClipSizeOf(in type);
        int defaultClip = DefaultClipOf(in type);
        carry.Weapons.Add(new Owned
        {
            TypeIndex = typeIndex,
            Clip = clipSize > 0 ? Math.Min(defaultClip, clipSize) : 0,
            Special = type.Kind == WeaponKind.Slam ? SlamBits.Initial : 0,
        });

        if (type.AmmoName.Length > 0)
        {
            if (clipSize <= 0)
                GiveAmmo(player, defaultClip, type.AmmoName);
            else if (defaultClip > clipSize)
                GiveAmmo(player, defaultClip - clipSize, type.AmmoName);
        }

        // A secondary pool with no clip in front of it is filled with the
        // row's default_clip2 (Weapon_Equip: GiveAmmo( GetDefaultClip2() )).
        if (type.DefaultSecondaryAmmo > 0)
            GiveAmmo(player, type.DefaultSecondaryAmmo, type.SecondaryAmmoName);

        // The first weapon deploys itself; later ones wait to be asked for,
        // which is HL2MP's own behaviour with everything but a better weapon.
        if (carry.ActiveSlot < 0)
            Deploy(carry, carry.Weapons.Count - 1);
        else
            RefreshReserves(carry);

        return true;
    }

    /// <summary>
    /// Adds to a pool, honouring the ammo row's carry limit. Answers how much
    /// was actually taken, zero for a full pool — which is what leaves the box
    /// on the floor.
    /// </summary>
    public int GiveAmmo(EntityHandle player, int count, string ammoName)
    {
        if (!IsStandalonePlayer(player) || count <= 0)
            return 0;

        // Same rule as GiveWeapon: a corpse collects nothing.
        if (!_players.IsAlive(player))
            return 0;

        if (_ammo.For(ammoName) is not AmmoType ammo)
        {
            _log.Warning($"[SourceSharp/Arsenal] no ammo row for '{ammoName}'\n");
            return 0;
        }

        var carry = CarryFor(player);

        carry.Ammo.TryGetValue(ammo.Name, out int have);

        int taken = Math.Min(count, Math.Max(0, ammo.MaxCarry - have));

        if (taken <= 0)
        {
            // CHL2_Player::GiveAmmo: "we've been denied the pickup, display a
            // hud icon to show that" -- reliable, to this player alone.
            _messages?.Send(new AmmoDenied { AmmoType = (short)_ammo.WireIndex(ammo.Name) },
                            _entities.GetIndex(player), reliable: true);
            return 0;
        }

        carry.Ammo[ammo.Name] = have + taken;
        RefreshReserves(carry);
        return taken;
    }

    //-------------------------------------------------------------------------
    // Switching.
    //-------------------------------------------------------------------------

    /// <summary>Switches to an owned weapon by classname.</summary>
    public SwitchOutcome Switch(EntityHandle player, string className)
    {
        if (Refusal(player, out var carry) is SwitchOutcome refused)
            return refused;

        int typeIndex = _weapons.IndexOf(className);

        for (int slot = 0; slot < carry!.Weapons.Count; slot++)
        {
            if (carry.Weapons[slot].TypeIndex == typeIndex)
                return Take(carry, slot);
        }

        return SwitchOutcome.NotOwned;
    }

    /// <summary>Switches to the slot'th owned weapon (0-based).</summary>
    public SwitchOutcome SwitchSlot(EntityHandle player, int slot)
    {
        if (Refusal(player, out var carry) is SwitchOutcome refused)
            return refused;

        if (slot < 0 || slot >= carry!.Weapons.Count)
            return SwitchOutcome.NoSuchSlot;

        return Take(carry, slot);
    }

    /// <summary>The next owned weapon after the active one, wrapping.</summary>
    public SwitchOutcome SwitchNext(EntityHandle player)
    {
        if (Refusal(player, out var carry) is SwitchOutcome refused)
            return refused;

        // ActiveSlot is -1 with empty hands, and -1 + 1 is the first weapon,
        // which is the right place for "next" to start from.
        return Take(carry!, (carry!.ActiveSlot + 1) % carry.Weapons.Count);
    }

    /// <summary>
    /// The owned weapon BEFORE the active one, wrapping.
    ///
    /// A separate direction rather than a second name for
    /// <see cref="SwitchNext"/>, which is what it used to be: invprev forwarded
    /// the same command invnext did, and with two weapons carried the two are
    /// the same move — so the mod played correctly right up until a third
    /// weapon was picked up.
    /// </summary>
    public SwitchOutcome SwitchPrev(EntityHandle player)
    {
        if (Refusal(player, out var carry) is SwitchOutcome refused)
            return refused;

        int count = carry!.Weapons.Count;

        // Empty hands go to the LAST weapon, which is where stepping backwards
        // from nothing lands — the mirror of "next" starting at the first.
        int from = carry.ActiveSlot < 0 ? count : carry.ActiveSlot;

        return Take(carry, (from - 1 + count) % count);
    }

    /// <summary>
    /// Back to whatever was in the hands before this, which is what the name
    /// lastinv has always meant and what the q bind in cfg/config.cfg expects.
    /// </summary>
    public SwitchOutcome SwitchLast(EntityHandle player)
    {
        if (Refusal(player, out var carry) is SwitchOutcome refused)
            return refused;

        if (carry!.LastTypeIndex < 0)
            return SwitchOutcome.NoHistory;

        for (int slot = 0; slot < carry.Weapons.Count; slot++)
        {
            if (carry.Weapons[slot].TypeIndex == carry.LastTypeIndex)
                return Take(carry, slot);
        }

        // The weapon it named has been dropped or stripped since. Not a failure
        // of the command — there is genuinely nothing to go back to any more.
        return SwitchOutcome.NoHistory;
    }

    /// <summary>
    /// Everything the four switches refuse for the same reasons, asked once.
    ///
    /// Null means go ahead, and <paramref name="carry"/> is then non-null with
    /// at least one weapon in it.
    /// </summary>
    private SwitchOutcome? Refusal(EntityHandle player, out Carry? carry)
    {
        if (!_carries.TryGetValue(player.Value, out carry))
            return SwitchOutcome.NotArmed;

        // A dead player's switch is refused, and that is correct rather than a
        // limitation: death strips the inventory (Strip, from the death
        // cleanup), so there is nothing to switch TO. It is answered as its own
        // outcome rather than falling through to "carries nothing" because the
        // two want different responses from whoever asked — one is "wait for
        // the respawn", the other is "you were never given anything" — and a
        // script driving switches needs to be able to tell them apart.
        if (!_players.IsAlive(player))
            return SwitchOutcome.PlayerIsDead;

        if (carry.Weapons.Count == 0)
            return SwitchOutcome.NothingCarried;

        return null;
    }

    /// <summary>Deploys a slot that has already been checked, or says it was already held.</summary>
    private SwitchOutcome Take(Carry carry, int slot)
    {
        if (slot == carry.ActiveSlot)
            return SwitchOutcome.AlreadyHeld;

        if (ActiveBehaviour(carry, out var context) is { } behaviour && !behaviour.CanHolster(in context))
            return SwitchOutcome.CannotHolster;

        Deploy(carry, slot);
        return SwitchOutcome.Switched;
    }

    /// <summary>
    /// Puts a weapon in the hands: stores the outgoing clip, loads the incoming
    /// one, points both entities at the new models, and starts the draw.
    /// </summary>
    private void Deploy(Carry carry, int slot)
    {
        EnsureEntities(carry);

        if (_directory.Get(carry.Weapon) is not SsWeapon weapon ||
            _directory.Get(carry.ViewModel) is not SsViewModel viewModel)
            return;

        // The outgoing weapon is put away (its Holster), and keeps what was
        // left in its clip.
        Holster(carry);
        StoreActiveClip(carry, weapon);

        var owned = carry.Weapons[slot];

        if (_weapons.ByIndex(owned.TypeIndex) is not WeaponType type)
            return;

        // What is being put away, for lastinv. Recorded before ActiveSlot moves,
        // and only for a real change: asking for the weapon already in the hands
        // must not make THAT the thing to go back to.
        if (carry.ActiveSlot >= 0 && carry.ActiveSlot != slot &&
            carry.ActiveSlot < carry.Weapons.Count)
        {
            carry.LastTypeIndex = carry.Weapons[carry.ActiveSlot].TypeIndex;

            // The outgoing weapon's Holster: the stunstick's SetStunState( false ).
            if (_weapons.ByIndex(carry.LastTypeIndex) is WeaponType outgoing)
                carry.Arms.Sound(outgoing.HolsterSound);
        }

        carry.ActiveSlot = slot;

        float now = _clock.Now;

        weapon.State.OwnerIndex = _entities.GetIndex(carry.Player);
        weapon.State.WeaponTypeId = owned.TypeIndex;
        weapon.State.Clip = ClipSizeOf(in type) > 0 ? owned.Clip : -1;
        weapon.State.InReload = false;
        weapon.State.NextPrimaryAttack = now + type.DeployTime;
        weapon.State.NextSecondaryAttack = now + type.DeployTime;
        weapon.State.TimeWeaponIdle = now + type.DeployTime;
        weapon.State.LastButtons = 0;
        weapon.State.Special = owned.Special;
        weapon.State.SpecialTime = owned.SpecialTime;

        // CWeaponFrag::Deploy: no redraw owed, no pin out, and a throw whose
        // sequence the switch cut short never goes off.
        weapon.State.Redraw = false;
        weapon.State.DrawbackFinished = false;
        weapon.State.AnimEvent = 0;

        viewModel.State.OwnerIndex = _entities.GetIndex(carry.Player);

        // Through the injected interface rather than the entities' own
        // protected SetModel — the same door Pickups dresses through — so what
        // a switch dresses is observable: the live bug this guards against was
        // precisely a viewmodel still wearing the OLD weapon's v_ model.
        _render.SetModel(carry.Weapon, type.WorldModel);
        _render.SetModel(carry.ViewModel, type.ViewModel);
        _render.SetVisible(carry.Weapon, true);
        _render.SetVisible(carry.ViewModel, true);

        RefreshReserves(carry);

        // The draw, through the same path every other animation takes, so the
        // sequence number that lands in the blob is the one the client plays —
        // the row's machine's own draw where it picks one (the SLAM's), else
        // the generic one.
        if (!carry.Machine.Deploy(weapon.State, viewModel.State, now))
            carry.Arms.SendDeployAnim(viewModel.State);

        // The incoming weapon's own Deploy: the stunstick's SetStunState( true ).
        carry.Arms.Sound(type.DeploySound);

        if (type.SparksOnDeploy)
            carry.Arms.DeploySparks();

        // And the weapon's own Deploy tail, over the timers just set.
        if (ActiveBehaviour(carry, out var deployed) is { } behaviour)
            behaviour.Deploy(in deployed);

        _log.Msg($"[SourceSharp/Arsenal] {carry.Player} deployed {type.Name}\n");
    }

    /// <summary>
    /// The active weapon's behaviour and the context its hooks are asked
    /// with, or null for a stock weapon or empty hands.
    /// </summary>
    private IWeaponBehaviour? ActiveBehaviour(Carry carry, out WeaponAttackContext context)
    {
        context = default;

        if (carry.ActiveSlot < 0 || carry.ActiveSlot >= carry.Weapons.Count)
            return null;

        if (_directory.Get(carry.Weapon) is not SsWeapon weapon ||
            _directory.Get(carry.ViewModel) is not SsViewModel viewModel)
            return null;

        if (_weapons.ByIndex(weapon.State.WeaponTypeId) is not WeaponType type ||
            _behaviours.For(type.Behaviour) is not { } behaviour)
            return null;

        context = new WeaponAttackContext(type, weapon.State, viewModel.State, _clock.Now, true, carry.Arms);
        return behaviour;
    }

    /// <summary>The active weapon's Holster, before it leaves the hands for any reason.</summary>
    private void Holster(Carry carry)
    {
        if (ActiveBehaviour(carry, out var context) is { } behaviour)
            behaviour.Holster(in context);
    }

    /// <summary>Writes the active weapon's live clip back into its inventory slot.</summary>
    private void StoreActiveClip(Carry carry, SsWeapon weapon)
    {
        if (carry.ActiveSlot < 0 || carry.ActiveSlot >= carry.Weapons.Count)
            return;

        var owned = carry.Weapons[carry.ActiveSlot];

        if (_weapons.ByIndex(owned.TypeIndex) is WeaponType type && ClipSizeOf(in type) > 0)
            owned.Clip = Math.Max(0, weapon.State.Clip);

        owned.Special = weapon.State.Special;
        owned.SpecialTime = weapon.State.SpecialTime;
    }

    /// <summary>
    /// The clip a row holds here: the row's own, unless its behaviour answers
    /// <see cref="IWeaponBehaviour.ModifyClipSize"/> — the same answer the
    /// machine spends down from and reloads up to, so a fresh give fills the
    /// clip a reload fills.
    /// </summary>
    /// <summary>The script's default_clip, or the clip's size, or one round for a weapon with no clip.</summary>
    private int DefaultClipOf(in WeaponType type)
    {
        if (type.DefaultClip > 0)
            return type.DefaultClip;
        int clipSize = ClipSizeOf(in type);
        return clipSize > 0 ? clipSize : 1;
    }

    private int ClipSizeOf(in WeaponType type)
        => _behaviours.For(type.Behaviour) is { } behaviour
            ? behaviour.ModifyClipSize(in type, type.ClipSize)
            : type.ClipSize;

    /// <summary>
    /// Creates the two carried entities the first time a player needs them.
    /// </summary>
    private void EnsureEntities(Carry carry)
    {
        if (carry.Weapon == EntityHandle.None || !_entities.IsValid(carry.Weapon))
        {
            carry.Weapon = _entities.Create(EntityNames.SsWeapon);
            _entities.SetOrigin(carry.Weapon, _entities.GetOrigin(carry.Player));
            _entities.Spawn(carry.Weapon);
        }

        if (carry.ViewModel == EntityHandle.None || !_entities.IsValid(carry.ViewModel))
        {
            carry.ViewModel = _entities.Create(EntityNames.SsViewModel);
            _entities.SetOrigin(carry.ViewModel, _entities.GetOrigin(carry.Player));
            _entities.Spawn(carry.ViewModel);

            // Sent to everyone, not PVS-checked. The viewmodel's PVS box is
            // built from its v_ model's bounds, and v_ models are authored in
            // CAMERA space — the box lands wherever the artist left it, which
            // for v_pistol is entirely below the floor. The first live switch
            // proved what that does: the engine drops the entity from the
            // snapshot mid-switch, the client's copy goes dormant holding the
            // OLD weapon's model, and it never recovers because a dormant
            // entity receives nothing. The stock analogue transmits a
            // viewmodel with its owner; ALWAYS is the door this hierarchy
            // has, and the cost is one small entity per armed player.
            _render.SetTransmitAlways(carry.ViewModel, true);
        }
    }

    /// <summary>
    /// Mirrors the pools into the active weapon's blob, which is where the
    /// machine spends from and what the client predicts a reload against.
    /// </summary>
    private void RefreshReserves(Carry carry)
    {
        if (_directory.Get(carry.Weapon) is not SsWeapon weapon)
            return;

        if (_weapons.ByIndex(weapon.State.WeaponTypeId) is not WeaponType type)
            return;

        weapon.State.Reserve = PoolOf(carry, type.AmmoName);
        weapon.State.SecondaryReserve = PoolOf(carry, type.SecondaryAmmoName);
    }

    private static int PoolOf(Carry carry, string ammoName)
    {
        if (ammoName.Length == 0)
            return 0;

        carry.Ammo.TryGetValue(ammoName, out int have);
        return have;
    }

    //-------------------------------------------------------------------------
    // The per-command run (Phase 9c4's crossing, phase 1).
    //-------------------------------------------------------------------------

    /// <summary>
    /// One command's worth of weapon behaviour for one player, after their
    /// movement ran. The machine reads and writes the declared blobs; this
    /// wrapper settles the pools afterwards, because the machine's Reserve is a
    /// snapshot and the pool is the authority.
    /// </summary>
    public void OnPlayerCommand(EntityHandle player, in UserCmd cmd, bool firstTime)
    {
        if (!_carries.TryGetValue(player.Value, out var carry) || carry.ActiveSlot < 0)
            return;

        if (_directory.Get(carry.Weapon) is not SsWeapon weapon ||
            _directory.Get(carry.ViewModel) is not SsViewModel viewModel)
            return;

        // A dead player's trigger does nothing; the machine never learns the
        // buttons, so a respawn does not fire the shot that killed them.
        if (!_players.IsAlive(player))
            return;

        int before = weapon.State.Reserve;
        int beforeSecondary = weapon.State.SecondaryReserve;

        // The command's random seed, which the predicting client derives from
        // the same command number: the pellets spread from it on both sides.
        RocketDiedIfGone(carry, weapon, viewModel);

        carry.Arms.CommandRandomSeed = UserCmdCodec.RandomSeedFor(cmd.CommandNumber);

        // CBasePlayer::PhysicsSimulate's SuppressHostEvents( this ) when
        // IsPredictingWeapons(): the shooter's client plays this command's
        // weapon sounds and impacts itself, so the server's copies skip it.
        int index = _entities.GetIndex(player);
        using (_prediction is not null && _prediction.PredictsWeapons(index)
                   ? _prediction.Suppress(index)
                   : default)
        {
            carry.Machine.RunCommand(weapon.State, viewModel.State,
                                     cmd.Buttons, _clock.Now, effectsLive: firstTime);
        }

        // What the machine spent comes out of the pools by name.
        SettlePool(carry, weapon.State.WeaponTypeId, primary: true,
                   before - weapon.State.Reserve);
        SettlePool(carry, weapon.State.WeaponTypeId, primary: false,
                   beforeSecondary - weapon.State.SecondaryReserve);

        // A SLAM with no charges left and none out asked to go: stock's
        // Weapon_Drop( this ) and UTIL_Remove( this ) from its WeaponIdle.
        if (SlamBits.Has(weapon.State.Special, SlamBits.Spent) &&
            _weapons.ByIndex(weapon.State.WeaponTypeId) is { Kind: WeaponKind.Slam })
        {
            RemoveActive(carry);
        }
    }

    /// <summary>
    /// CWeaponRPG::NotifyRocketDied, at the first command after the missile
    /// stopped waiting on its launcher: it went off, was shot down, or is
    /// gone altogether (a level change, a reload, a removal). Stock's missile
    /// calls it from its Explode and ShotDown; the launcher here asks instead,
    /// so a missile that went without saying so cannot hold the RPG forever.
    /// </summary>
    private void RocketDiedIfGone(Carry carry, SsWeapon weapon, SsViewModel viewModel)
    {
        if (!RpgState.Has(weapon.State.Special, RpgState.MissileOut) ||
            _weapons.ByIndex(weapon.State.WeaponTypeId) is not WeaponType type ||
            type.Behaviour != Hl2mpWeaponBehaviours.Rpg)
            return;

        if (_directory.Get(carry.Missile) is Rocket { LauncherWaits: true })
            return;

        carry.Missile = EntityHandle.None;
        Hl2mpWeaponBehaviours.RocketDied(new WeaponAttackContext(type, weapon.State, viewModel.State,
                                                                 _clock.Now, EffectsLive: true, carry.Arms));
    }

    /// <summary>
    /// The active weapon is gone — removed, not dropped, so nothing is left in
    /// the world — and the hands take the next weapon, as
    /// <c>CBasePlayer::Weapon_Drop</c>'s <c>SwitchToNextBestWeapon</c> does:
    /// the one held before, if it is still owned, else the first.
    /// </summary>
    private void RemoveActive(Carry carry)
    {
        if (carry.ActiveSlot < 0 || carry.ActiveSlot >= carry.Weapons.Count)
            return;

        carry.Weapons.RemoveAt(carry.ActiveSlot);
        EmptyHands(carry);

        if (carry.Weapons.Count == 0)
            return;

        int next = carry.Weapons.FindIndex(o => o.TypeIndex == carry.LastTypeIndex);
        Deploy(carry, next >= 0 ? next : 0);
    }

    private void SettlePool(Carry carry, int typeIndex, bool primary, int spent)
    {
        if (spent == 0 || _weapons.ByIndex(typeIndex) is not WeaponType type)
            return;

        string name = primary ? type.AmmoName : type.SecondaryAmmoName;

        if (name.Length == 0)
            return;

        carry.Ammo.TryGetValue(name, out int have);
        carry.Ammo[name] = Math.Max(0, have - spent);
    }

    //-------------------------------------------------------------------------
    // The frame sweep: the carried entities ride their owner.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Once per server frame: parks each player's weapon and viewmodel at the
    /// player, so PVS and everyone else's view of the carrier stay honest. The
    /// client does the fine placement (bonemerge, eye position); the server
    /// only needs them in the right part of the world.
    /// </summary>
    public void Frame()
    {
        foreach (var carry in _carries.Values)
        {
            if (!_entities.IsValid(carry.Player))
                continue;

            Vector at = _entities.GetOrigin(carry.Player);

            if (_entities.IsValid(carry.Weapon))
                _entities.SetOrigin(carry.Weapon, at);
            if (_entities.IsValid(carry.ViewModel))
                _entities.SetOrigin(carry.ViewModel, at);
        }
    }

    //-------------------------------------------------------------------------
    // Leaving, dying, level changes.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Drops the active weapon where the player fell, as a pickup carrying the
    /// clip it had — killing somebody stays worth walking over.
    /// </summary>
    public void DropActive(EntityHandle player, Vector velocity)
    {
        if (!_carries.TryGetValue(player.Value, out var carry) || carry.ActiveSlot < 0)
            return;

        if (_directory.Get(carry.Weapon) is not SsWeapon weapon)
            return;

        // Whatever the weapon was doing ends here (the physcannon lets go),
        // and a weapon whose stock Drop removes it leaves nothing behind.
        bool leavesPickup = ActiveBehaviour(carry, out _) is not { LeavesPickup: false };
        Holster(carry);
        StoreActiveClip(carry, weapon);

        var owned = carry.Weapons[carry.ActiveSlot];

        // A row that is removed on a drop leaves nothing: CWeaponStunStick::Drop
        // is SetStunState( false ) and UTIL_Remove( this ).
        if (_weapons.ByIndex(owned.TypeIndex) is WeaponType type)
            carry.Arms.Sound(type.HolsterSound);

        if (leavesPickup && _weapons.ByIndex(owned.TypeIndex) is WeaponType { RemovedOnDrop: false } kept)
        {
            EntityHandle dropped = _entities.Create(kept.Name);

            // COMPARED AGAINST None RATHER THAN ASKED WHETHER IT IsValid, which
            // is the rule Players.OnKilled records: EntityHandle.IsValid asks
            // the STATIC engine, so it is right in a game and answers false
            // everywhere else -- which silently skipped this whole branch and
            // is why nothing had ever exercised the drop.
            if (dropped != EntityHandle.None)
            {
                _entities.SetOrigin(dropped, DropPoint(player));
                _entities.Spawn(dropped);
            }
        }

        carry.Weapons.RemoveAt(carry.ActiveSlot);

        // Empty hands until the next deploy re-arms them.
        EmptyHands(carry);
    }

    /// <summary>
    /// Where a dropped weapon comes to rest: on the floor under the player,
    /// rather than hanging in the air where they died.
    ///
    /// <para>
    /// THE BUG THIS FIXES was visible and had been for a while — a dropped
    /// crowbar standing in mid-air thirty-two units above a corpse. This
    /// method's predecessor was <c>GetOrigin(player) + (0, 0, 32)</c> and
    /// nothing else: a <see cref="WeaponPickup"/> is an Item, which has no
    /// physics object and no falling movetype, so wherever it is put is where
    /// it stays forever.
    /// </para>
    ///
    /// <para>
    /// A TRACE AND NOT A PHYSICS OBJECT, deliberately.
    /// <c>ConvertToPhysics</c> answers a NEW <c>prop_physics</c> — it replaces
    /// the entity rather than animating it — so a converted weapon would fall
    /// beautifully and could never be picked up again, which is the opposite
    /// of what dropping one is for. Stock gets both by putting the weapon on a
    /// gravity movetype, which this hierarchy's Item does not have.
    /// </para>
    ///
    /// <para>
    /// The lift comes FIRST and the trace starts from it, so a weapon dropped
    /// by a player standing on a crate lands on the crate rather than inside
    /// it. Recorded in docs/divergences.md: this lands, it does not tumble.
    /// </para>
    /// </summary>
    private Vector DropPoint(EntityHandle player)
    {
        Vector from = _entities.GetOrigin(player) + new Vector(0f, 0f, DropLift);

        TraceResult floor = _world.TraceLine(from, from - new Vector(0f, 0f, DropReach),
                                             TraceMask.Solid, player);

        // Nothing under them -- a pit, or the void. Left where it was thrown,
        // which is the old behaviour and the honest one: there is no floor to
        // put it on.
        return floor.Hit ? floor.End + new Vector(0f, 0f, DropClearance) : from;
    }

    /// <summary>How far above the player's feet a drop starts. Stock's own lift.</summary>
    private const float DropLift = 32f;

    /// <summary>How far down to look for a floor. A tall room, not the whole map.</summary>
    private const float DropReach = 4096f;

    /// <summary>
    /// A little off the floor, so the weapon's own bounds do not start
    /// intersecting it and the pickup trigger stays reachable.
    /// </summary>
    private const float DropClearance = 4f;

    /// <summary>
    /// Empties a player's hands and inventory — the respawn loadout starts
    /// clean, exactly as stock's RemoveAllItems on death.
    /// </summary>
    public void Strip(EntityHandle player)
    {
        if (!_carries.TryGetValue(player.Value, out var carry))
            return;

        Holster(carry);
        carry.Weapons.Clear();
        carry.Ammo.Clear();

        // Nothing to go back to either: the weapon lastinv remembered went with
        // the rest, and a respawn that inherited it would switch to a gun the
        // player no longer owns.
        carry.LastTypeIndex = -1;

        EmptyHands(carry);
    }

    /// <summary>
    /// Nothing in the hands, and nothing SHOWN in the hands. Death's other
    /// half: the two carried entities used to disappear on death by ACCIDENT
    /// (PVS dropped them), and the dormancy fix that marked the viewmodel
    /// transmit-always — correct for a live player — is exactly what now keeps
    /// a dead player's viewmodel on screen unless death says otherwise. So
    /// death says otherwise, deliberately: the weapon entity loses its type
    /// and its model; the viewmodel is DISOWNED (owner 0), which is the signal
    /// the client's presenter reads to take it out of the viewmodel pass; and
    /// both are hidden (EF_NODRAW) until <see cref="Deploy"/> dresses and shows
    /// them again. SetModel does not clear EF_NODRAW, so the show is explicit.
    /// </summary>
    private void EmptyHands(Carry carry)
    {
        carry.ActiveSlot = -1;

        // An RPG's dot does not outlast the hands that painted it.
        carry.Arms.UpdateLaserDot(exists: false, on: false);

        if (_directory.Get(carry.Weapon) is SsWeapon weapon)
        {
            weapon.State.WeaponTypeId = -1;
            weapon.State.OwnerIndex = _entities.GetIndex(carry.Player);
        }

        if (_directory.Get(carry.ViewModel) is SsViewModel viewModel)
            viewModel.State.OwnerIndex = 0;

        // Unconditionally rather than behind IsValid: the native door resolves
        // the handle itself and a handle that resolves to nothing hides
        // nothing, while an IsValid ask here is one more engine read for no
        // decision.
        _render.SetVisible(carry.Weapon, false);
        _render.SetVisible(carry.ViewModel, false);

    }

    /// <summary>Forgets a player and removes their carried entities.</summary>
    public void Forget(EntityHandle player)
    {
        if (!_carries.TryGetValue(player.Value, out var carry))
            return;

        Holster(carry);
        carry.Arms.UpdateLaserDot(exists: false, on: false);

        if (_entities.IsValid(carry.Weapon))
            _entities.Remove(carry.Weapon);
        if (_entities.IsValid(carry.ViewModel))
            _entities.Remove(carry.ViewModel);

        _carries.Remove(player.Value);
    }

    //-------------------------------------------------------------------------
    // A DEPARTING PLAYER (2026-10-05). Stock's CMultiplayRules::ClientDisconnected
    // (multiplay_gamerules.cpp): RemoveAllItems( true ) -- "destroy all of the
    // players weapons and items" -- then DestroyViewModels. Forget is exactly
    // that here and had no caller, so a departed player's ss_weapon and
    // ss_viewmodel stood in the world with their owner gone. Not called for a
    // level change, where the client stays (ClientLifecycleDriver says so).
    //-------------------------------------------------------------------------
    int IClientLifecycle.ClientLifecycleOrder => (int)LifecycleStage.Arsenal;

    void IClientLifecycle.OnClientDisconnected(int entIndex)
    {
        foreach (var carry in _carries.Values.ToArray())
        {
            if (carry.Player.EntryIndex == entIndex)
                Forget(carry.Player);
        }
    }

    //-------------------------------------------------------------------------
    // ACROSS A HOT RELOAD (2026-10-05). Every player's carry lived only in
    // _carries, which no reload carried: after cs_sv_reload every player owned
    // nothing in managed code -- weapons, clips, ammo pools, the active and the
    // last weapon -- while their ss_weapon and ss_viewmodel stood on. Touching
    // a weapon they already owned then took it as new and built a second pair
    // (the daily gate's "2 gained, a ss_weapon"). Weapons and ammo go by NAME,
    // never by table index: a build may reorder the table between the two.
    //-------------------------------------------------------------------------
    private const string RowKind = "kind";

    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (var carry in _carries.Values)
        {
            string active = carry.ActiveSlot >= 0 && carry.ActiveSlot < carry.Weapons.Count
                ? _weapons.ByIndex(carry.Weapons[carry.ActiveSlot].TypeIndex)?.Name ?? string.Empty
                : string.Empty;

            writer.AddRow()
                  .Set(RowKind, "carry")
                  .Set("p", carry.Player)
                  .Set("w", carry.Weapon)
                  .Set("v", carry.ViewModel)
                  .Set("a", active)
                  .Set("l", _weapons.ByIndex(carry.LastTypeIndex)?.Name ?? string.Empty);

            foreach (var owned in carry.Weapons)
            {
                writer.AddRow()
                      .Set(RowKind, "owned")
                      .Set("p", carry.Player)
                      .Set("t", _weapons.ByIndex(owned.TypeIndex)?.Name ?? string.Empty)
                      .Set("c", owned.Clip)
                      .Set("s", owned.Special);
            }

            foreach (var (ammo, count) in carry.Ammo)
            {
                writer.AddRow()
                      .Set(RowKind, "ammo")
                      .Set("p", carry.Player)
                      .Set("n", ammo)
                      .Set("c", count);
            }
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        var active = new Dictionary<uint, string>();

        foreach (ReloadRow row in reader.Rows)
        {
            EntityHandle player = reader.Resolve(row.GetHandle("p"));
            if (player == EntityHandle.None)
                continue;

            switch (row.GetString(RowKind))
            {
                case "carry":
                {
                    var carry = CarryFor(player);
                    carry.Weapons.Clear();
                    carry.Ammo.Clear();
                    carry.ActiveSlot = -1;
                    carry.Weapon = reader.Resolve(row.GetHandle("w"));
                    carry.ViewModel = reader.Resolve(row.GetHandle("v"));
                    carry.LastTypeIndex = _weapons.IndexOf(row.GetString("l"));
                    active[player.Value] = row.GetString("a");
                    break;
                }

                case "owned":
                {
                    int type = _weapons.IndexOf(row.GetString("t"));
                    if (type < 0)
                    {
                        _log.Warning($"[SourceSharp/Arsenal] reload: this build has no weapon " +
                                     $"'{row.GetString("t")}'; {player} no longer owns it\n");
                        break;
                    }

                    CarryFor(player).Weapons.Add(new Owned { TypeIndex = type, Clip = row.GetInt("c"), Special = row.GetInt("s") });
                    break;
                }

                case "ammo":
                    CarryFor(player).Ammo[row.GetString("n")] = row.GetInt("c");
                    break;
            }
        }

        // The active slot by TYPE, once every owned row is back: a slot index
        // written before an unknown type was dropped would name the wrong gun.
        foreach (var (player, name) in active)
        {
            if (!_carries.TryGetValue(player, out var carry) || name.Length == 0)
                continue;

            int type = _weapons.IndexOf(name);
            carry.ActiveSlot = carry.Weapons.FindIndex(o => o.TypeIndex == type);
        }
    }

    /// <summary>Level change: handles do not survive it, so nothing here does.</summary>
    public void Reset() => _carries.Clear();

    /// <summary>
    /// Everything a weapon can look like or sound like, ahead of the first
    /// shot. Stock's weapon scripts precached per weapon at spawn; the table
    /// is nine rows, so all of it goes in at level init — an unprecached fire
    /// sound is a console error per trigger pull and a silent gun.
    /// </summary>
    public void PrecacheAll()
    {
        foreach (var type in _weapons.All)
        {
            _precache.PrecacheModel(type.ViewModel);
            _precache.PrecacheModel(type.WorldModel);
            _precache.PrecacheSound(type.FireSound);
            _precache.PrecacheSound(type.EmptySound);
            _precache.PrecacheSound(type.ReloadSound);
            _precache.PrecacheSound(type.MeleeHitSound);
            _precache.PrecacheSound(type.DeploySound);
            _precache.PrecacheSound(type.HolsterSound);

            // The SLAM's machine names its own (CWeapon_SLAM::Precache).
            if (type.Kind == WeaponKind.Slam)
            {
                _precache.PrecacheSound(SlamMachine.ThrowSound);
                _precache.PrecacheSound(SlamMachine.DetonateSound);
                _precache.PrecacheSound(SlamMachine.TripMineModeSound);
            }

            if (type.Secondary is { Sound.Length: > 0 } secondary)
                _precache.PrecacheSound(secondary.Sound);

            // CBaseCombatWeapon::Precache: every sound the script's SoundData
            // names, the ones only a weapon's own code plays among them (the
            // crossbow's BoltFly and BoltElectrify, its SPECIAL1 and SPECIAL2).
            if (_scripts?.ReadScript(type.Name) is { } script)
                foreach (string sound in script.Sounds)
                    if (sound.Length > 0)
                        _precache.PrecacheSound(sound);
        }
    }

    /// <summary>One player's holdings, for the console report and the tests.</summary>
    public IReadOnlyList<(string ClassName, int Clip, bool Active)> Holdings(EntityHandle player)
    {
        var lines = new List<(string, int, bool)>();

        if (!_carries.TryGetValue(player.Value, out var carry))
            return lines;

        for (int slot = 0; slot < carry.Weapons.Count; slot++)
        {
            var owned = carry.Weapons[slot];
            string name = _weapons.ByIndex(owned.TypeIndex)?.Name ?? $"#{owned.TypeIndex}";

            // The active slot's clip lives in the weapon's blob while it is in
            // the hands; the stored copy is only settled on a switch. A report
            // that showed the stale one would claim a full clip mid-firefight.
            int clip = owned.Clip;

            if (slot == carry.ActiveSlot && _directory.Get(carry.Weapon) is SsWeapon live)
                clip = live.State.Clip;

            lines.Add((name, clip, slot == carry.ActiveSlot));
        }

        return lines;
    }

    /// <summary>Test seam and readout: the carried ss_weapon and ss_viewmodel, None each when there is none.</summary>
    public (EntityHandle Weapon, EntityHandle ViewModel) CarriedPair(EntityHandle player)
        => _carries.TryGetValue(player.Value, out var carry)
            ? (carry.Weapon, carry.ViewModel)
            : (EntityHandle.None, EntityHandle.None);

    /// <summary>The player's ammo pools, for the console report.</summary>
    public IReadOnlyDictionary<string, int> Pools(EntityHandle player)
        => _carries.TryGetValue(player.Value, out var carry)
            ? carry.Ammo
            : new Dictionary<string, int>();

    //-------------------------------------------------------------------------
    // The server's arms: what the machine's decisions DO.
    //-------------------------------------------------------------------------

    /// <summary>
    /// THE one projectile spawn, shared by the stock launch and by a behaviour's
    /// <see cref="IWeaponArms.LaunchWith"/>: create, place ahead of the eye,
    /// spawn, set the velocity — an impulse for a thrown one, the plain velocity
    /// for an aimed one — and report the launch with the fuse the gun declared.
    /// One body for both doors so they cannot drift: a mod's projectile that
    /// skipped the placement or the owner rule would hit its shooter or fly
    /// sideways, and the bug would live in whichever door nobody tested.
    /// </summary>
    private void LaunchFlying(ILog log, Carry carry, in WeaponType type,
                              string projectile, Vector velocity, bool thrown, float fuse)
    {
        EntityHandle flying = _entities.Create(projectile);

        if (!_entities.IsValid(flying))
        {
            log.Warning($"[SourceSharp/Arsenal] {type.Name} could not create '{projectile}'\n");
            return;
        }

        Vector from = _players.GetShootOrigin(carry.Player);
        var aim = Angles.Vectors(_entities.GetEyeAngles(carry.Player));

        // Position BEFORE spawning, because spawning is what reads it — and
        // the offset is so it does not appear inside the thrower.
        _entities.SetOrigin(flying, from + aim.Forward * WeaponLogic.LaunchOffset);
        _entities.Spawn(flying);

        if (thrown)
            _entities.ApplyVelocityImpulse(flying, velocity, false);

        Launched?.Invoke(flying, carry.Player, velocity, fuse);
    }

    /// <summary>
    /// CWeaponFrag's ThrowGrenade, LobGrenade and RollGrenade (weapon_frag.cpp,
    /// hl2mp) and Fraggrenade_Create: where each throw starts, pulled back to
    /// the eye (or the body's centre) when it would start in a wall, and the
    /// velocity it leaves with -- the thrower's own plus the throw's -- and its
    /// spin, which stock gives about the grenade's own axes. The timer goes to
    /// the grenade with the launch report.
    /// </summary>
    private void ThrowGrenade(ILog log, Carry carry, in WeaponType type, GrenadeThrow kind, float timer, Random random)
    {
        EntityHandle player = carry.Player;
        Vector eye = _players.GetShootOrigin(player);
        var aim = Angles.Vectors(_entities.GetEyeAngles(player));
        Vector thrower = _entities.GetVelocity(player);
        Vector from, velocity, spin, angles = Vector.Zero;

        switch (kind)
        {
            case GrenadeThrow.Throw:
                from = ThrowPosition(player, eye, eye + aim.Forward * 18f + aim.Right * 8f);
                velocity = thrower + (aim.Forward + new Vector(0f, 0f, 0.1f)) * 1200f;
                spin = new Vector(600f, random.Next(-1200, 1201), 0f);
                break;

            case GrenadeThrow.Lob:
                from = ThrowPosition(player, eye, eye + aim.Forward * 18f + aim.Right * 8f + new Vector(0f, 0f, -8f));
                velocity = thrower + aim.Forward * 350f + new Vector(0f, 0f, 50f);
                spin = new Vector(200f, random.Next(-600, 601), 0f);
                break;

            default:
            {
                // The bottom of the player's box, a grenade's radius up, rolled
                // the way the body faces, along the floor when there is one.
                from = _entities.GetOrigin(player) + new Vector(0f, 0f, GrenadeRadius);
                float yaw = _entities.GetEyeAngles(player).Y;
                float radians = yaw * MathF.PI / 180f;
                var facing = new Vector(MathF.Cos(radians), MathF.Sin(radians), 0f);

                // MASK_PLAYERSOLID in stock; the line trace takes the shot
                // hull's mask, which is the same solid set for a floor.
                TraceResult floor = _world.TraceLine(from, from - new Vector(0f, 0f, 16f), TraceMask.ShotHull, player);
                if (floor.Fraction != 1f)
                {
                    Vector tangent = facing.Cross(floor.Normal);
                    facing = floor.Normal.Cross(tangent);
                }

                from = ThrowPosition(player, _entities.GetWorldSpaceCenter(player), from + facing * 18f);
                velocity = thrower + facing * 700f;
                angles = new Vector(0f, _entities.GetAngles(player).Y, -90f);
                spin = new Vector(0f, 0f, 720f);
                break;
            }
        }

        EntityHandle grenade = _entities.Create(type.Projectile);

        if (!_entities.IsValid(grenade))
        {
            log.Warning($"[SourceSharp/Arsenal] {type.Name} could not create '{type.Projectile}'\n");
            return;
        }

        // CBaseEntity::Create( name, position, angles, owner ): placed, owned
        // (so it does not collide with its thrower), then spawned.
        _entities.SetOrigin(grenade, from);
        _entities.SetAngles(grenade, angles);
        _entities.SetOwner(grenade, player);
        _entities.Spawn(grenade);
        // Fraggrenade_Create's SetVelocity: AddVelocity( velocity, angVelocity )
        // on an object at rest, the spin in its own frame.
        _entities.ApplyVelocityImpulse(grenade, velocity, false);
        var axes = Angles.Vectors(angles);
        _entities.SetAngularVelocity(grenade, axes.Forward * spin.X - axes.Right * spin.Y + axes.Up * spin.Z);

        Launched?.Invoke(grenade, player, velocity, timer);
    }

    /// <summary>
    /// CheckThrowPosition: a box a little wider than the grenade swept from
    /// <paramref name="start"/> to where the throw wants it, stopped short of
    /// what it meets.
    /// </summary>
    private Vector ThrowPosition(EntityHandle player, Vector start, Vector wanted)
    {
        var extent = new Vector(GrenadeRadius + 2f, GrenadeRadius + 2f, GrenadeRadius + 2f);
        return _world.EntityTraceHull(player, start, wanted, extent * -1f, extent,
                                      GameMovement.MaskPlayerSolid, CollisionGroupPlayer).EndPos;
    }

    /// <summary>GRENADE_RADIUS, in inches.</summary>
    private const float GrenadeRadius = 4f;

    /// <summary>COLLISION_GROUP_PLAYER, the thrower's own group, which the throw's sweep is made as.</summary>
    private const int CollisionGroupPlayer = 5;

    internal sealed class ServerWeaponArms : IWeaponArms
    {
        // This class's log, handed in by its composition (statics to zero).
        private readonly ILog _log;

        private readonly Arsenal _arsenal;
        private readonly Carry _carry;

        internal ServerWeaponArms(ILog log, Arsenal arsenal, Carry carry)
        {

            _log = log;
            _arsenal = arsenal;
            _carry = carry;
        }

        /// <summary>
        /// The command's random seed (<c>CUserCmd::random_seed</c>), set by
        /// the arsenal before each run; the pellets spread from it.
        /// </summary>
        internal int CommandRandomSeed { get; set; }

        // The pellets' stream, re-seeded per pellet (ShotSpread).
        private readonly SeededUniformStream _spread = new();

        // A thrown grenade's spin (random->RandomInt in stock's throws): the
        // server's own draw, not the command's, as stock's is.
        private readonly Random _random = new();

        /// <summary>
        /// <c>CBaseCombatWeapon::WeaponSound</c>: from the owner, to the PAS of
        /// the owner, under the prediction rules -- the predicting shooter
        /// plays its own.
        /// </summary>
        public void Sound(string soundScript)
        {
            if (soundScript.Length == 0)
                return;

            if (_arsenal._weaponSounds is { } sounds)
                sounds.Emit(_carry.Player, soundScript);
            else
                _arsenal._sound.EntityEmitSound(_carry.Player, soundScript);
        }

        /// <summary>
        /// Hitscan fire: one trace per pellet from the eyes, damage from the
        /// ammo row, an IMPACT where the round lands, and blood where a player
        /// is hit. Stock's triangular spread sample (two uniform draws summed),
        /// so the pattern clusters centre the way HL2's does.
        ///
        /// <para>
        /// THE IMPACT IS STOCK'S <c>DoImpactEffect</c>, AND IT IS NOT OPTIONAL.
        /// <c>CBaseEntity::FireBullets</c> calls <c>DoImpactEffect( tr,
        /// nDamageType )</c> for every pellet that lands
        /// (<c>baseentity_shared.cpp:1986</c>), which is
        /// <c>UTIL_ImpactTrace</c>, which ends at
        /// <c>CBaseEntity::ImpactTrace</c> dispatching the effect named
        /// <c>"Impact"</c>. Nothing on this path ever did, and the omission was
        /// SILENT — the shape plan_entity.md §7 forbids. Measured live by
        /// `live-batch`: a crate at the aim point, rounds 90 -&gt; 63, sprite
        /// quads +2115, and both effect ledgers flat.
        /// </para>
        ///
        /// <para>
        /// EVERY LANDING ROUND AND NOT ONLY THOSE THAT NAMED AN ENTITY. A shot
        /// into a wall or a floor is the common case and it carries no entity
        /// handle at all; stock dispatches for it too — <c>UTIL_ImpactTrace</c>
        /// resolves such a trace to worldspawn and <c>CWorld::ImpactTrace</c>
        /// answers for it. The damage arm below is the one that needs a subject
        /// and it keeps its own guard.
        /// </para>
        ///
        /// <para>
        /// THE SURFACEPROP TRAVELS WITH THE SHOT, which is why this uses
        /// <see cref="IEngineWorldQuery.TraceLineSurface"/> and not the narrow
        /// trace beside it. The client chooses the decal and the impact sound
        /// from the surfaceprop the server sent — that is exactly what
        /// <c>CBaseEntity::ImpactTrace</c> writes into its <c>CEffectData</c> —
        /// and a server that named none would leave a wooden crate taking a
        /// concrete decal and cement flecks.
        /// </para>
        ///
        /// <para>
        /// THE START POINT IS THE PELLET'S OWN, not the eye's. The client
        /// re-traces start-&gt;origin in <c>fx_impact.cpp</c>'s <c>Impact()</c>
        /// to find the plane it decals, so a spread pellet that handed over the
        /// centre ray would have its decal placed on whatever the CENTRE of the
        /// pattern hit. It is stepped back from the impact rather than sent from
        /// the muzzle so the re-trace is short: the ray only has to reach the
        /// face that stopped this pellet.
        /// </para>
        /// </summary>
        public void FireBullets(in WeaponType type, int pellets, float spreadDegrees)
        {
            if (_arsenal._ammo.For(type.AmmoName) is not AmmoType ammo)
                return;

            Vector from = _arsenal._players.GetShootOrigin(_carry.Player);
            var aim = Angles.Vectors(_arsenal._entities.GetEyeAngles(_carry.Player));

            float spread = ShotSpread.SpreadOf(spreadDegrees);
            int seed = ShotSpread.FirstSeed(CommandRandomSeed);

            for (int i = 0; i < pellets; i++)
            {
                // Stock's spread, seeded per pellet from the command, so the
                // predicting client's pellet goes where this one does.
                Vector dir = ShotSpread.Apply(aim, spread, seed + i, _spread).Normalized;

                var trace = _arsenal._world.TraceLineSurface(from, from + dir * MaxTrace,
                                                             TraceMask.Shot, _carry.Player);

                if (!trace.Hit)
                    continue;

                // UTIL_ImpactTrace marks nothing on the sky, on either side.
                if ((trace.SurfaceFlags & SurfSky) == 0)
                    _arsenal._effects.ImpactEffect(trace.End - dir * ImpactBackoff, trace.End,
                                                   trace.SurfaceProps, ammo.DamageType,
                                                   trace.Entity);

                if (_arsenal._entities.IsValid(trace.Entity) && trace.Entity != EntityHandle.None)
                {
                    // FireBullets' CalculateBulletDamageForce: the ammo's force
                    // along the shot, acting where it landed, so the victim's
                    // OnTakeDamage can push its physics object (A30). The
                    // trace's hitgroup rides beside the call (stock hands the
                    // trace itself to TraceAttack): see DamageHitGroups.
                    using (_arsenal._hitGroups.Push(trace.HitGroup))
                        _arsenal._damage.ApplyAt(trace.Entity, _carry.Player,
                                                 ammo.Damage, ammo.DamageType,
                                                 DamageForceRules.BulletForce(dir, ammo.DamageForce),
                                                 trace.End);

                    if (_arsenal.IsStandalonePlayer(trace.Entity))
                        _arsenal._effects.BloodEffect(trace.End, dir, BloodColor.Red, ammo.Damage);
                }
            }
        }

        /// <summary>
        /// How far back along the shot the impact's start point is placed.
        ///
        /// <para>
        /// The client re-traces start-&gt;origin and extends it by 8 units
        /// (<c>fx_impact.cpp</c>'s <c>Impact</c>), so the ray has to BEGIN on
        /// the near side of the face the pellet stopped at or it starts inside
        /// the solid and finds nothing.
        /// </para>
        ///
        /// <para>
        /// THIRTY-TWO, which is what <c>FuncBreakableSurf.ImpactBackoff</c>
        /// steps back, and it is the same number rather than a second one so
        /// that two callers of one effect do not disagree about how far "just
        /// in front of" is. NOT shared as one constant: that one is a private
        /// member of a brush entity, and hoisting a rendering distance out to
        /// a shared home to save a literal would put it somewhere neither
        /// caller belongs.
        /// </para>
        /// </summary>
        private const float ImpactBackoff = 32f;

        /// <summary>
        /// A swing: <c>CBaseHL2MPBludgeonWeapon::Swing</c>'s first ray and, when
        /// it lands, <c>Hit</c> — the row's own damage, the hit sound for a row
        /// that plays one (<c>PlayFleshyHittySoundOnHit</c>), and the impact:
        /// the row's own client effect where it declares one, then
        /// <c>UTIL_ImpactTrace( DMG_CLUB )</c>, which every bludgeon's
        /// <c>ImpactEffect</c> ends in.
        /// </summary>
        public void MeleeSwing(in WeaponType type)
        {
            Vector from = _arsenal._players.GetShootOrigin(_carry.Player);
            var aim = Angles.Vectors(_arsenal._entities.GetEyeAngles(_carry.Player));

            // The surface rides with the trace for the impact below, as it does
            // for a bullet: the client picks the decal and the sound from it.
            var trace = _arsenal._world.TraceLineSurface(from, from + aim.Forward * type.MeleeRange,
                                                         TraceMask.Shot, _carry.Player);

            if (!trace.Hit)
                return;

            // CBaseHLBludgeonWeapon::Hit's CalculateMeleeDamageForce, at the
            // trace's end, so a swing shoves a physics object (A30).
            if (_arsenal._entities.IsValid(trace.Entity) && trace.Entity != EntityHandle.None)
                _arsenal._damage.ApplyAt(trace.Entity, _carry.Player,
                                         type.MeleeDamage, DamageType.Club,
                                         DamageForceRules.MeleeForce(aim.Forward, type.MeleeDamage),
                                         trace.End);

            // Hit plays it for whatever the ray struck, the world included: a
            // trace that stopped on a wall names worldspawn, never nothing.
            Sound(type.MeleeHitSound);

            if (type.MeleeImpactEffect.Length > 0 && _arsenal.Temps is { } temps)
            {
                // CWeaponStunStick::ImpactEffect: four units off the face.
                TeEffectData data = new()
                {
                    Origin = trace.End + trace.Normal * MeleeEffectStandoff,
                    Normal = trace.Normal,
                    Scale = 1f,
                };
                temps.DispatchEffect(TeFilters.Pas(data.Origin), 0f, type.MeleeImpactEffect, in data);
            }

            // Hit's ImpactEffect: UTIL_ImpactTrace( &traceHit, DMG_CLUB ), the
            // world included, as a round's. The predicting swinger draws its
            // own (ClientArsenal's arms); this copy is suppressed for it.
            if ((trace.SurfaceFlags & SurfSky) == 0)
                _arsenal._effects.ImpactEffect(trace.End - aim.Forward * ImpactBackoff, trace.End,
                                               trace.SurfaceProps, DamageType.Club, trace.Entity);
        }

        /// <summary>How far off the struck face a row's own melee effect sits: stock's 4.</summary>
        private const float MeleeEffectStandoff = 4f;

        /// <summary><c>SURF_SKY</c>: UTIL_ImpactTrace's refusal.</summary>
        internal const int SurfSky = 0x0004;

        /// <summary>
        /// Sends what the weapon launches instead of a bullet. It leaves from
        /// the eye rather than the entity origin, because anything else throws
        /// from the player's feet.
        /// </summary>
        public void Launch(in WeaponType type, string projectile, float speed, bool thrown)
        {
            var aim = Angles.Vectors(_arsenal._entities.GetEyeAngles(_carry.Player));

            _arsenal.LaunchFlying(_log, _carry, type, projectile,
                                  WeaponLogic.LaunchVelocity(aim, speed, thrown), thrown, fuse: 0f);
        }

        /// <inheritdoc />
        public void LaunchWith(in WeaponType type, string projectile, Vector velocity, float fuse)
        {
            // The caller's velocity, unmodified: a behaviour that worked its own
            // arc out is not asked to also know where the launcher's eye is.
            _arsenal.LaunchFlying(_log, _carry, type, projectile, velocity, type.Thrown, fuse);
        }

        /// <inheritdoc />
        public void SetFov(int fov, float rate) => _arsenal._players.SetFov(_carry.Player, _carry.Weapon, fov, rate);

        /// <inheritdoc />
        public void ThrowGrenade(in WeaponType type, GrenadeThrow kind, float timer)
            => _arsenal.ThrowGrenade(_log, _carry, type, kind, timer, _random);

        //---------------------------------------------------------------------
        // The RPG's hands (CWeaponRPG, shared/hl2mp/weapon_rpg.cpp).
        //---------------------------------------------------------------------

        /// <inheritdoc />
        public void LaunchMissile(in WeaponType type)
        {
            EntityHandle player = _carry.Player;
            Vector eye = _arsenal._players.GetShootOrigin(player);
            var aim = Angles.Vectors(_arsenal._entities.GetEyeAngles(player));

            // Weapon_ShootPosition() + vForward * 12.0f + vRight * 6.0f + vUp * -3.0f.
            Vector muzzle = eye + aim.Forward * 12f + aim.Right * 6f - aim.Up * 3f;
            Vector angles = Angles.FromVector(aim.Forward);

            EntityHandle missile = _arsenal._entities.Create(type.Projectile);
            if (missile == EntityHandle.None || !_arsenal._entities.IsValid(missile))
            {
                _log.Warning($"[SourceSharp/Arsenal] {type.Name} could not create '{type.Projectile}'\n");
                return;
            }

            // CBaseEntity::Create( "rpg_missile", origin, angles, owner ).
            _arsenal._entities.SetOrigin(missile, muzzle);
            _arsenal._entities.SetAngles(missile, angles);
            _arsenal._entities.SetOwner(missile, player);
            _arsenal._entities.Spawn(missile);

            // "If the shot is clear to the player, give the missile a grace
            // period": 128 units from the eye along the aim, MASK_SHOT.
            var clear = _arsenal._world.TraceLine(eye, eye + aim.Forward * 128f, TraceMask.Shot, player);

            if (_arsenal._directory.Get(missile) is Rocket rocket)
                rocket.FiredFromLauncher(player, angles, gracePeriod: clear.Fraction == 1f);

            _carry.Missile = missile;
        }

        /// <inheritdoc />
        public void UpdateLaserDot(bool exists, bool on)
        {
            var entities = _arsenal._entities;

            if (!exists)
            {
                if (_carry.LaserDot != EntityHandle.None && entities.IsValid(_carry.LaserDot))
                {
                    if (_arsenal._directory.Get(_carry.LaserDot) is LaserDot gone)
                        gone.TurnOff();
                    entities.Remove(_carry.LaserDot);
                }

                _carry.LaserDot = EntityHandle.None;
                return;
            }

            if (_arsenal._directory.Get(_carry.LaserDot) is not LaserDot dot)
            {
                // CLaserDot::Create( GetAbsOrigin(), GetOwner() ), then TurnOff.
                EntityHandle made = entities.Create(EntityNames.EnvLaserdot);
                if (made == EntityHandle.None || !entities.IsValid(made))
                    return;

                entities.SetOrigin(made, entities.GetOrigin(_carry.Player));
                entities.Spawn(made);
                _carry.LaserDot = made;

                if (_arsenal._directory.Get(made) is not LaserDot fresh)
                    return;

                dot = fresh;
                dot.OwnedBy(_carry.Player);
            }

            if (on)
                dot.TurnOn();
            else
                dot.TurnOff();

            // UpdateLaserPosition: from Weapon_ShootPosition along the eye, the
            // whole way, MASK_SHOT (less CONTENTS_WINDOW in stock; see
            // docs/divergences.md), past the owner.
            Vector from = _arsenal._players.GetShootOrigin(_carry.Player);
            Vector forward = Angles.Forward(entities.GetEyeAngles(_carry.Player));
            var tr = _arsenal._world.TraceLine(from, from + forward * MaxTrace, TraceMask.Shot, _carry.Player);

            dot.SetLaserPosition(tr.End, tr.Normal);

            // DidHitNonWorldEntity, and m_takedamage: something that can be hurt.
            EntityHandle hit = tr.Entity;
            bool hurtable = hit != EntityHandle.None && entities.GetIndex(hit) != 0 &&
                            (entities.IsPlayer(hit) || entities.GetHealth(hit) > 0);
            dot.TargetEntity = hurtable ? hit : EntityHandle.None;
        }

        /// <inheritdoc />
        public void ServerSound(string soundScript) => Sound(soundScript);

        /// <summary>
        /// The sequence for an animation, in the viewmodel's own numbering —
        /// through the ONE shared resolution (activities first, names second),
        /// because the client's replay resolves through the same method and
        /// the answer is predicted state. -1 (no such animation) is a real
        /// answer the machine survives.
        /// </summary>
        public int LookupSequence(WeaponAnim anim)
        {
            if (!_arsenal._entities.IsValid(_carry.ViewModel))
                return -1;

            return WeaponAnimNames.Resolve(_arsenal._render, _carry.ViewModel, anim);
        }

        public float AnimDuration(WeaponAnim anim)
        {
            int sequence = LookupSequence(anim);

            if (sequence < 0)
                return 0f;

            return _arsenal._render.SequenceDuration(_carry.ViewModel, sequence);
        }

        //---------------------------------------------------------------------
        // The SLAM's hands (CWeapon_SLAM, weapon_slam.cpp).
        //---------------------------------------------------------------------

        public int LookupActivity(string activity)
            => _arsenal._entities.IsValid(_carry.ViewModel)
                ? _arsenal._render.LookupSequenceByActivity(_carry.ViewModel, activity)
                : -1;

        public float SequenceDuration(int sequence)
            => sequence >= 0 && _arsenal._entities.IsValid(_carry.ViewModel)
                ? _arsenal._render.SequenceDuration(_carry.ViewModel, sequence)
                : 0f;

        /// <summary>
        /// The eye along the aim, through <c>MASK_SOLID</c>, ignoring the
        /// carrier. Stock's traces read <c>GetLocalAngles()</c>, which during
        /// the weapon's frame is the command's view angles — the aim.
        /// </summary>
        public WallProbe ProbeWall(float reach)
        {
            var trace = Ahead(reach);

            if (!trace.Hit)
                return WallProbe.Nothing;

            return trace.Entity != EntityHandle.None && _arsenal._entities.IsPlayer(trace.Entity)
                ? WallProbe.Character
                : WallProbe.Surface;
        }

        private TraceResult Ahead(float reach)
        {
            Vector eye = _arsenal._players.GetShootOrigin(_carry.Player);
            Vector forward = Angles.Forward(_arsenal._entities.GetEyeAngles(_carry.Player));

            return _arsenal._world.TraceLine(eye, eye + forward * reach, TraceMask.Solid, _carry.Player);
        }

        /// <summary>
        /// <c>SatchelThrow</c>: from the carrier's middle, eighteen units out
        /// and twenty-four up, at their own velocity plus five hundred along
        /// the aim — or, when they have turned to a wall during the throw,
        /// barely moving from five units out, so it is not thrown into it.
        /// </summary>
        public void ThrowCharge(in WeaponType type)
        {
            Vector centre = _arsenal._entities.GetWorldSpaceCenter(_carry.Player);
            Vector facing = Angles.Forward(_arsenal._entities.GetEyeAngles(_carry.Player));

            Vector from = centre + facing * SlamThrowOut + new Vector(0f, 0f, SlamThrowLift);
            Vector velocity = _arsenal._entities.GetVelocity(_carry.Player) + facing * SlamThrowSpeed;

            if (ProbeWall(SlamMachine.AttachReach) == WallProbe.Surface)
            {
                velocity = facing;
                from = centre + facing * SlamThrowOutAtWall;
            }

            EntityHandle satchel = _arsenal._entities.Create(EntityNames.NpcSatchel);

            if (satchel == EntityHandle.None)
            {
                _log.Warning($"[SourceSharp/Arsenal] {type.Name} could not create {EntityNames.NpcSatchel}\n");
                return;
            }

            _arsenal._entities.SetOrigin(satchel, from);
            _arsenal._entities.Spawn(satchel);

            if (_arsenal._directory.Get(satchel) is Satchel live)
                live.ThrownBy(_carry.Player, attached: false);

            _arsenal._entities.ApplyVelocityImpulse(satchel, velocity, false);
        }

        /// <summary>
        /// <c>SatchelAttach</c>: a satchel on the wall ahead, three units off
        /// it and six down, stuck there.
        /// </summary>
        public void AttachCharge(in WeaponType type)
        {
            var trace = Ahead(SlamMachine.PlaceReach);

            if (!trace.Hit)
                return;

            Vector angles = Angles.FromVector(trace.Normal);
            angles = new Vector(angles.X, angles.Y - 90f, angles.Z - 90f);

            Vector at = trace.End - new Vector(0f, 0f, 6f) + trace.Normal * SlamWallStandoff;

            EntityHandle satchel = _arsenal._entities.Create(EntityNames.NpcSatchel);

            if (satchel == EntityHandle.None)
                return;

            _arsenal._entities.SetOrigin(satchel, at);
            _arsenal._entities.SetAngles(satchel, angles);
            _arsenal._entities.Spawn(satchel);

            if (_arsenal._directory.Get(satchel) is Satchel live)
                live.ThrownBy(_carry.Player, attached: true);
        }

        /// <summary>
        /// <c>TripmineAttach</c>: a tripmine three units off the wall ahead,
        /// turned so its beam runs out along the wall's normal.
        /// </summary>
        public void AttachTripmine(in WeaponType type)
        {
            var trace = Ahead(SlamMachine.PlaceReach);

            if (!trace.Hit)
                return;

            Vector angles = Angles.FromVector(trace.Normal);
            angles = new Vector(angles.X + 90f, angles.Y, angles.Z);

            EntityHandle mine = _arsenal._entities.Create(EntityNames.NpcTripmine);

            if (mine == EntityHandle.None)
            {
                _log.Warning($"[SourceSharp/Arsenal] {type.Name} could not create {EntityNames.NpcTripmine}\n");
                return;
            }

            _arsenal._entities.SetOrigin(mine, trace.End + trace.Normal * SlamWallStandoff);
            _arsenal._entities.SetAngles(mine, angles);
            _arsenal._entities.Spawn(mine);

            if (_arsenal._directory.Get(mine) is Tripmine planted)
                planted.PlantedBy(_carry.Player);
        }

        public void DetonateCharges()
        {
            foreach (var satchel in LiveCharges())
                satchel.Detonate();
        }

        public bool ChargesOut() => LiveCharges().Any();

        private IEnumerable<Satchel> LiveCharges()
            => _arsenal._directory.OfClass(EntityNames.NpcSatchel)
                      .OfType<Satchel>()
                      .Where(s => s.IsLive && s.Thrower == _carry.Player)
                      .ToArray();

        private const float SlamThrowOut = 18f;
        private const float SlamThrowLift = 24f;
        private const float SlamThrowSpeed = 500f;
        private const float SlamThrowOutAtWall = 5f;
        private const float SlamWallStandoff = 3f;

        /// <summary>
        /// The stunstick's draw sparks: <c>g_pEffects-&gt;Sparks</c> (magnitude
        /// one, trail one, no direction, no sound) at the head of the stick.
        ///
        /// <para>
        /// Stock sparks at the WEAPON's attachment 1, which on a stock server is
        /// the bonemerged w_ model's tip in the carrier's hand. This build's
        /// carried weapon is parked at the player's origin and not bonemerged on
        /// the server (<see cref="Frame"/>), so its attachment would answer a
        /// point at the feet; the sparks go where the hand is instead, a short
        /// reach out from the eye.
        /// </para>
        /// </summary>
        public void DeploySparks()
        {
            if (_arsenal.Temps is not { } temps)
                return;

            Vector eye = _arsenal._players.GetShootOrigin(_carry.Player);
            var aim = Angles.Vectors(_arsenal._entities.GetEyeAngles(_carry.Player));
            Vector at = eye + aim.Forward * DeploySparkReach;

            temps.Sparks(TeFilters.Pvs(at), 0f, at, 1, 1, null);
        }

        /// <summary>How far out from the eye the draw's sparks are made.</summary>
        private const float DeploySparkReach = 24f;

        /// <summary>The draw, from Deploy — outside the machine's own frame.</summary>
        public void SendDeployAnim(ViewModelNetState vm)
        {
            int sequence = LookupSequence(WeaponAnim.Draw);

            if (sequence < 0)
                return;

            vm.Sequence = sequence;
            vm.AnimationParity = SourceSharp.Shared.AnimationParity.Next(vm.AnimationParity);
            vm.PlaybackRate = 1f;
        }


        /// <summary>Source's MAX_TRACE_LENGTH: the diagonal of the world cube.</summary>
        private const float MaxTrace = 56756f;
    }
}
