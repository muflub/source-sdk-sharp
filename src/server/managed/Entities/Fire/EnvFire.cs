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
/// env_fire: something burning.
///
/// <para>
/// The FLAME stays native — <c>_firesmoke</c> and <c>_plasma</c> are particle
/// systems with a glow, and there is no decision anywhere in them. What moved is
/// everything about the fire: how hot it gets, how long its fuel lasts, how far
/// it reaches, what it hurts, and what it spreads to.
/// </para>
///
/// <para>
/// The fire SYSTEM came with it. Stock keeps a native registry so that sources
/// and sensors can find fires in a radius; with the fires themselves managed
/// there is nothing left for that registry to hold, so it is the server
/// module's <see cref="IEntityRoster{T}"/> of fires (it was a static list here),
/// and <c>env_firesource</c>, <c>env_firesensor</c>, the flame and the one
/// native caller that still needs it ask <see cref="InRadius"/> over it.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFire)]
public sealed class EnvFire : ScriptedEntity
{
    public EnvFire(IGameClock clock, IEngineDamage damage, IEnginePrecache precache, IEngineNetworking net, IEngineWorldQuery world,
                   IEntityRoster<EnvFire> fires)
    {
        _fires = fires;
        Clock = clock;
        Damage = damage;
        Precache = precache;
        Net = net;
        World = world;
    }

    /// <summary>what the server thinks the time is.</summary>
    private readonly IGameClock Clock;

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>loading what the flame is drawn with.</summary>
    private readonly IEnginePrecache Precache;

    /// <summary>putting this entity’s state on the wire.</summary>
    private readonly IEngineNetworking Net;

    /// <summary>asking the world what is there.</summary>
    private readonly IEngineWorldQuery World;
    /// <summary>This entity's own spawnflags, as their own type.</summary>
    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>What a map may set on this entity. See <see cref="EnvFireVars"/>.</summary>
    [ScriptVars] public EnvFireVars Vars = new();

    // Stock's SF_FIRE_*. The values matter and are not sequential: START_ON is
    // 4, not 1, and a map keyed against the real numbers would light the wrong
    // fires if these were renumbered to look tidy.

    /// <summary>
    /// env_fire's own spawnflags, stock's SF_FIRE_*.
    ///
    /// <para>
    /// The values matter and are not sequential: START_ON is 4, not 1, and a map
    /// keyed against the real numbers would light the wrong fires if these were
    /// renumbered to look tidy. 0x040 is skipped because stock skips it.
    /// </para>
    /// </summary>
    [Flags]
    private enum SpawnFlag
    {
        /// <summary>No bit set. What an entity whose mapper ticked nothing reads as.</summary>
        None = 0b0000_0000_0000_0000,

        Infinite = 0b0000_0000_0000_0001,
        Smokeless = 0b0000_0000_0000_0010,
        StartOn = 0b0000_0000_0000_0100,
        StartFull = 0b0000_0000_0000_1000,
        DontDrop = 0b0000_0000_0001_0000,
        NoGlow = 0b0000_0000_0010_0000,
        DiePermanent = 0b0000_0000_1000_0000,
        VisibleFromAbove = 0b0000_0001_0000_0000,  // 0x100
    }

    /// <summary>How far down a fire looks for the floor to sit on.</summary>
    private const float DropDistance = 1024f;

    public readonly Output OnIgnited = new();
    public readonly Output OnExtinguished = new();

    /// <summary>
    /// Every fire in the map — stock's native registry, as the module's roster
    /// now that the fires are managed.
    /// </summary>
    private readonly IEntityRoster<EnvFire> _fires;

    private EntityHandle _effect = EntityHandle.None;

    /// <summary>
    /// The flame, when it is a PLASMA one, as the class rather than the handle.
    ///
    /// Only a plasma flame is kept typed, because only a plasma flame is ever
    /// told anything after it is built. A natural flame is handed its size and
    /// its smokiness once and never spoken to again — stock's C_FireSmoke reads
    /// neither the scale nor the ramp, every method that would have is an empty
    /// body — so keeping a typed reference to one would be keeping it to say
    /// nothing.
    /// </summary>
    private SsPlasma? _plasma;
    private float _heat;
    private float _lastHeat;
    /// <summary>
    /// The absorption left to spend. Computed in <see cref="OnSpawn"/> and
    /// spent as the fire burns, and a hot reload does not run <c>OnSpawn</c>
    /// again -- so it is <c>[SaveField]</c>: uncarried, a reload would hand a
    /// partly burned fire back the map's full ignition absorb
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _absorb;

    /// <summary>
    /// The ceiling the heat clamps to, computed ONCE in <see cref="OnSpawn"/>
    /// from the fire size, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>: uncarried it would come back zero and the fire
    /// would read as cold (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _maxHeat;
    private float _fuelLeft;
    private float _nextDamage;
    private float _goOutAt = -1f;
    private bool _enabled = true;
    private bool _diePermanent;

    /// <summary>How hot this fire is now. What a sensor sums and a source feeds.</summary>
    public float HeatLevel => _heat;

    /// <summary>Alight. Heat above zero IS burning; there is no separate flag.</summary>
    public bool IsBurning => _heat > 0f;

    public override void OnSpawn()
    {
        // ALL EIGHT fire particle systems, and this is the first time anything
        // in this build has precached any of them. Stock does it from
        // CFire::Precache, which has not run since this classname became
        // managed: LINK_ENTITY_TO_CLASS( env_fire, CFire ) sits inside
        // `#ifndef SOURCESHARP`, so no CFire is ever constructed. An unprecached
        // particle system does not refuse -- it is simply not in the table the
        // client looks in, and nothing says so.
        //
        // HERE rather than on _firesmoke, because a flame is built when a fire
        // catches and that is long after level init, which is the only moment a
        // precache is allowed. This entity is map-placed and spawns at the right
        // time.
        //
        // All eight rather than the one this fire will use, because a fire grows
        // and crosses bucket boundaries while it burns, and both smoke variants
        // are reachable from one spawnflag.
        foreach (string particleSystem in FireParticleNames.All())
            Precache.PrecacheParticleSystem(particleSystem);

        _maxHeat = FireRules.MaxHeatLevel * FireRules.ScaleFromSize(Vars.FireSize);

        // The ignition point keyvalue is not the absorption: stock takes a
        // twentieth of it. Keyed 100 means 5 points of absorption, which is a
        // little over a second and a half of a small fire's own growth.
        _absorb = FireRules.IgnitionAbsorb(Vars.IgnitionPoint);
        _heat = 0f;

        // An infinite fire has no fuel to spend rather than a lot of it, and a
        // fire that DOES have fuel is one placed by something transient, so it
        // removes itself when it burns out instead of sitting there cold.
        _fuelLeft = Flags.HasFlag(SpawnFlag.Infinite) ? 0f : Vars.Fuel;
        _diePermanent = Flags.HasFlag(SpawnFlag.DiePermanent) || _fuelLeft != 0f;

        if (Flags.HasFlag(SpawnFlag.StartFull))
            _heat = _maxHeat;

        _enabled = !Vars.StartDisabled;

        _fires.Add(this);

        if (_enabled && Flags.HasFlag(SpawnFlag.StartOn))
        {
            _heat = _maxHeat;
            StartFire();
        }
    }

    public override void OnRemoved()
    {
        _fires.Remove(this);
        DestroyEffect();
    }

    /// <summary>
    /// Fires within a radius — the query the whole fire system is built on. A
    /// source feeds them, a sensor sums them, a burning fire spreads to them.
    /// </summary>
    /// <param name="activeOnly">
    /// Only fires that are actually alight. A sensor wants this, since it
    /// measures heat and an unlit fire has none. A source does not: lighting
    /// fires that are out is the entire point of one.
    /// </param>
    /// <param name="fires">The fires to look through: the module's roster.</param>
    public static IEnumerable<EnvFire> InRadius(IEnumerable<EnvFire> fires, Vector origin, float radius,
                                                bool activeOnly = false)
    {
        float rr = radius * radius;

        foreach (var fire in fires)
        {
            if (activeOnly && !fire.IsBurning)
                continue;

            // Through the FIRE's own engine rather than this one's. The walk is
            // over the module's roster of live fires and may be entered from a class
            // that is not a fire at all — env_firesensor asks it — so there is
            // no "this" to read the engine from, and each fire has its own.
            Vector d = fire.Entities.GetOrigin(fire.Handle) - origin;

            if (d.LengthSquared <= rr)
                yield return fire;
        }
    }

    /// <summary>
    /// Light it, at the floor beneath it.
    ///
    /// The drop is what makes an env_fire placeable by eye: a mapper puts one
    /// roughly where the fire should be and it finds the surface itself, which
    /// is also why a fire meant to hang in the air has to say so.
    /// </summary>
    private void StartFire()
    {
        if (Entities.IsValid(_effect))
            return;

        Vector origin = Origin;

        if (!Flags.HasFlag(SpawnFlag.DontDrop))
        {
            var floor = World.TraceLine(origin, origin - new Vector(0f, 0f, DropDistance),
                                         TraceMask.Brushes, Handle);
            origin = floor.End;
            Entities.SetOrigin(Handle, origin);
        }

        // The flame is a MANAGED entity now, where it used to be a native
        // _firesmoke or _plasma reached through three ABI slots. Both classes
        // are this mod's own and both are code-spawned -- neither name appears
        // in any map's entity lump -- so nothing outside this method is affected
        // by which one gets built.
        bool plasma = Vars.FireType == 1;

        _effect = Entities.Create(plasma ? EntityNames.Plasma : EntityNames.FireSmoke);

        if (!Entities.IsValid(_effect))
        {
            Log.Warning($"[SourceSharp] env_fire '{Name}': could not create a flame\n");
            return;
        }

        // Placed BEFORE it is spawned, so the first update it transmits already
        // carries the right position. Deliberately not parented, which is the
        // reasoning the native shim carried and which still holds: a fire on the
        // parallel hierarchy is not a parent, a map-placed fire does not move,
        // and nothing in this build makes one that travels.
        Entities.SetOrigin(_effect, origin);
        Entities.Spawn(_effect);

        if (plasma)
        {
            _plasma = FindByHandle<SsPlasma>(_effect);

            if (_plasma is null)
            {
                Log.Warning($"[SourceSharp] env_fire '{Name}': plasma flame has no managed half\n");
                return;
            }

            // Stock's CFire::SpawnEffect, exactly: the three-argument Scale with
            // the fire's SIZE, and a ramp of no seconds meaning be that size
            // now. That zero is what stock's client then divided by; the ramp is
            // guarded here -- see PlasmaScaleRamp.
            _plasma.ScaleFrom(Vars.FireSize, Vars.FireSize, 0f);
            _plasma.Active = true;
        }
        else
        {
            var smoke = FindByHandle<SsFiresmoke>(_effect);

            if (smoke is null)
            {
                Log.Warning($"[SourceSharp] env_fire '{Name}': flame has no managed half\n");
                return;
            }

            // THE SIZE IN UNITS, which is the fix. This used to be
            // heat / MaxHeatLevel -- a ratio that cannot exceed 1 -- where the
            // client divides by 36 to pick one of four particle systems, so
            // every fire in every map drew as env_fire_tiny whatever its
            // firesize said.
            smoke.SetLook(Vars.FireSize, smoke: !Flags.HasFlag(SpawnFlag.Smokeless));
        }

        _lastHeat = -1f;
        _nextDamage = 0f;
        _goOutAt = -1f;

        OnIgnited.Fire();
        SetNextThink(0f);
    }

    private void DestroyEffect()
    {
        if (!Entities.IsValid(_effect))
            return;

        // The bit before the removal, because a plasma flame really does read
        // it -- C_Plasma::AddEntity returns without drawing when it is clear --
        // and the removal is a tick behind the wire.
        if (_plasma is not null)
            _plasma.Active = false;

        Entities.Remove(_effect);
        _effect = EntityHandle.None;
        _plasma = null;
    }

    public override void OnThink()
    {
        // Going out is its own think, not a branch inside the burn: the flame
        // shrinks over the time it was given and the fire is only actually out
        // when it has finished shrinking.
        if (_goOutAt >= 0f)
        {
            if (Clock.Now >= _goOutAt)
                GoOut();
            else
                SetNextThink(FireRules.ThinkInterval);

            return;
        }

        SetNextThink(FireRules.ThinkInterval);
        Update(FireRules.ThinkInterval);
    }

    /// <summary>
    /// One tick of burning: spend fuel, grow, hurt what is close, spread.
    ///
    /// Stock's order, and it matters: the fire grows BEFORE it works out how far
    /// it reaches, so a fire that has only just caught already has slightly more
    /// than nothing by the time it looks for something to burn.
    /// </summary>
    private void Update(float simTime)
    {
        if (_fuelLeft != 0f)
        {
            _fuelLeft -= simTime;

            if (_fuelLeft <= 0f)
            {
                GoOutInSeconds(1f);
                return;
            }
        }

        float strength = _heat / FireRules.MaxHeatLevel;

        // The flame follows the heat, and only when it changes: the scale eases
        // over half a second, and restarting that every tick would freeze it
        // mid-ease and the flame would never actually grow.
        if (_heat != _lastHeat)
        {
            _lastHeat = _heat;

            // Only a plasma flame listens. Stock sends this to both and the
            // natural one throws it away -- C_FireSmoke::UpdateScale is an empty
            // body and the particle system was chosen once from the size -- so
            // sending it would be putting a float on the wire per fire per heat
            // change for nothing to read.
            _plasma?.ScaleTo(strength, 0.5f);
        }

        // Grow. A fire with no attack time reaches full heat in one tick.
        float added = Vars.AttackTime > 0f ? _maxHeat / Vars.AttackTime : _maxHeat;
        AddHeat(added * simTime * FireRules.GrowthRate, selfHeat: true);

        // The spread reach. Damage uses HALF of it, which is the whole point of
        // the multiplier: a fire spreads to its neighbours from further away
        // than it burns you from.
        float spreadRadius = FireRules.DamageRadius(_heat, _maxHeat, Vars.FireSize);
        float hurtRadius = FireRules.HurtRadius(spreadRadius);

        // Damage is an integer, and a fire that would deal none skips the pass
        // entirely rather than tracing to everything in range to hurt it by
        // nothing. With the base at 1 that only happens to a fire keyed cold.
        int damage = 0;

        if (Clock.Now >= _nextDamage)
        {
            _nextDamage = Clock.Now + FireRules.DamageInterval;
            damage = (int)FireRules.DamagePerInterval(_heat, _maxHeat, Vars.DamageScale);
        }

        Vector origin = Origin;

        if (damage > 0)
            Burn(origin, spreadRadius, hurtRadius, damage);

        Spread(origin, spreadRadius, strength * _heat * simTime);
    }

    /// <summary>Hurts what is standing in it, and only what it can see.</summary>
    private void Burn(Vector origin, float spreadRadius, float hurtRadius, int damage)
    {
        DamageType damageType = Vars.FireType == 1 ? DamageType.Plasma : DamageType.Burn;

        Span<EntityHandle> nearby = stackalloc EntityHandle[128];
        int count = World.EntitiesInSphere(origin, spreadRadius, nearby);

        for (int i = 0; i < count; i++)
        {
            var other = nearby[i];

            if (other == Handle || !Entities.IsValid(other))
                continue;

            var state = Net.EntityGetState(other, out _);

            if ((state & EntityState.TakesDamage) == 0)
                continue;

            Vector where = Entities.GetOrigin(other);

            // A PLAYER is burned at the full spread reach; everything else at
            // half. Stock's asymmetry, and deliberate: standing near a fire
            // should hurt at the distance the fire visibly reaches, while a
            // crate needs to be properly in it.
            if ((state & EntityState.Player) == 0)
            {
                Vector d = where - origin;

                if (d.LengthSquared > hurtRadius * hurtRadius)
                    continue;
            }

            // Not through walls, which is why this traces at all rather than
            // trusting the radius. The TARGET is the ignored entity, not the
            // fire: anything else in the way should stop it.
            var seen = World.TraceLine(origin, where, TraceMask.Brushes, other);

            if (seen.Hit)
                continue;

            Damage.TakeDamage(other, Handle, damage, damageType);
        }
    }

    /// <summary>
    /// Warms the fires around it.
    ///
    /// The heat is DIVIDED between them rather than given to each, which is what
    /// stops a cluster of fires from feeding each other into a runaway: a fire
    /// with eight neighbours warms each an eighth as fast as it would warm one.
    /// </summary>
    private void Spread(Vector origin, float radius, float outputHeat)
    {
        int count = 0;

        foreach (var fire in InRadius(_fires, origin, radius))
        {
            if (!ReferenceEquals(fire, this))
                count++;
        }

        if (count == 0)
            return;

        float share = outputHeat / count;

        foreach (var fire in InRadius(_fires, origin, radius))
        {
            if (!ReferenceEquals(fire, this))
                fire.AddHeat(share, selfHeat: false);
        }
    }

    /// <summary>
    /// Heat arriving, from its own growth or from something else.
    ///
    /// The absorption is the ignition point: a fire that has not caught soaks
    /// heat rather than lighting, which is why a match does not light a bonfire.
    /// </summary>
    public void AddHeat(float heat, bool selfHeat = false)
    {
        if (!_enabled)
            return;

        var (through, absorbLeft) = FireRules.Absorb(heat, _absorb, IsBurning, selfHeat);
        _absorb = absorbLeft;

        bool wasCold = _heat <= 0f;
        _heat += through;

        if (_heat > _maxHeat)
            _heat = _maxHeat;

        if (wasCold && _heat > 0f && !Entities.IsValid(_effect))
            StartFire();
    }

    /// <summary>
    /// Shrinks the flame to nothing over the given time, then goes out.
    ///
    /// Both extinguish inputs land here, and the argument is how long the fire
    /// takes to die rather than how hard it is doused — a map asking for fire to
    /// stop is asking for it to stop, and the number only decides how it looks
    /// on the way.
    /// </summary>
    private void GoOutInSeconds(float seconds)
    {
        _plasma?.ScaleTo(0f, seconds);

        _goOutAt = Clock.Now + seconds;
        SetNextThink(seconds);
    }

    /// <summary>Out. Says so, and leaves if it was never meant to stay.</summary>
    private void GoOut()
    {
        OnExtinguished.Fire();
        DestroyEffect();

        _heat = 0f;
        _lastHeat = 0f;
        _goOutAt = -1f;
        StopThinking();

        // A fire that came with fuel was placed by something passing through,
        // so it goes rather than sitting there cold waiting to be relit.
        if (_diePermanent)
            Entities.Remove(Handle);
    }

    [ScriptedEvent("StartFire")]
    private void OnStartFire()
    {
        if (!_enabled || Entities.IsValid(_effect))
            return;

        _heat = _maxHeat;
        StartFire();
    }

    /// <summary>
    /// Put it out, over the given number of seconds.
    ///
    /// Stock clears the fire's infinite flag here as well, which reads like it
    /// should mean something and does not: the flag was already spent deciding
    /// how much fuel the fire got, back at spawn. The two extinguish inputs are
    /// therefore the same input under two names, and both are kept because maps
    /// use both.
    /// </summary>
    [ScriptedEvent("Extinguish")]
    private void OnExtinguishInput(float seconds) => GoOutInSeconds(seconds);

    /// <summary>The same thing. See <see cref="OnExtinguishInput"/>.</summary>
    [ScriptedEvent("ExtinguishTemporary")]
    private void OnExtinguishTemporary(float seconds) => GoOutInSeconds(seconds);

    [ScriptedEvent("Enable")]
    private void OnEnable() => _enabled = true;

    [ScriptedEvent("Disable")]
    private void OnDisable()
    {
        _enabled = false;

        if (IsBurning)
            GoOut();
    }
}

/// <summary>
/// The map keys <see cref="EnvFire"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvFireVars
{
    /// <summary>How tall the flame is, which also decides how far it reaches.</summary>
    public float FireSize { get; set; } = 64f;

    /// <summary>Seconds to reach full heat. Zero gets there in one tick.</summary>
    [VarName("fireattack")]
    public float AttackTime { get; set; } = 4f;

    /// <summary>How much heat it soaks before catching — its ignition point.</summary>
    public float IgnitionPoint { get; set; } = 32f;

    public float DamageScale { get; set; } = 1f;

    /// <summary>0 natural, 1 plasma. Stock's fireType_e.</summary>
    public int FireType { get; set; }

    /// <summary>Seconds of fuel. Health is the keyvalue stock reads it from.</summary>
    [VarName("health")]
    public float Fuel { get; set; }

    public bool StartDisabled { get; set; }
}
