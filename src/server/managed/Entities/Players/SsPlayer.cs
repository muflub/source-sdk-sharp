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
/// The standalone player's managed half (Phase 9a of plan_baseentity.md).
///
/// <para>
/// The one class in the mod that declares <see cref="ScriptedEntityKind.Player"/>,
/// which is what makes the native factory build the player shell — the
/// CStandaloneEntity subclass that carries the engine-facing state blocks and
/// answers the movement loop — instead of the plain scripted one. Instances
/// are never made by a map or by this side: the ENGINE causes one per
/// connecting client, and this class finds out through the same OnSpawn every
/// entity on the hierarchy gets.
/// </para>
///
/// <para>
/// What lives here is the player as a GAME object: what they look like, how
/// much they can take, what dying means. Movement stays in the shared native
/// loop (9b's decision), reading the parameters <see cref="Movement"/>
/// registered.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsPlayer, ScriptedEntityKind.Player)]
public sealed class SsPlayer : ScriptedEntity
{
    /// <summary>
    /// The model every player wears. The same citizen the CBasePlayer shell
    /// settled on after Phase 10's bonemerge work found the old path had been
    /// error.mdl all along (shell_player.cpp) — group03 is where the mounted
    /// content actually keeps the citizens.
    /// </summary>
    public const string PlayerModel = "models/humans/group03/male_01.mdl";

    /// <summary>What a fresh spawn stands up with.</summary>
    public const int SpawnHealth = 100;

    /// <summary>
    /// Being drawn is not <c>Entities</c>' area, so it arrives by constructor —
    /// the base class's rule for every area whose subject is not always this
    /// entity. See <see cref="OnKilled"/> for what this class does with it.
    /// <para>
    /// <paramref name="removals"/> turns this entity's removal into a client's
    /// disconnect (plan_host_sdk §2.3); tests that build a player by hand leave
    /// it out.
    /// </para>
    /// </summary>
    // This composition's player command queues (statics to zero), whose entry
    // for this player goes when it does. Null for a player built without one.
    private readonly PlayerCommandQueue? _commands;

    public SsPlayer(IEngineEntityRender render, IEnginePlayers players, PhysicsDamageQueue impacts,
                    FluidTouches fluid, IPlayerRemovals? removals = null,
                    PlayerCommandQueue? commands = null, IUserMessageSender? messages = null,
                    IEngineHost? host = null, DamageInflictors? inflictors = null,
                    IPlayerShadows? shadows = null, IGameRules? rules = null, ConVarRegistry? convars = null,
                    ModelDescriptors? models = null, IGameClock? clock = null,
                    SourceSharp.Shared.Anim.AnimStateLayers? animLayers = null)
    {
        _shadows = shadows;
        _host = host;
        _inflictors = inflictors;
        _rules = rules;
        _printDamage = convars?.Get<PlayerDebugPrintDamageConVar>();
        _messages = messages;
        _commands = commands;
        _fluid    = fluid;
        _render   = render;
        _players  = players;
        _impacts  = impacts;
        _removals = removals;
        _models   = models;
        _clock    = clock;
        _animLayers = animLayers;
    }

    // The shared animation state (phase 0.10), container-supplied and built
    // lazily on the first PreThink — the same machine the client runs, over
    // this side's arms. OPTIONAL by decision: a player built by hand without
    // them has no machine and drives nothing, which is what the hand-built
    // server tests need (the fake would refuse the machine's reads) and what
    // the composition's absence should mean anyway. Where they are the
    // engine's own, the machine runs exactly as stock's
    // m_PlayerAnimState->Update() in CBasePlayer::PreThink.
    private readonly ModelDescriptors? _models;
    private readonly IGameClock? _clock;
    // The side's layer composition (the mods' IAnimStateLayer services and
    // the core's HL2 layer); null for a hand-built player, whose chain is
    // the HL2 layer alone.
    private readonly SourceSharp.Shared.Anim.AnimStateLayers? _animLayers;
    private SourceSharp.Shared.Anim.EngineAnimTarget? _animTarget;
    private SourceSharp.Shared.Anim.MultiplayerAnimState? _animState;
    private float _lastAnimDrive = float.NaN;

    // The sequence last handed to the server's skeleton, so the per-drive
    // hand-over reaches the engine only on a change (see EnsureAnimState).
    private int _skeletonSequence = -1;

    private readonly IEngineEntityRender _render;
    private readonly IEnginePlayers _players;

    // The physics shadows (plan_physics_bones_native_zero 2.5). Null for a
    // player built without them, which then has no body in the solver.
    private readonly IPlayerShadows? _shadows;
    private readonly PhysicsDamageQueue _impacts;
    private readonly FluidTouches _fluid;
    private readonly IPlayerRemovals? _removals;
    private readonly IUserMessageSender? _messages;
    private readonly IEngineHost? _host;
    private readonly DamageInflictors? _inflictors;
    // The mode's damage decision (spawn protection, friendly fire, the mods'
    // IDamageRule chain): stock's g_pGameRules->FPlayerCanTakeDamage. Null
    // for a player built by hand, which takes every hit, as CMultiplayRules'
    // own answer does.
    private readonly IGameRules? _rules;
    // player_debug_print_damage: null for a player built without the registry.
    private readonly PlayerDebugPrintDamageConVar? _printDamage;

    /// <summary>The suit's cap: stock's 100 (MAX_NORMAL_BATTERY).</summary>
    public const int MaxArmour = 100;

    /// <summary>The suit's armour, networked to the client's HUD (<see cref="PlayerNetState.Armour"/>).</summary>
    public int Armour
    {
        get => State.Armour;
        set => State.Armour = Math.Clamp(value, 0, MaxArmour);
    }

    //-- THE PLAYER AS A BODY IN THE PHYSICS WORLD (plan_entity.md step 3f) ----
    //
    // These four numbers are why the shadow is asked for from HERE rather than
    // built in C++ the moment an edict is bound: every one of them is a game
    // decision about what a player is and what a player may do to the world.

    /// <summary>
    /// How heavy the player's physics shadow is. Stock's 85 kilograms
    /// (<c>CBasePlayer::SetupVPhysicsShadow</c>).
    /// </summary>
    public const float ShadowMass = 85f;

    /// <summary>
    /// The heaviest thing a player may shove by walking into it, in kilograms.
    /// Stock's 350 — heavy enough for crates and barrels, light enough that a
    /// car does not slide when leant on.
    /// </summary>
    public const float PushMassLimit = 350f;

    /// <summary>
    /// How fast a player may shove something, in units per second. Stock's 50,
    /// which is well under a walk: a shoved crate slides rather than being
    /// kicked across the room.
    /// </summary>
    public const float PushSpeedLimit = 50f;

    /// <summary>
    /// The two hulls the shadow is built from — standing and crouched.
    ///
    /// <para>
    /// <b>THE SAME NUMBERS THE MOVEMENT LOOP TRACES WITH</b>, taken from
    /// <see cref="MoveConstantDefaults.Stock"/> rather than restated, because a shadow
    /// of a different size from the body is a player who is stopped by a crate
    /// their shadow has not reached (or shoves one they are not touching).
    /// <c>TheShadowHullIsTheMovementHull</c> pins the agreement.
    /// </para>
    ///
    /// <para>
    /// COUPLED TO PlayerShadow.Step's climb clamp, which traces with the same
    /// <see cref="MoveConstantDefaults.Stock"/> hulls where stock's
    /// <c>UTIL_TraceEntity( this, ... )</c> uses the player's scaled ones
    /// (player.cpp:4834-4835). Consistent while both read Stock; a composition
    /// that registers other constants or a scaled model must change both.
    /// </para>
    /// </summary>
    public static (Vector StandMin, Vector StandMax, Vector CrouchMin, Vector CrouchMax) ShadowHulls
    {
        get
        {
            var c = MoveConstantDefaults.Stock;
            return (c.HullMin, c.HullMax, c.DuckHullMin, c.DuckHullMax);
        }
    }

    /// <summary>
    /// The player's replicated state. See <see cref="PlayerNetState"/>.
    ///
    /// <para>
    /// <b>HEALTH IS NOT WRITTEN HERE AND MUST NOT BE.</b> On the server this
    /// object is a view straight into the entity's native data blob, and
    /// <c>CStandalonePlayer::SyncToBlob</c> puts <c>GetHealth()</c> into that
    /// blob's <c>Health</c> word — at the end of <c>Spawn</c>, after every hit
    /// that landed, after a view punch, and after every user command
    /// (shell_standalone_player.cpp; standalone_player_sync.cpp:200). Anything
    /// this side writes there is overwritten before a snapshot can carry it.
    /// </para>
    ///
    /// <para>
    /// MEASURED RATHER THAN REASONED, because this class carried two such
    /// writes and they read as live code. <c>OnSpawn</c> was made to write a
    /// sentinel of 4242 and <c>OnDamaged</c> to log what it read back:
    /// <c>[SourceSharp/Probe] native Health=100, State.Health=100</c>, and 83/83
    /// on the next hit. The sentinel never survived a single frame.
    /// </para>
    ///
    /// <para>
    /// That is also the RIGHT arrangement rather than an accident to be
    /// reversed. Health changes from eight places that are not this class —
    /// the pickups, the healthkit, the vial, the charger, the sodacan,
    /// trigger_hurt, game_player_hurt, the impulse cheats — and every one of
    /// them writes the native health and nothing else. A wire copy maintained
    /// by managed code would miss all of them; one maintained by the native
    /// sync misses none. <c>m_iHealth</c> is the single health, exactly as
    /// stock's <c>CBasePlayer</c> has a single <c>m_iHealth</c>.
    /// </para>
    /// </summary>
    [NetState] public PlayerNetState State = new();

    /// <summary>
    /// The entity index this player stands on, -1 for nothing: the ground word
    /// <see cref="State"/> carries, decoded (ground_handle_wire.h).
    /// </summary>
    public int GroundIndex
        => State.GroundEntity == GroundWireNone ? -1 : State.GroundEntity & GroundWireIndexMask;

    /// <summary>ground_handle_wire.h's GROUNDWIRE_NONE.</summary>
    public const int GroundWireNone = (1 << 21) - 1;

    /// <summary>ground_handle_wire.h's GROUNDWIRE_INDEX_MASK.</summary>
    public const int GroundWireIndexMask = (1 << 11) - 1;

    /// <summary>
    /// <c>m_afPhysicsFlags</c>, the two bits of it this side has: stock's
    /// <c>PFLAG_GAMEPHYSICS_ROTPUSH</c> and <c>PFLAG_VPHYSICS_MOTIONCONTROLLER</c>.
    /// Not networked; the server's own state. Carried across a reload as stock
    /// saves it (<c>DEFINE_FIELD( m_afPhysicsFlags, FIELD_INTEGER )</c>,
    /// player.cpp:301): the trigger_vphysics_motion that set the controller bit
    /// keeps the player's shadow attached through the reload, so an emptied
    /// word stopped the shadow update counting them as touching.
    /// </summary>
    [SaveField] public int PhysicsFlags;

    /// <summary>
    /// <c>PFLAG_GAMEPHYSICS_ROTPUSH</c> (player.h): a rotating pusher moved this
    /// player, and the next shadow update checks whether that left them stuck.
    /// </summary>
    public const int PhysicsFlagGamePhysicsRotPush = 1 << 5;

    /// <summary>
    /// <c>PFLAG_VPHYSICS_MOTIONCONTROLLER</c> (player.h): the player is in a
    /// trigger_vphysics_motion's controller. The shadow update counts it as
    /// touching and eases the impulse (player.cpp:8326, :8425).
    /// </summary>
    public const int PhysicsFlagVPhysicsMotionController = 1 << 4;

    /// <summary>
    /// WHERE THIS PLAYER'S EYES ARE — the one position the engine asks for twice
    /// under two names (step 3c, plan_entity.md §3.10).
    ///
    /// <para>
    /// <c>ClientSetupVisibility</c> adds it to the PVS and
    /// <c>ClientEarPosition</c> puts the sound listener at it. In stock those
    /// are <c>CBasePlayer::EyePosition()</c> and <c>EarPosition()</c>, and
    /// <c>EarPosition</c> is literally <c>return EyePosition();</c> — one value,
    /// two callers, and this is that value.
    /// </para>
    ///
    /// <para>
    /// <b>IT IS ARITHMETIC OVER THIS CLASS'S OWN STATE AND CROSSES NOTHING.</b>
    /// Until this step the managed answer was a round trip:
    /// <c>ServerEarPositionForClient</c> went out to C#, and C# went straight
    /// back in through <c>ClientsStandalonePlayerEarPosition</c> to read
    /// <c>CStandalonePlayer::EyePosition</c> — a crossing each way to be told
    /// what the native side already had. Both halves are here now:
    /// <see cref="PlayerNetState.Origin"/> and
    /// <see cref="PlayerNetState.ViewOffset"/> are the two words stock adds, the
    /// second of which is what ducking moves.
    /// </para>
    ///
    /// <para>
    /// The native side caches this in the engine block for the frame it asked
    /// on, so a busy frame — <c>ClientEarPosition</c> is called once per
    /// PVS-filtered message, not once a frame — asks for it once.
    /// </para>
    /// </summary>
    public Vector ViewOrigin => State.Origin + State.ViewOffset;

    /// <summary>
    /// The player is going — a disconnect, or the level tearing down.
    ///
    /// <para>
    /// The command queue is handed back. Step 3d's <c>PlayerCommandQueue</c> is
    /// told about arrivals and never about departures, so it was bounded by a
    /// staleness sweep every 256 batches rather than by being told
    /// (requests/3d.md, "FOR STEP 3f, WHICH OWNS ClientDisconnect"). A queue
    /// left behind belongs to whoever next occupies the slot.
    /// </para>
    ///
    /// <para>
    /// <b>THE PHYSICS SHADOW IS NOT TAKEN BACK HERE, AND THAT IS DELIBERATE.</b>
    /// It has exactly one owner — <c>CStandalonePlayer</c>'s destructor — and a
    /// managed second owner for the same teardown was tried and reverted: a
    /// live-gate run with a <c>DestroyVCollision</c> on this line came back with
    /// every physics prop in the SECOND map falling five thousand units through
    /// the floor, which is the world's physics object gone. Whatever the exact
    /// path, the shape is the one this project keeps meeting — two owners of one
    /// piece of teardown, running at two different points of a level's life.
    /// The shadow's lifetime is the player's, the player's lifetime is the
    /// engine's edict, and both of those are native.
    /// </para>
    /// </summary>
    public override void OnRemoved()
    {
        _commands?.Forget(Handle);
        _removals?.PlayerRemoved(Entities.GetIndex(Handle));
    }

    public override void OnSpawn()
    {
        SetModel(PlayerModel);
        Health = SpawnHealth;

        // NOT State.Health: the native sync owns that word — see the field.
        State.Alive  = true;
        State.Ducked = false;
        State.Armour = 0;

        // In play: no observer mode, nobody watched (the invalid handle, never 0).
        State.ObserverMode = 0;
        State.ObserverTarget = EntityHandle.None.Value;

        // Stock's Spawn clears the damage report and forgets what the HUD was
        // told, so the next client-data update sends one empty Damage message,
        // ResetHUD, the battery and the train state.
        DamageReport.Reset();
        _initHud = true;
        _clientBattery = -1;
        _train = TrainNew;

        // Precache's geiger reset, and Spawn's two seconds before the first send.
        _geigerRange = GeigerFar;
        _geigerRangePrev = (int)GeigerFar;
        _geigerDelay = GameTime + 2f;

        // DRAWN AGAIN, because dying stopped drawing them -- see OnKilled.
        // Spawn IS the respawn on this hierarchy (SourceSharp_PlayerRespawn
        // calls straight into it), so this is the one place that has to undo
        // it, and a player who came back invisible would be a far worse bug
        // than the one it fixes.
        _render.SetVisible(Handle, true);

        // A BODY IN THE PHYSICS WORLD, so walking into a crate moves the crate.
        //
        // Here rather than at ClientPutInServer because a respawn has to ask
        // again: the shadow is teleported to wherever this spawn put the player,
        // and a shadow left at the place they died would drag itself across the
        // map shoving everything on the way. The call is idempotent and says so.
        //
        // The live gate's `push` step was a recorded skip until this line
        // existed -- measured per tick, the player walked into a prop, was
        // stopped dead by it, and the prop moved 0.000 units.
        var (standMin, standMax, crouchMin, crouchMax) = ShadowHulls;
        _shadows?.Init(Handle, standMin, standMax, crouchMin, crouchMax,
                                ShadowMass, PushMassLimit, PushSpeedLimit);

        Log.Msg($"[SourceSharp/C#] ss_player spawned at {Origin}, {Health} health\n");
    }

    /// <summary>
    /// god: refuses ALL damage. Stock's FL_GODMODE, kept as a damage rule here
    /// because that is what it is. Toggled — out loud, behind sv_cheats — by
    /// <see cref="DamageCheats"/>.
    /// </summary>
    public bool GodMode { get; set; }

    /// <summary>
    /// buddha: takes damage but never dies — health floors at 1. Stock's
    /// OVERLAY_BUDDHA_MODE, kept as a damage rule for the same reason.
    /// </summary>
    public bool BuddhaMode { get; set; }

    /// <summary>
    /// The damage rule. Stock's — everything lands — unless a cheat says
    /// otherwise: god refuses it all, buddha stops one point short of death.
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
        => TakeHit(attacker, damage, damageType, HitGroup.Generic);

    /// <summary>
    /// The located form, which is the one the damage op reaches: the same
    /// rule, with the hit's <paramref name="hitGroup"/> handed to the mode's
    /// damage decision (<see cref="DamageContext.HitGroup"/>).
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => TakeHit(attacker, damage, damageType, hitGroup);

    private int TakeHit(EntityHandle attacker, float damage, DamageType damageType, HitGroup hitGroup)
    {
        // THE ANSWER IS THE WHOLE OF IT. The shell subtracts what this returns
        // from m_iHealth and calls OnKilled if that reaches zero
        // (CStandaloneEntity::TakeDamage), so a class that also wrote health
        // would be applying the same hit twice.
        //
        // This used to write State.Health as well, clamped at zero. It was dead
        // — the native sync overwrites that word, measured; see the field — and
        // it stated a floor the game does not have: a player killed by 200 goes
        // to -117 and dies there, which is stock's arithmetic.
        if (GodMode)
            return 0;

        // Stock's "early out if there's no damage", before anything is recorded.
        if (damage == 0f)
            return 0;

        // CBasePlayer::OnTakeDamage asks the rules before the armour takes
        // anything (player.cpp:1147, FPlayerCanTakeDamage): "no" refuses the
        // whole hit. Only a refusal changes the answer; Yes and UseStockRule
        // both land it, the multiplayer rules' own true.
        if (_rules?.CanTakeDamage(Handle, attacker, damage, (int)damageType, GameTime, hitGroup) == Decision.No)
            return 0;

        // Stock's player_debug_print_damage line, at stock's place (after the
        // rules, before the armour), and the hitgroup the rules were told.
        if (_printDamage?.Value == true)
            Log.Msg($"{GameTime:F6}: Player {Handle} at [{Origin.X:F2} {Origin.Y:F2} {Origin.Z:F2}] "
                  + $"took {damage:F6} damage from {attacker}, type {damageType}, hitgroup={(int)hitGroup}\n");

        // The suit first (stock's CBasePlayer::OnTakeDamage armour block), then
        // the health rule on what got through.
        var (toHealth, armourLeft, absorbed) = Absorb(Armour, damage, damageType);
        Armour = armourLeft;
        if (absorbed > 0f)
            DamageReport.Absorbed(absorbed);

        int taken = DamageTaken(Health, (int)toHealth, GodMode, BuddhaMode);

        // Into the frame's report, sent by UpdateClientData: not a message per hit.
        DamageReport.Took(taken, damageType, DamageOrigin(attacker), GameTime);
        return taken;
    }

    /// <summary>What the suit takes of a hit: stock's armour arithmetic (<see cref="Armour"/>).</summary>
    public static (float ToHealth, int ArmourLeft, float Absorbed) Absorb(int armour, float damage, DamageType damageType)
    {
        var r = Server.Armour.Apply(damage, armour, damageType, multiplayer: GameRulesPolicy.Multiplayer);
        return (r.Damage, r.ArmourLeft, r.Absorbed);
    }

    /// <summary>
    /// What the HUD is told about this player's hits, summed over a frame.
    /// See <see cref="PlayerDamageReport"/>.
    /// </summary>
    public PlayerDamageReport DamageReport { get; } = new();

    /// <summary>The watching this player does when not in play. See <see cref="Observers"/>.</summary>
    public ObserverRecord Observer { get; } = new();

    /// <summary>
    /// Where a hit came from: stock's <c>m_DmgOrigin</c>, the inflictor's
    /// origin. The inflictor is the open <see cref="DamageInflictors"/> scope
    /// (a blast's grenade, rocket or env_explosion) when there is one; else, on
    /// this build's damage path, the attacker (SourceSharp_EntityTakeDamage
    /// builds <c>CTakeDamageInfo( pAttacker, pAttacker, ... )</c>). The world,
    /// or no attacker, is the world's origin: zero, which the HUD reads as "no
    /// direction".
    /// </summary>
    private Vector DamageOrigin(EntityHandle attacker)
    {
        if (_inflictors is not null && _inflictors.TryCurrent(out _, out var from))
            return from;

        return attacker != EntityHandle.None && attacker.EntryIndex != 0 && Entities.IsValid(attacker)
            ? Entities.GetOrigin(attacker)
            : default;
    }

    // Stock's CBasePlayer HUD bookkeeping: m_fInitHUD, m_iClientBattery and
    // m_iTrain. Spawn sets all three so the first update resends the HUD.
    private bool _initHud = true;
    private int _clientBattery = -1;
    private int _train = TrainNew;

    /// <summary>Stock's <c>TRAIN_NEW</c> (baseplayer_shared.h): the train state has not been sent.</summary>
    public const int TrainNew = 0xc0;

    /// <summary>
    /// The client-data update, once a frame (<see cref="PlayerClientData"/>):
    /// stock's <c>CHL2_Player::UpdateClientData</c> (the frame's Damage) and then
    /// its base, <c>CBasePlayer::UpdateClientData</c>: ResetHUD after a spawn,
    /// Battery when the armour the client was told is stale, Train when the
    /// train state is new. Each goes reliably to this player alone, in that
    /// order. GameTitle is not sent: see docs/player-usermessages.tsv.
    /// </summary>
    public void UpdateClientData(float now)
    {
        bool damage = DamageReport.TryFlush(now, out var report);
        if (_messages is null)
            return;

        int slot = Entities.GetIndex(Handle);

        if (damage)
            _messages.Send(report, slot, reliable: true);

        if (_initHud)
        {
            _initHud = false;
            _messages.Send(new ResetHud { Unused = 0 }, slot, reliable: true);
        }

        if (Armour != _clientBattery)
        {
            _clientBattery = Armour;
            _messages.Send(new Battery { Value = (short)Armour }, slot, reliable: true);
        }

        // CheckTrainUpdate. Only the spawn's TRAIN_NEW reaches it: driving a
        // train is unreachable on this hierarchy (FuncTrackTrain.OnControls).
        if ((_train & TrainNew) != 0)
        {
            _messages.Send(new Train { Position = (byte)(_train & 0xF) }, slot, reliable: true);
            _train &= ~TrainNew;
        }
    }

    //-- THE GEIGER COUNTER (stock's UpdateGeigerCounter, from CheckSuitUpdate) --

    /// <summary>GEIGERDELAY: the counter is sent at most this often.</summary>
    public const float GeigerDelay = 0.25f;

    /// <summary>The range that means "nothing near" (m_flgeigerRange's reset).</summary>
    public const float GeigerFar = 1000f;

    private float _geigerRange = GeigerFar;
    private int _geigerRangePrev = (int)GeigerFar;

    /// <summary>
    /// When the counter may next be sent. Seeded ONCE in <see cref="OnSpawn"/>
    /// two seconds out -- a hot reload does not run it again, so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt player held 0 and the
    /// throttle opened immediately (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _geigerDelay;

    /// <summary>The nearest radiation source's range, as the counter will read it.</summary>
    public float GeigerRange => _geigerRange;

    /// <summary>A radiation trigger within <paramref name="range"/>: the nearest one wins (NotifyNearbyRadiationSource).</summary>
    public void NotifyNearbyRadiationSource(float range)
    {
        if (_geigerRange >= range)
            _geigerRange = range;
    }

    /// <summary>
    /// Stock's UpdateGeigerCounter: every <see cref="GeigerDelay"/>, the range
    /// over four as a byte, sent reliably when it changed; then a one-in-four
    /// chance (the engine's random) of forgetting the source.
    /// </summary>
    public void UpdateGeigerCounter(float now)
    {
        if (now < _geigerDelay)
            return;

        _geigerDelay = now + GeigerDelay;

        int range = Math.Clamp((int)MathF.Floor(_geigerRange / 4f), 0, 255);
        if (range != _geigerRangePrev)
        {
            _geigerRangePrev = range;
            _messages?.Send(new Geiger { Range = (byte)range }, Entities.GetIndex(Handle), reliable: true);
        }

        if (_host is not null && _host.RandomInt(0, 3) == 0)
            _geigerRange = GeigerFar;
    }

    /// <summary>
    /// Stock's CheckTimeBasedDamage: the report counts the time-based kinds
    /// down, and what poison gives back is TakeHealth'd (up to a hundred).
    /// </summary>
    public void CheckTimeBasedDamage(float now)
    {
        int heal = DamageReport.CheckTimeBased(now);
        if (heal > 0 && State.Alive && Health > 0 && Health < SpawnHealth)
            Health = Math.Min(SpawnHealth, Health + heal);
    }

    /// <summary>
    /// The player's per-frame think, in stock's PreThink order: the client data
    /// (<see cref="UpdateClientData"/>), the time-based damage, then the suit's
    /// geiger counter — and, where the composition handed this player its
    /// clock and model table, the shared animation state, last (stock drives
    /// <c>m_pPlayerAnimState-&gt;Update()</c> at the tail of
    /// <c>CBasePlayer::PreThink</c>, after the state the pose reads is final).
    /// </summary>
    public void PreThink(float now)
    {
        UpdateClientData(now);
        CheckTimeBasedDamage(now);
        UpdateGeigerCounter(now);
        UpdateAnimState(now);
    }

    /// <summary>
    /// One drive of the shared machine (phase 0.10): the server poses nothing
    /// — it has no pose-parameter, layer or ground-speed arms and the pose
    /// writes reach no shell (the divergence rows name each) — but it runs the
    /// same decisions the client's copy runs, so the sequence the machine
    /// chooses and the activity it remembers are the server's answer for
    /// hitboxes, ragdolls and the day a pose driver exists. The eyes are the
    /// replicated eye angles (stock's server-side <c>EyeAngles()</c>) and the
    /// frame time is measured between drives, so the yaw converge and the
    /// autokill timers advance whatever the driver's rhythm.
    /// </summary>
    public void UpdateAnimState(float now)
    {
        if (_clock is null || _models is null)
            return;

        SourceSharp.Shared.Anim.MultiplayerAnimState? state = EnsureAnimState();
        if (state is null)
            return;

        float frameTime = float.IsNaN(_lastAnimDrive) ? 0f : now - _lastAnimDrive;
        _lastAnimDrive = now;

        // pitch, yaw, roll: the machine takes the yaw and pitch of the eyes.
        Vector eye = Entities.GetEyeAngles(Handle);
        state.Update(eye.Y, eye.X, now, frameTime);
    }

    /// <summary>The server's machine, built once on the first drive.</summary>
    private SourceSharp.Shared.Anim.MultiplayerAnimState? EnsureAnimState()
    {
        if (_animState is not null)
            return _animState;

        if (_animTarget is null)
        {
            _animTarget = new SourceSharp.Shared.Anim.EngineAnimTarget(
                Entities, _render, _models,
                () => State,
                () => Entities.GetEyeAngles(Handle),
                arms: new SourceSharp.Shared.Anim.EngineAnimTarget.Arms(
                    // The server's side has pose-parameter, dormancy and
                    // layer arms nowhere (the divergence rows), so those
                    // stay null — the target answers no dials and keeps the
                    // gesture mirror in memory. What the server DOES have is
                    // its own sequence write (EntitySetSequence, the arm
                    // ScriptedEntity's PlaySequence helper uses; the rate and
                    // restart slot is CLIENT-backed through its recording
                    // branch): what the machine chooses is the pose the
                    // server builds hitboxes and attachment frames from.
                    // EDGE-TRIGGERED here, because the machine hands its
                    // sequence over on every drive and EntitySetSequence
                    // always restarts: a write per tick would pin the
                    // server's cycle at zero. A new sequence, or the
                    // machine's own restart (a re-entered one-shot), writes.
                    PlaySequence: (sequence, _, restart) =>
                    {
                        if (restart || sequence != _skeletonSequence)
                            _render.SetSequence(Handle, sequence);

                        _skeletonSequence = sequence;
                    }));

            _animTarget.SetHandle(Handle);
        }

        _animState = new SourceSharp.Shared.Anim.MultiplayerAnimState(
            _animTarget,
            SourceSharp.Shared.Anim.AnimStateParams.Hl2Defaults(),
            _animLayers?.ChainFor(_animTarget)
                ?? SourceSharp.Shared.Anim.AnimStateLayers.CoreOnly(_animTarget));
        return _animState;
    }

    /// <summary>The server's copy of the machine, or null before it first runs (reports; the pose driver's future entry).</summary>
    public SourceSharp.Shared.Anim.MultiplayerAnimState? AnimStateOrNull => _animState;

    /// <summary>
    /// What one hit takes off a player at <paramref name="health"/>. Pure so
    /// the rule is testable apart from the engine: god takes nothing, buddha
    /// takes at most down to 1 (a buddha already AT 1 takes nothing), and
    /// otherwise the whole hit lands, as stock.
    /// </summary>
    public static int DamageTaken(int health, int damage, bool god, bool buddha)
    {
        if (god)
            return 0;

        if (buddha)
            return Math.Min(damage, Math.Max(0, health - 1));

        return damage;
    }

    /// <summary>
    /// The player's energy scale for impact damage: stock's
    /// <c>m_impactEnergyScale</c>, 1 for every combat character.
    /// </summary>
    public const float ImpactEnergyScale = 1f;

    /// <summary>
    /// Stock's <c>CBaseCombatCharacter::VPhysicsShadowCollision</c>: a moving
    /// prop that hits the player's shadow hurts them by the player table, with
    /// no static damage, unless it is the world, not a moveable VPHYSICS
    /// mover, or what the player is standing on. The prop is the attacker.
    /// Stock's "the player was this prop's physics attacker this frame" skip
    /// has nothing to read: no holder in this mod records a physics attacker.
    /// </summary>
    public override void OnVPhysicsShadowCollision(in PhysicsEvent ev, int index)
    {
        int otherSide = 1 - index;
        uint otherRaw = otherSide == 0 ? ev.Entity0 : ev.Entity1;
        if (otherRaw == PhysicsEvent.NoEntity)
            return;

        if ((otherSide == 0 ? ev.MoveType0 : ev.MoveType1) != (int)MoveType.VPhysics)
            return;
        if (!ev.IsMoveable(otherSide))
            return;

        var other = new EntityHandle(otherRaw);
        if (Entities.GetIndex(other) == State.GroundEntity)
            return;

        var parties = new ImpactParties(SelfIsPlayer: true, OtherIsPlayer: Entities.IsPlayer(other),
                                        OtherIsWorld: false, SelfIsFloating: _fluid.IsFloating(Handle));
        float damage = PhysicsImpactDamage.Calculate(in ev, index, ImpactDamageTables.Player,
                                                     ImpactEnergyScale, allowStaticDamage: false,
                                                     damageFromHeldObjects: false, parties,
                                                     out DamageType damageType);
        if (damage > 0f)
            _impacts.Add(Handle, other, damage, damageType, in ev, index);
    }

    /// <summary>
    /// Stock's <c>phys_stressbodyweights</c>: how many times their own weight
    /// a body can hold up before the squeeze is a crush.
    /// </summary>
    public const float StressBodyWeights = 5f;

    /// <summary>
    /// Stock's <c>ApplyStressDamage</c>'s damage, 200 DMG_CRUSH.
    /// </summary>
    public const float StressDamage = 200f;

    /// <summary>
    /// Stock's <c>CBasePlayer::VPhysicsUpdate</c>: the hull's received stress
    /// at an eighth of <see cref="ImpactEnergyScale"/>, per kilogram of the
    /// hull, over <see cref="StressBodyWeights"/> with a non-static pusher, is
    /// <see cref="StressDamage"/> crush -- unless the hull is penetrating
    /// (stuck, not squeezed), and only when one of the pushers is a large
    /// object (<c>ApplyStressDamage( pPhysics, true )</c>). Stock's attacker
    /// is the world: see <see cref="PhysicsDamageQueue.AddStress"/>, which HL2MP
    /// scores the same (-1 frag). The damage force (received stress x gravity x frametime, downwards) is
    /// not given: the ABI's direction is a blast direction, not a force.
    /// </summary>
    public override void OnVPhysicsStress(in PhysicsEvent ev)
    {
        float mass = ev.Energy;
        if (mass <= 0f)
            return;

        float stress = ev.Speed * (ImpactEnergyScale * 0.125f) / mass;
        if ((ev.Flags & PhysicsEvent.StressNonStatic) == 0 || stress <= StressBodyWeights)
            return;
        if ((ev.Flags & PhysicsEvent.StressPenetrating) != 0)
            return;
        if ((ev.Flags & PhysicsEvent.StressLargeObject) == 0)
            return;

        _impacts.AddStress(Handle, StressDamage, ev.Point);
    }

    public override void OnKilled(EntityHandle attacker)
    {
        State.Alive = false;
        Observer.DeathTime = GameTime;   // m_flDeathTime

        // CBasePlayer::Event_Killed: AddSolidFlags( FSOLID_NOT_SOLID )
        // (player.cpp:1723). The collision rules read it, so the dying player's
        // shadow stops blocking and pushing props now rather than when observer
        // mode comes a think later; leaving observer at respawn makes the player
        // solid again (A22).
        SetNotSolid(true);

        // STOP DRAWING THEM, because the body standing here is now a separate
        // entity (SsRagdoll) and two of them in the same place is not a
        // cosmetic problem: the corpse, the still-animating player and the
        // weapon bone-merged to that player were all drawn on top of each
        // other, which reads as one badly deformed model rather than as three
        // models.
        //
        // Stock says the same thing in the same place --
        // CHL2MP_Player::Event_Killed's "since we're dead, it won't draw us on
        // the client, but we don't set EF_NODRAW because we still want to
        // transmit to the clients in our PVS". Stock gets it for free from
        // C_BasePlayer, which declines to draw a dead player; the standalone
        // player has no such rule, so it is said out loud here.
        //
        // NOT removed and not moved: the entity still has to network, because
        // it is the same entity that will respawn, and the observer camera the
        // death cycle hands out rides on it.
        _render.SetVisible(Handle, false);

        Log.Msg("[SourceSharp/C#] ss_player died\n");
    }
}
