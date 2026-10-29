//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// CBreakableProp (props.cpp): what every studio-model prop that can break
/// shares, whether it is simulated (<see cref="PropPhysics"/>) or scenery
/// (<see cref="PropDynamic"/>). Its health and damage modifiers come from its
/// model's prop data. It catches fire as its fire interactions say, and keeps
/// track of who hurt it. When it breaks it explodes, makes its pieces and is
/// gone. Its health, physcannon and punt-sound inputs are here too.
/// </summary>
public abstract class BreakableProp : AnimatingEntity, IPhysGunTarget
{
    protected BreakableProp(IEngineDamage damage, IEngineEntityPhysics physics, IEngineWorldQuery world,
                            IEngineEffects effects, IEnginePrecache precache, IEngineHost host, IBlastSweep blast,
                            IServerDecals decals, IEngineEntityRender render, IServerPropData propData,
                            PhysicsSoundBatch sounds, DamageInflictors inflictors, IEngineSound sound,
                            ITempEnts temps, IEngineEnvironment environment, ConVarRegistry? convars = null)
    {
        Temps = temps;
        Environment = environment;
        ConVars = convars;
        Sound = sound;
        Inflictors = inflictors;
        Sounds = sounds;
        Render = render;
        PropDataTable = propData;
        Damage = damage;
        Physics = physics;
        World = world;
        Effects = effects;
        Precache = precache;
        Host = host;
        Blast = blast;
        Decals = decals;
    }

    /// <summary>The inflictor of the blow being dealt now, which explosive_resist measures from.</summary>
    private readonly DamageInflictors Inflictors;

    /// <summary>The PhysicsProp temp entity a multiplayer break sends its pieces to the clients in.</summary>
    private readonly ITempEnts Temps;

    /// <summary>gpGlobals->maxClients, which says whether this is multiplayer.</summary>
    private readonly IEngineEnvironment Environment;

    /// <summary>breakable_multiplayer.</summary>
    private readonly ConVarRegistry? ConVars;

    /// <summary>The burst an explode_fire prop makes as it goes, and the punt sound.</summary>
    protected readonly IEngineSound Sound;

    /// <summary>
    /// The managed blast traversal — the falloff, the line of sight and the
    /// blocking rule that used to run inside <c>CGameRules::RadiusDamage</c>.
    /// See <see cref="BlastSweep"/>.
    /// </summary>
    private readonly IBlastSweep Blast;

    /// <summary>the decal system, whose "Scorch" group the blast paints from.</summary>
    private readonly IServerDecals Decals;

    /// <summary>The model's $keyvalues text, for its prop data.</summary>
    protected readonly IEngineEntityRender Render;

    /// <summary>propdata.txt, which a model's prop_data names as its base.</summary>
    protected readonly IServerPropData PropDataTable;

    /// <summary>tracing, and what is at a point. For the blast.</summary>
    protected readonly IEngineWorldQuery World;

    /// <summary>the fireball, the scorch mark and the sparks.</summary>
    protected readonly IEngineEffects Effects;

    /// <summary>Getting the blast's sprite and decal onto the client, and
    /// asking a model what it is made of before the level runs. Two lanes
    /// added this field independently -- explosions and break pieces -- and
    /// the compiler caught the duplicate at merge.</summary>
    protected readonly IEnginePrecache Precache;

    /// <summary>the engine's random stream, which the blast's sparks draw from.</summary>
    protected readonly IEngineHost Host;

    /// <summary>hurting things.</summary>
    protected readonly IEngineDamage Damage;

    /// <summary>this entity as the solver sees it.</summary>
    protected readonly IEngineEntityPhysics Physics;

    /// <summary>The queued impact and break sounds (CPhysicsHook's batch, managed).</summary>
    protected readonly PhysicsSoundBatch Sounds;

    /// <summary>CBreakableProp's keyfields. See <see cref="BreakablePropVars"/>.</summary>
    [ScriptVars] public BreakablePropVars BreakableVars = new();

    /// <summary>The model the map named, which the pieces are read out of.</summary>
    protected abstract string MapModel { get; }

    /// <summary>m_hLastAttacker: the last player to hurt it, credited with what its explosion kills.</summary>
    [SaveField] private EntityHandle _lastAttacker;

    /// <summary>m_hPhysicsAttacker and m_flLastPhysicsInfluenceTime: who last held or threw it, and when.</summary>
    [SaveField] private EntityHandle _physicsAttacker;

    [SaveField] private float _physicsInfluenceTime = -1000f;

    /// <summary>CBreakableProp::SetPhysicsAttacker.</summary>
    protected void SetPhysicsAttacker(EntityHandle player)
    {
        _physicsAttacker = player;
        _physicsInfluenceTime = GameTime;
    }

    /// <summary>CBreakableProp::HasPhysicsAttacker: who held or threw it within the last <paramref name="dt"/> seconds.</summary>
    private EntityHandle PhysicsAttackerWithin(float dt)
        => GameTime - dt <= _physicsInfluenceTime ? _physicsAttacker : EntityHandle.None;

    private PropBreak? _breaker;

    /// <summary>
    /// What this prop breaks into, and how to make it.
    ///
    /// <para>
    /// Built on first use rather than in the constructor, and that is not
    /// laziness for its own sake: <see cref="ScriptedEntity.Entities"/> is
    /// BOUND after construction, so a breaker made in the constructor would
    /// hold the default binding — the real engine — however the entity was
    /// wired. In a test that means an engine that answers zero to everything,
    /// silently, which is the one thing the double exists to prevent.
    /// </para>
    /// </summary>
    protected PropBreak Breaker => _breaker ??= new PropBreak(Log, Entities, Physics, Precache);
    public readonly Output OnBreak = new();
    public readonly Output OnHealthChanged = new();

    /// <summary>m_OnTakeDamage: every blow it takes, naming the attacker.</summary>
    public readonly Output OnTakeDamage = new();

    private bool _broken;
    private int _startingHealth;

    /// <summary>How big a bang this model makes when it breaks, if any.</summary>
    private float _explosiveDamage;
    private float _explosiveRadius;
    private bool _propDataRead;

    /// <summary>
    /// How many pieces this model breaks into — stock's
    /// <c>m_iNumBreakableChunks</c>, filled at the same moment stock fills it.
    ///
    /// <para>
    /// It is not only the loop count. It is also what tells a GIB whether it may
    /// break again: stock refuses damage to a fading piece that has no pieces of
    /// its own, so a splinter cannot be shot into smaller splinters forever.
    /// </para>
    /// </summary>
    private int _breakPieces;

    /// <summary>What the piece count says, for tests, which have no models.</summary>
    internal int BreakPieceCount => _breakPieces;

    /// <summary>Whether this prop has taken itself out of the world. For tests, which have no engine.</summary>
    internal bool Gone { get; private set; }

    /// <summary>
    /// Out of the world, and said so where a test can see it.
    /// </summary>
    protected void Vanish()
    {
        Gone = true;
        Remove();
    }

    /// <summary>
    /// CBreakableProp::Precache, from a spawn: what the model breaks into,
    /// precached HERE because here is inside the level-load window. Stock says
    /// the same thing in the same place — CBreakableProp::Precache is where
    /// m_iNumBreakableChunks comes from — and it has to be here: a piece is
    /// created when something breaks, by which time the map is long since
    /// built and a precache is a console line and nothing drawn.
    ///
    /// <para>
    /// By NAME, not through this entity, which is what makes it possible at
    /// all: the pieces are read out of the model file, and a piece's own
    /// pieces are read by recursing on a name with no entity behind it. Then
    /// CBaseProp::Precache's explode_fire burst and the map's punt sound.
    /// </para>
    /// </summary>
    protected void PrecacheBreakable()
    {
        if (MapModel.Length > 0)
            _breakPieces = Breaker.Precache(MapModel);

        Precache.PrecacheSound(PropaneTankBurst);
        if (BreakableVars.PuntSound.Length > 0)
            Precache.PrecacheSound(BreakableVars.PuntSound);
    }

    /// <summary>
    /// The health this prop starts with, resolved on first use.
    ///
    /// The model, not the map, is where a prop's health usually comes from: no
    /// map gives an explosive barrel a "health" key, and its health, damage
    /// modifiers and explosion are all compiled into the model and resolved
    /// through scripts/propdata.txt. Reading only map keys made every barrel in
    /// dm_lockdown scenery, and scenery ignores damage entirely.
    ///
    /// Lazy rather than eager because a physics prop's model is not set until
    /// after OnSpawn.
    /// </summary>
    protected int StartingHealth
    {
        get
        {
            if (_propDataRead)
                return _startingHealth;

            _propDataRead = true;

            // The model's own prop_data over propdata.txt's base chain, parsed in
            // C# (plan_server_novalve.md P2.4; SourceSharp_EntityGetPropData
            // parsed it with tier1's KeyValues).
            string? text = Render.GetModelKeyValueText(Render.GetModelName(Handle));
            PropDataValues model = PropDataTable.ModelPropValues(text);
            _interactions = PropDataTable.ModelInteractions(text);
            _damageModifiers = PropDataTable.ModelDamageModifiers(text);

            // ParsePropFromKV: the model's prop_data health and explosion over
            // the map's keys where it sets them, except on an override class,
            // which keeps the map's (the reason to place one).
            _startingHealth = IsOverride ? BreakableVars.Health : model.Health ?? BreakableVars.Health;
            _explosiveDamage = IsOverride ? BreakableVars.ExplodeDamage : model.ExplosiveDamage ?? BreakableVars.ExplodeDamage;
            _explosiveRadius = IsOverride ? BreakableVars.ExplodeRadius : model.ExplosiveRadius ?? BreakableVars.ExplodeRadius;
            _multiplayerBreak = model.MultiplayerBreak ?? MultiplayerBreak.Default;
            _chunkGroup = model.BreakableModel ?? string.Empty;

            // CBreakableProp::Spawn: with no pieces of its own, a prop breaks
            // into its prop data's generic chunks, if it names some.
            if (_breakPieces == 0 && _chunkGroup.Length > 0 && model.BreakableCount is > 0 and var chunks)
                _breakPieces = chunks;

            // And it takes damage only with health and something to do with
            // it: pieces, or one of the interactions that break or burn it.
            // Otherwise it is DAMAGE_EVENTS_ONLY at no health.
            const PropInteractions Breaks = PropInteractions.PhysgunBreakExplode | PropInteractions.PhysgunFirstBreak
                                          | PropInteractions.FireFlammable | PropInteractions.FireIgniteHalfHealth
                                          | PropInteractions.FireExplosiveResist;
            if (_breakPieces == 0 && (_interactions & Breaks) == 0)
                _startingHealth = 0;

            return _startingHealth;
        }
    }

    private PropInteractions _interactions;

    /// <summary>m_mpBreakMode: which DLL makes its pieces in multiplayer, from its prop_data.</summary>
    private MultiplayerBreak _multiplayerBreak;

    /// <summary>m_iszBreakableModel: the propdata.txt chunk group it breaks into without pieces of its own.</summary>
    private string _chunkGroup = string.Empty;

    /// <summary>Placed as an _override class, whose map keys stand over its model's prop_data (OverridePropdata).</summary>
    [SaveField] protected bool IsOverride;

    private PropDamageModifiers _damageModifiers = new(1f, 1f, 1f);

    /// <summary>CBaseProp::HasInteraction: the model's interactions, read with its prop data.</summary>
    public bool HasInteraction(PropInteractions interaction)
    {
        _ = StartingHealth;
        return (_interactions & interaction) != 0;
    }

    /// <summary>
    /// CBreakableProp::Ignite: only a flammable model catches, and only once.
    /// Stock also lights it (EF_DIMLIGHT) when the rules ask for burning props
    /// to emit light, which HL2MP's do not, and calls its danger sound to NPCs,
    /// of which HL2MP has none.
    /// </summary>
    public override void Ignite(float lifetime, float size = 0f)
    {
        if (IsOnFire || !HasInteraction(PropInteractions.FireFlammable))
            return;

        base.Ignite(lifetime, size);
    }

    /// <summary>
    /// Back to whole: unbroken, at its starting health. For a prop that hides
    /// to come back rather than going.
    /// </summary>
    protected void Unbreak()
    {
        _broken = false;
        Health = StartingHealth;
    }

    //-------------------------------------------------------------------------
    // The gravity gun (CBreakableProp's CDefaultPlayerPickupVPhysics).
    //-------------------------------------------------------------------------

    /// <summary>SF_PHYSPROP_PREVENT_PICKUP, for a class that has it.</summary>
    bool IPhysGunTarget.PreventsPickup => PreventsPhysGunPickup;

    protected virtual bool PreventsPhysGunPickup => false;

    /// <summary>SF_PHYSPROP_ENABLE_ON_PHYSCANNON, for a class that has it.</summary>
    bool IPhysGunTarget.EnablesOnPhyscannon => EnablesOnPhyscannon;

    protected virtual bool EnablesOnPhyscannon => false;

    /// <summary>EFL_NO_PHYSCANNON_INTERACTION, which DisablePhyscannonPickup sets.</summary>
    bool IPhysGunTarget.NoPhyscannonInteraction => _noPhyscannon;

    [SaveField] private bool _noPhyscannon;

    [ScriptedEvent] private void OnEnablePhyscannonPickup() => _noPhyscannon = false;

    [ScriptedEvent] private void OnDisablePhyscannonPickup() => _noPhyscannon = true;

    /// <summary>m_bUsePuntSound: on unless DisablePuntSound.</summary>
    [SaveField] private bool _usePuntSound = true;

    [ScriptedEvent] private void OnEnablePuntSound() => _usePuntSound = true;

    [ScriptedEvent] private void OnDisablePuntSound() => _usePuntSound = false;

    /// <summary>CBreakableProp::PlayPuntSound: the map's puntsound, while it is on.</summary>
    private void PlayPuntSound()
    {
        if (_usePuntSound && BreakableVars.PuntSound.Length > 0)
            Sound.EntityEmitSound(Handle, BreakableVars.PuntSound);
    }

    void IPhysGunTarget.OnPhysGunPickup(EntityHandle player, PhysGunPickup reason) => PhysGunPickedUp(player, reason);

    void IPhysGunTarget.OnPhysGunDrop(EntityHandle player, PhysGunDrop reason) => PhysGunDropped(player, reason);

    /// <summary>CBreakableProp::OnPhysGunPickup: a punt's sound, and who last held it.</summary>
    protected virtual void PhysGunPickedUp(EntityHandle player, PhysGunPickup reason)
    {
        if (reason == PhysGunPickup.PuntedByCannon)
            PlayPuntSound();

        SetPhysicsAttacker(player);
    }

    /// <summary>
    /// CBreakableProp::OnPhysGunDrop: who last held it. Its punt-sound test
    /// compares the DROP reason with PUNTED_BY_CANNON, a PICKUP reason, and
    /// both are 1: so it is a throw by hand (THROWN_BY_PLAYER) that plays the
    /// punt sound here. Ported as stock has it.
    /// </summary>
    protected virtual void PhysGunDropped(EntityHandle player, PhysGunDrop reason)
    {
        SetPhysicsAttacker(player);

        if (reason == PhysGunDrop.ThrownByPlayer)
            PlayPuntSound();
    }

    //-------------------------------------------------------------------------
    // Health and damage.
    //-------------------------------------------------------------------------

    [ScriptedEvent("SetHealth")] private void OnSetHealthInput(InputData input) => UpdateHealth((int)input.Number, input.Activator);

    [ScriptedEvent("AddHealth")] private void OnAddHealthInput(InputData input) => UpdateHealth(Health + (int)input.Number, input.Activator);

    [ScriptedEvent("RemoveHealth")] private void OnRemoveHealthInput(InputData input) => UpdateHealth(Health - (int)input.Number, input.Activator);

    /// <summary>
    /// CBreakableProp::UpdateHealth: the health, its ratio of the max out, and
    /// a break (as by itself) when none is left. The max does not move.
    /// </summary>
    private void UpdateHealth(int health, EntityHandle activator)
    {
        if (health == Health)
            return;

        Health = health;
        float ratio = Math.Clamp(health / (float)Math.Max(1, StartingHealth), 0f, 1f);
        OnHealthChanged.Fire(ratio.ToString(System.Globalization.CultureInfo.InvariantCulture), activator);

        if (health <= 0)
            Break(Handle);
    }

    /// <summary>InputBreak: broken, as by itself (its attacker is itself).</summary>
    [ScriptedEvent("Break")] private void OnBreakInput() => Break(Handle);

    /// <summary>
    /// CBreakableProp's physdamagescale input: m_impactEnergyScale, which the
    /// next impact reads (props.cpp:775; A38).
    /// </summary>
    [ScriptedEvent("physdamagescale")]
    internal void OnPhysDamageScaleInput(float scale) => BreakableVars.PhysDamageScale = scale;

    /// <summary>
    /// CBreakableProp::OnTakeDamage: under minhealthdmg the blow is refused
    /// before the base, so it neither hurts nor pushes (props.cpp:1045; A38).
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
        => damage < BreakableVars.MinHealthDamage ? 0 : OnDamaged(attacker, damage, damageType);

    /// <summary>
    /// Whether a blow may hurt it at all: not once broken, and not with no
    /// health to lose (CBreakableProp::Spawn's DAMAGE_EVENTS_ONLY).
    /// </summary>
    protected virtual bool CanTakeDamage => !_broken && StartingHealth > 0;

    /// <summary>A blow it would not take, for a class that does something with one.</summary>
    protected virtual void RefusedDamage(DamageType damageType) { }

    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (!CanTakeDamage)
        {
            RefusedDamage(damageType);
            return 0;
        }

        // m_hLastAttacker: a combat character, which in HL2MP is a player.
        if (attacker != EntityHandle.None && Entities.IsPlayer(attacker))
            _lastAttacker = attacker;

        // GetBreakableDamage: the model's damage modifiers, read with its prop data.
        _ = StartingHealth;
        damage = BreakableDamage.Scale(damage, damageType, _damageModifiers);

        // Ignore fire damage from other flames if already on fire: only the
        // flame attached to it burns it.
        if (IsOnFire && (damageType & DamageType.Burn) != 0 && (damageType & DamageType.Direct) == 0)
            return 0;

        damage = FireInteractions(damage, damageType);

        int taken = (int)damage;
        if (taken <= 0)
            return 0;

        OnHealthChanged.Fire(Math.Max(0, Health - taken) / (float)StartingHealth);
        OnTakeDamage.Fire(attacker);
        return taken;
    }

    /// <summary>What an explode_fire prop's break sounds like.</summary>
    private const string PropaneTankBurst = "PropaneTank.Burst";

    /// <summary>PROP_EXPLOSION_IGNITE_RADIUS: a blast nearer than this sets it off at once.</summary>
    private const float ExplosionIgniteRadius = 32f;

    /// <summary>
    /// CBreakableProp::OnTakeDamage's fire interactions, answering the damage
    /// it then takes. A blast that would kill an explosive_resist prop burns it
    /// instead, for longer the further it was; a blast or a burn that does not
    /// kill sets it alight; and a bullet that takes an ignite-halfhealth prop to
    /// half sets it alight at full health, or, when it is burning already,
    /// sets it off.
    /// </summary>
    private float FireInteractions(float damage, DamageType damageType)
    {
        int health = Health;
        bool deadly = damage >= health;

        if (deadly && (damageType & DamageType.Blast) != 0 && HasInteraction(PropInteractions.FireExplosiveResist)
            && Inflictors.TryCurrent(out EntityHandle inflictor, out _) && inflictor != EntityHandle.None)
        {
            float distance = (Entities.GetWorldSpaceCenter(Handle) - Entities.GetWorldSpaceCenter(inflictor)).Length;

            float burnTime;
            if (distance >= ExplosionIgniteRadius)
            {
                // Far from the blast: ignite and burn for several seconds.
                const float MaxBlastDistance = 256f, MaxBurnTime = 5f;
                distance = Math.Min(distance, MaxBlastDistance);
                burnTime = Math.Max(0.5f, MaxBurnTime * (distance / MaxBlastDistance));
                burnTime += Host.RandomFloat(0f, 0.5f);
            }
            else
            {
                // Very near the explosion: explode almost immediately.
                burnTime = Host.RandomFloat(0.1f, 0.2f);
            }

            // The health that burns for burnTime seconds, and the damage that leaves it.
            float idealHealth = Math.Min(health, FlameRules.DirectDamagePerSecond * burnTime);
            damage = health - idealHealth;
            deadly = damage >= health;
        }

        if (!deadly && (damageType & DamageType.Blast) != 0)
        {
            Ignite(Host.RandomFloat(10f, 15f));
        }
        else if (!deadly && (damageType & DamageType.Burn) != 0)
        {
            Ignite(Host.RandomFloat(10f, 15f));
        }
        else if (!deadly && (damageType & DamageType.Bullet) != 0 && HasInteraction(PropInteractions.FireIgniteHalfHealth))
        {
            if (health - damage <= StartingHealth / 2 && !IsOnFire)
            {
                // Back up to full health so it burns longer.
                Health = StartingHealth;
                Ignite(Host.RandomFloat(10f, 15f));
            }
            else if (IsOnFire)
            {
                // Explode right now.
                damage = health;
            }
        }

        return damage;
    }

    /// <summary>
    /// CBreakableProp::Event_Killed: it breaks, then CBaseEntity::Event_Killed
    /// removes it (or a subclass does what it does instead).
    /// </summary>
    public override void OnKilled(EntityHandle attacker)
    {
        Break(attacker);
        Broken();
    }

    /// <summary>
    /// Breaking, which is the same for every breakable prop and therefore not
    /// virtual.
    ///
    /// It was virtual once, and prop_physics_respawnable overrode it to hide
    /// itself instead — which silently dropped the explosion, because the bang
    /// lived in the half that was replaced. Every explosive barrel in
    /// dm_lockdown is a respawnable one, so the map's twenty-one barrels all
    /// died quietly. A subclass now chooses only what becomes of the prop
    /// afterwards; it cannot skip the parts that are common to breaking.
    ///
    /// <para>
    /// It does not remove the prop: HL2MP compiles CBreakableProp::Break's
    /// UTIL_Remove out (#ifndef HL2MP). A prop broken by Break or by its health
    /// inputs stays where it was, not solid and still drawn, as stock's does;
    /// only the damage path's Event_Killed removes it (<see cref="OnKilled"/>).
    /// </para>
    /// </summary>
    protected void Break(EntityHandle attacker)
    {
        // Once. A prop hit twice in a tick would otherwise fire its outputs
        // twice and be removed twice.
        if (_broken)
            return;

        _broken = true;
        OnBreak.Fire(attacker);

        // FSOLID_NOT_SOLID, before anything else can meet it.
        Physics.SetNotSolid(Handle, true);

        // CBreakableProp::Break's PhysBreakSound( this, VPhysicsGetObject(),
        // GetAbsOrigin() ), before the explosion and the pieces (props.cpp:1673;
        // physics stock parity L).
        Sounds.AddBreakSound(Handle);

        // The bang is the model's, and it happens before the prop goes: the
        // explosion needs somewhere to be. Touching StartingHealth first is
        // what guarantees the prop data has been read, for a prop broken by an
        // input rather than by damage.
        _ = StartingHealth;

        // Credited to the last player to hurt it, or else to whoever held or
        // threw it in the last two seconds, or else to the blow's attacker.
        if (_lastAttacker != EntityHandle.None)
            attacker = _lastAttacker;
        else if (PhysicsAttackerWithin(2f) is var thrower && thrower != EntityHandle.None)
            attacker = thrower;

        // At its middle, with stock's flags. ignored: Handle -- a barrel that
        // explodes where it stands must say so, or its own hull stops the very
        // first trace of the traversal and every victim in the radius is scored
        // at distance zero. See Explosions.Fire.
        if (_explosiveDamage > 0f || _explosiveRadius > 0f)
        {
            const EnvExplosion.SpawnFlag Flags = EnvExplosion.SpawnFlag.NoSparks | EnvExplosion.SpawnFlag.NoDynamicLights
                                               | EnvExplosion.SpawnFlag.NoSmoke | EnvExplosion.SpawnFlag.SurfaceOnly;
            Vector centre = Entities.GetWorldSpaceCenter(Handle);

            if (HasInteraction(PropInteractions.PhysgunBreakExplode))
            {
                Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                                centre, _explosiveDamage, _explosiveRadius, attacker,
                                ignored: Handle, flags: Flags | EnvExplosion.SpawnFlag.NoSound, inflictor: Handle);
                Sound.EntityEmitSound(Handle, PropaneTankBurst);
            }
            else
            {
                Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                                centre, _explosiveDamage * ModelScale, _explosiveRadius * ModelScale, attacker,
                                ignored: Handle, flags: Flags, inflictor: Handle);
            }
        }

        // In multiplayer the pieces are the clients' to make, from a temp
        // entity, unless the prop's data puts them on the server; the server
        // then makes only the pieces marked for it.
        if (Multiplayer)
        {
            if (_multiplayerBreak is MultiplayerBreak.Default or MultiplayerBreak.ClientSide or MultiplayerBreak.Both)
                SendPieces();

            if (_multiplayerBreak is MultiplayerBreak.ServerSide or MultiplayerBreak.Both)
                CreatePieces(BreakPieceSide.ServerMarked);
        }
        else
        {
            CreatePieces(BreakPieceSide.All);
        }
    }

    private bool Multiplayer => BreakPieceSides.InMultiplayer(Environment, ConVars);

    /// <summary>
    /// CBreakableProp::Break's te->PhysicsProp: the clients in the PAS of its
    /// middle each break their own copy of it into pieces (the breakmodel
    /// flag), from where it is and as fast as it was going.
    /// </summary>
    private void SendPieces()
    {
        const int BreakModelFlag = 1;
        int modelIndex = Precache.PrecacheModel(MapModel);
        Vector angles = Entities.GetAngles(Handle);

        Temps.PhysicsProp(TeFilters.Pas(Entities.GetWorldSpaceCenter(Handle)), -1f, modelIndex, Skin,
                          Entities.GetOrigin(Handle), new QAngle(angles.X, angles.Y, angles.Z),
                          Entities.GetVelocity(Handle), BreakModelFlag,
                          EngineBlockEffects.Read(Entities.GetEngineBlock(Handle)));
    }

    /// <summary>
    /// The debris, which is what a player actually sees a prop do when it
    /// breaks.
    ///
    /// <para>
    /// BEFORE <see cref="Broken"/>, and that order is the whole of it: the
    /// pieces are placed relative to where the prop is and thrown along what it
    /// was doing, and the default <see cref="Broken"/> removes it. Reading a
    /// removed entity's origin and velocity gives a heap of debris at the world
    /// origin, motionless.
    /// </para>
    ///
    /// <para>
    /// The prop is handed on as each piece's owner. Stock does the same, through
    /// <c>CreateNoSpawn( "prop_physics", position, angles, pOwner )</c>, and it
    /// is what keeps a piece from colliding with the thing it came out of.
    /// </para>
    /// </summary>
    private void CreatePieces(BreakPieceSide side)
    {
        if (_breakPieces == 0 || MapModel.Length == 0)
            return;

        var context = new BreakContext(
            // Through the injected area rather than the base class's Origin
            // property, so the whole context comes from one place — the property
            // is a static-engine convenience and would answer from a different
            // engine than the four lines below it.
            Origin: Entities.GetOrigin(Handle),
            Angles: Entities.GetAngles(Handle),
            Velocity: Entities.GetVelocity(Handle),
            AngularVelocity: Entities.GetAngularVelocity(Handle),

            // Stock sets this unconditionally in CBreakableProp::Break: "no
            // damage/damage force? set a burst of 100 for some movement".
            DefaultBurstScale: PropBreakPlan.DefaultBurstScale,

            DefaultCollisionGroup: PropBreakPlan.GroupForPieces(
                Physics.GetCollisionGroup(Handle)),

            SpawnFlags: SpawnFlags);

        // "If we're burning, break into burning pieces": each piece alight for
        // what the flame had left, with its hitbox fires (PropBreakableCreateAll).
        SsEntityFlame? flame = Flame;
        PiecesMade = Breaker.CreateAll(MapModel, Handle, context, flame is null ? null : piece =>
        {
            if (FindByHandle<AnimatingEntity>(piece) is not { } burning)
                return;

            burning.Ignite(flame.RemainingLife);
            burning.IgniteNumHitboxFires(flame.NumHitboxFires);
            burning.IgniteHitboxFireScale(flame.HitboxFireScale);
        }, side);
    }

    /// <summary>How many pieces the last break made. For tests, and for a report.</summary>
    internal int PiecesMade { get; private set; }

    /// <summary>
    /// What becomes of the prop once a blow has broken it. Gone, unless a
    /// subclass wants it back.
    /// </summary>
    protected virtual void Broken() => Vanish();
}

/// <summary>
/// The map keys every <see cref="BreakableProp"/> declares: CBreakableProp's
/// keyfields, and CBaseEntity's health, which its spawn reads.
/// </summary>
[ScriptVars]
public partial class BreakablePropVars
{
    public int Health { get; set; }

    /// <summary>m_iszPuntSound: what the gravity gun's punt sounds like on it.</summary>
    [VarName("puntsound")]
    public string PuntSound { get; set; } = string.Empty;

    /// <summary>m_explodeDamage and m_explodeRadius: the map's explosion, which a model's prop_data replaces.</summary>
    public float ExplodeDamage { get; set; }

    public float ExplodeRadius { get; set; }

    /// <summary>
    /// m_impactEnergyScale: how much of a physics impact's energy this prop
    /// feels. 0 means stock's default, 0.1.
    /// </summary>
    [VarName("physdamagescale")]
    public float PhysDamageScale { get; set; }

    /// <summary>
    /// m_iMinHealthDmg: a blow under this is refused outright, neither hurting
    /// nor pushing the prop.
    /// </summary>
    [VarName("minhealthdmg")]
    public int MinHealthDamage { get; set; }
}
