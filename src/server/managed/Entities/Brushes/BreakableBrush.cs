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
/// CBreakable (func_break.cpp): a brush that breaks. Its material picks the
/// noise it makes when hit and when it breaks, and the propdata.txt chunk
/// group its gibs come from. Its damage is scaled by the func_breakdmg_*
/// convars, or by its propdata's modifiers. With no health, or trigger-only,
/// it takes no damage (glass keeps 1 so bullets still pass). Breaking makes
/// it not solid, throws its gibs, fires OnBreak, explodes if its propdata
/// names a radius, and removes it a tenth later. A plate-glass one breaks
/// when a player runs into it.
///
/// <para>
/// Not here yet: breaking when stood on (SF_BREAK_PRESSURE), the spawned
/// object, physics impact damage, the break_breakable game event and the
/// name cleared before the outputs.
/// </para>
/// </summary>
public abstract class BreakableBrush : ScriptedEntity
{
    protected BreakableBrush(IServerPropData propData, IEngineSound sound, IEnginePrecache precache, ITempEnts temps,
                         IEngineHost host, IEngineDamage damage, IEngineWorldQuery world, IEngineEffects effects,
                         IBlastSweep blast, IServerDecals decals, IEngineEntityPhysics physics,
                         ConVarRegistry? convars = null)
    {
        PropData = propData;
        Sound = sound;
        Precache = precache;
        Temps = temps;
        Host = host;
        Damage = damage;
        World = world;
        Effects = effects;
        Blast = blast;
        Decals = decals;
        Physics = physics;
        ConVars = convars;
    }

    private readonly IServerPropData PropData;
    private readonly IEngineSound Sound;
    private readonly IEnginePrecache Precache;
    private readonly ITempEnts Temps;
    private readonly IEngineHost Host;
    private readonly IEngineDamage Damage;
    private readonly IEngineWorldQuery World;
    private readonly IEngineEffects Effects;
    private readonly IBlastSweep Blast;
    private readonly IServerDecals Decals;
    protected readonly IEngineEntityPhysics Physics;
    private readonly ConVarRegistry? ConVars;

    public readonly Output OnBreak = new();
    public readonly Output OnHealthChanged = new();
    public readonly Output OnTakeDamage = new();

    /// <summary>What a map may set on this entity. See <see cref="BreakableVars"/>.</summary>
    [ScriptVars] public BreakableVars Vars = new();

    /// <summary>Materials (func_break.h).</summary>
    public enum Material
    {
        Glass = 0, Wood, Metal, Flesh, CinderBlock, CeilingTile, Computer, UnbreakableGlass, Rocks, Web, None,
    }

    /// <summary>Explosions (func_break.h): which way the gibs fly.</summary>
    public enum GibDirection { Random = 0, Directed, UsePrecise }

    /// <summary>SF_BREAK_* (func_break.h).</summary>
    [Flags]
    private enum SpawnFlag
    {
        None = 0,
        TriggerOnly = 0x0001,
        Touch = 0x0002,
        Pressure = 0x0004,
    }

    private SpawnFlag Flags => SpawnFlagsAs<SpawnFlag>();

    /// <summary>The BREAK_* temp entity flags (const.h).</summary>
    private const int BreakGlass = 0x01, BreakMetal = 0x02, BreakFlesh = 0x04, BreakWood = 0x08, BreakConcrete = 0x40, BreakSlave = 0x80;

    /// <summary>PerformanceMode_t's PM_NO_GIBS and PM_REDUCED_GIBS (props_shared.h).</summary>
    private const int NoGibs = 1, ReducedGibs = 3;

    [SaveField] private bool _broken;

    /// <summary>m_iMaxHealth, which the health ratio is measured against.</summary>
    [SaveField] private int _startingHealth;

    /// <summary>m_takedamage: DAMAGE_YES, or DAMAGE_NO for no health or trigger-only.</summary>
    [SaveField] private bool _takesDamage;

    [SaveField] private float _explodeRadius;

    [SaveField] private int _explodeMagnitude;

    /// <summary>m_flDmgModBullet, m_flDmgModClub, m_flDmgModExplosive.</summary>
    private PropDamageModifiers _modifiers = new(1f, 1f, 1f);

    /// <summary>m_angle: the yaw it was keyed with, which aims its gibs.</summary>
    [SaveField] private float _gibYaw;

    /// <summary>The way the last blow went, which a directed break throws its gibs back along (stock's g_vecAttackDir).</summary>
    private Vector _lastBlowDirection;

    /// <summary>m_hBreaker.</summary>
    private EntityHandle _breaker;

    private const string RemoveContext = "BreakableRemove";

    public override bool TakesDamage => _takesDamage;

    /// <summary>MOVETYPE_VPHYSICS: a blow pushes it.</summary>
    protected virtual bool PushedByDamage => false;

    /// <summary>A player can crash through a plate-glass one: its touch is every frame for that.</summary>
    public override bool TouchEveryTick => Flags.HasFlag(SpawnFlag.Touch);

    public Material Kind => Vars.Material is >= 0 and <= (int)Material.None ? (Material)Vars.Material : Material.Wood;

    /// <summary>IsBreakable: anything but unbreakable glass.</summary>
    private bool IsBreakable => Kind != Material.UnbreakableGlass;

    /// <summary>CBreakable::CreateVPhysics: a static object, so props land on it.</summary>
    protected virtual void CreatePhysics() => InitStaticPhysics();

    /// <summary>
    /// Whether a blow reaches it to push it: a pushable's breakable flag
    /// (CPushable::OnTakeDamage). The damage itself it refuses by never having
    /// become damageable.
    /// </summary>
    protected virtual bool DamageReaches => true;

    public override void OnSpawn()
    {
        // Initialize damage modifiers. Must be done before the propdata.
        _modifiers = new PropDamageModifiers(ConVars?.Get<FuncBreakDmgBulletConVar>()?.Value ?? 0.5f,
                                             ConVars?.Get<FuncBreakDmgClubConVar>()?.Value ?? 1.5f,
                                             ConVars?.Get<FuncBreakDmgExplosiveConVar>()?.Value ?? 1.25f);
        int health = Vars.Health;
        _explodeMagnitude = Vars.ExplodeMagnitude;

        // ParsePropData: the propdata the map chose, over what the keys gave.
        if (PropDataName(Vars.PropData) is { } name
            && PropData.BaseSectionData(name, _modifiers, out PropDataValues values, out PropDamageModifiers modifiers))
        {
            _modifiers = modifiers;
            health = values.Health ?? health;
            _explodeMagnitude = (int)(values.ExplosiveDamage ?? _explodeMagnitude);
            _explodeRadius = values.ExplosiveRadius ?? _explodeRadius;
        }

        PrecacheSounds();

        if (health == 0 || Flags.HasFlag(SpawnFlag.TriggerOnly))
        {
            // This allows people to shoot at the glass (since it's penetrable).
            if (Kind == Material.Glass)
                health = 1;

            _takesDamage = false;
        }
        else
        {
            _takesDamage = true;
        }

        Damage.SetTakeDamageMode(Handle, _takesDamage ? TakeDamageMode.Yes : TakeDamageMode.No);
        _startingHealth = health > 0 ? health : 1;
        Health = health;

        // "a hack to shoot the gibs in a specific yaw/direction".
        _gibYaw = SpawnAngles.Y;

        SetSolidity(BrushSolidity.Solid);
        CreatePhysics();
        SetVisible(true);
    }

    /// <summary>pFGDPropData: the propdata.txt section a func_breakable's "propdata" key names, or null.</summary>
    public static string? PropDataName(int index) => index switch
    {
        1 => "Wooden.Tiny", 2 => "Wooden.Small", 3 => "Wooden.Medium", 4 => "Wooden.Large", 5 => "Wooden.Huge",
        6 => "Metal.Small", 7 => "Metal.Medium", 8 => "Metal.Large",
        9 => "Cardboard.Small", 10 => "Cardboard.Medium", 11 => "Cardboard.Large",
        12 => "Stone.Small", 13 => "Stone.Medium", 14 => "Stone.Large", 15 => "Stone.Huge",
        16 => "Glass.Small",
        17 => "Plastic.Small", 18 => "Plastic.Medium", 19 => "Plastic.Large",
        20 => "Pottery.Small", 21 => "Pottery.Medium", 22 => "Pottery.Large", 23 => "Pottery.Huge",
        24 => "Glass.Window",
        _ => null,
    };

    /// <summary>CBreakable::Precache's sounds.</summary>
    protected void PrecacheSounds()
    {
        foreach (string name in new[] { "Breakable.MatGlass", "Breakable.MatWood", "Breakable.MatMetal", "Breakable.MatFlesh",
                                        "Breakable.MatConcrete", "Breakable.Computer", "Breakable.Crate", "Breakable.Glass",
                                        "Breakable.Metal", "Breakable.Flesh", "Breakable.Concrete", "Breakable.Ceiling" })
            Precache.PrecacheSound(name);
    }

    /// <summary>CBreakable::Precache's gib group: the material's chunks, or the map's gibmodel.</summary>
    private string? GibGroup
    {
        get
        {
            if (Vars.GibModel.Length > 0)
                return Vars.GibModel;

            return Kind switch
            {
                Material.Glass or Material.UnbreakableGlass => "GlassChunks",
                Material.Metal => "MetalChunks",
                Material.Rocks or Material.CinderBlock => "ConcreteChunks",
                Material.None => string.Empty,
                _ => "WoodChunks",
            };
        }
    }

    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force, HitGroup hitGroup = HitGroup.Generic)
    {
        // CBaseEntity::OnTakeDamage's physics push, for one that is simulated
        // and that the blow reaches (plan_physics_managed A30).
        if (PushedByDamage && !_broken && DamageReaches && (int)damage >= Vars.MinimumDamage)
            VPhysicsTakeDamage(damageType, position, force);

        if (force.LengthSquared > 0f)
            _lastBlowDirection = force.Normalized;

        return OnDamaged(attacker, damage, damageType);
    }

    /// <summary>
    /// CBreakable::OnTakeDamage: nothing when it takes no damage or the blow
    /// is under minhealthdmg; nothing to unbreakable glass; otherwise the
    /// blow scaled by its modifiers, a shard noise unless it burns, and the
    /// health it leaves (UpdateHealth's outputs; the break itself comes with
    /// the kill).
    /// </summary>
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_broken || !_takesDamage || damage < Vars.MinimumDamage || !IsBreakable)
            return 0;

        damage = BreakableDamage.Scale(damage, damageType, _modifiers);
        int taken = (int)damage;

        if ((damageType & DamageType.Burn) == 0)
            DamageSound();

        OnTakeDamage.Fire(attacker);
        OnHealthChanged.Fire(Math.Clamp((Health - taken) / (float)_startingHealth, 0f, 1f));
        _breaker = attacker;
        return taken;
    }

    public override void OnKilled(EntityHandle attacker) => Break(attacker);

    /// <summary>CBreakable::BreakTouch's SF_BREAK_TOUCH: a player running hard enough crashes through, and is cut for a quarter of it.</summary>
    public override void OnTouch(EntityHandle toucher)
    {
        if (_broken || !Flags.HasFlag(SpawnFlag.Touch) || !IsBreakable || !Entities.IsPlayer(toucher))
            return;

        float damage = Entities.GetVelocity(toucher).Length * 0.01f;
        if (damage < Health)
            return;

        _takesDamage = true;
        Damage.TakeDamage(Handle, toucher, damage, DamageType.Crush);
        Damage.TakeDamage(toucher, toucher, damage / 4f, DamageType.Slash);
    }

    [ScriptedEvent("Break")] private void OnBreakInput(InputData input) => Break(input.Activator);

    [ScriptedEvent("SetHealth")] private void OnSetHealthInput(InputData input) => UpdateHealth((int)input.Number, input.Activator);

    [ScriptedEvent("AddHealth")] private void OnAddHealthInput(InputData input) => UpdateHealth(Health + (int)input.Number, input.Activator);

    [ScriptedEvent("RemoveHealth")] private void OnRemoveHealthInput(InputData input) => UpdateHealth(Health - (int)input.Number, input.Activator);

    /// <summary>InputSetMass: its physics object's mass, when it has one.</summary>
    [ScriptedEvent]
    private void OnSetMass(float mass)
    {
        if (Physics.PhysicsObject(Handle) == 0)
        {
            Log.Warning($"Tried to call SetMass() on {Name} but it has no physics.\n");
            return;
        }

        Physics.SetMass(Handle, mass);
    }

    /// <summary>CBreakable::UpdateHealth: the health, its ratio out, and the break at none.</summary>
    private void UpdateHealth(int health, EntityHandle activator)
    {
        if (_broken || health == Health)
            return;

        Health = health;
        OnHealthChanged.Fire(Math.Clamp(health / (float)_startingHealth, 0f, 1f).ToString(System.Globalization.CultureInfo.InvariantCulture),
                             activator);

        if (health <= 0)
        {
            Break(activator);
            return;
        }

        _takesDamage = !Flags.HasFlag(SpawnFlag.TriggerOnly);
        Damage.SetTakeDamageMode(Handle, _takesDamage ? TakeDamageMode.Yes : TakeDamageMode.No);
    }

    /// <summary>CBreakable::DamageSound: the material's shard noise, at a random volume, now and then off pitch.</summary>
    private void DamageSound()
    {
        int pitch = Host.RandomInt(0, 2) != 0 ? 100 : 95 + Host.RandomInt(0, 34);
        float volume = Host.RandomFloat(0.75f, 1f);

        Material material = Kind;
        if (material == Material.Computer && Host.RandomInt(0, 1) != 0)
            material = Material.Metal;

        string? sound = material switch
        {
            Material.Glass or Material.UnbreakableGlass => "Breakable.MatGlass",
            Material.Wood => "Breakable.MatWood",
            Material.Metal => "Breakable.MatMetal",
            Material.Rocks or Material.CinderBlock => "Breakable.MatConcrete",
            Material.Computer => "Breakable.Computer",
            _ => null,
        };

        if (sound is not null)
            Sound.EntityEmitSoundAs(Handle, sound, volume, pitch);
    }

    /// <summary>CBreakable::Break: anything breakable, by whoever broke it.</summary>
    private void Break(EntityHandle breaker)
    {
        if (_broken || !IsBreakable)
            return;

        _breaker = breaker;
        Die();
    }

    /// <summary>
    /// CBreakable::Die: the material's break noise, louder the deader it is;
    /// its gibs, one per 432 square units of its box's faces up to
    /// func_break_max_pieces, fewer by its PerformanceMode; not solid;
    /// OnBreak; gone a tenth later; and its explosion.
    /// </summary>
    private void Die()
    {
        _broken = true;

        int pitch = 95 + Host.RandomInt(0, 29);
        if (pitch > 97 && pitch < 103)
            pitch = 100;

        float volume = Math.Min(1f, Host.RandomFloat(0.85f, 1f) + Math.Abs(Health) / 100f);

        (string? sound, int flag) = Kind switch
        {
            Material.Glass => ("Breakable.Glass", BreakGlass),
            Material.Wood => ("Breakable.Crate", BreakWood),
            Material.Computer => ("Breakable.Computer", BreakMetal),
            Material.Metal => ("Breakable.Metal", BreakMetal),
            Material.Flesh or Material.Web => ("Breakable.Flesh", BreakFlesh),
            Material.Rocks or Material.CinderBlock => ("Breakable.Concrete", BreakConcrete),
            Material.CeilingTile => ("Breakable.Ceiling", 0),
            _ => ((string?)null, 0),
        };

        if (sound is not null)
            Sound.EntityEmitSoundAs(Handle, sound, volume, pitch);

        Vector velocity = (GibDirection)Vars.Explosion switch
        {
            GibDirection.Directed => _lastBlowDirection * -200f,
            GibDirection.UsePrecise => Angles.Vectors(Vars.GibDir).Forward * 200f,
            _ => Vector.Zero,
        };

        Vector spot = Entities.GetWorldSpaceCenter(Handle);
        Vector size = Entities.GetBoundsSize(Handle);
        var angles = new QAngle(0f, _gibYaw, 0f);
        int count = Gibs(size);

        if (GibGroup is { Length: > 0 } group)
        {
            for (int i = 0; i < count; i++)
            {
                string? model = PropData.RandomChunkModel(group, -1);
                int index = model is null ? 0 : Precache.PrecacheModel(model);

                // All objects except the first one in this run are marked as slaves.
                Temps.BreakModel(TeFilters.Pvs(spot), 0f, spot, angles, size, velocity, index, 100, 1, 2.5f,
                                 flag | (i != 0 ? BreakSlave : 0));
            }
        }

        Physics.SetNotSolid(Handle, true);
        OnBreak.Fire(_breaker);
        SetContextThink(RemoveContext, 0.1f);

        if (_explodeRadius > 0f)
            Explosions.Fire(World, Effects, Damage, Precache, Host, Blast, Decals,
                            spot, _explodeMagnitude, _explodeRadius, Handle, ignored: Handle, inflictor: Handle);
    }

    /// <summary>The gib count: the box's face areas over 432, capped, then trimmed by PerformanceMode.</summary>
    private int Gibs(Vector size)
    {
        int count = (int)((size.X * size.Y + size.Y * size.Z + size.Z * size.X) / (3f * 12f * 12f));
        count = Math.Min(count, ConVars?.Get<FuncBreakMaxPiecesConVar>()?.Value ?? 15);

        if (!(ConVars?.Get<BreakableDisableGibLimitConVar>()?.Value ?? false) && count != 0)
        {
            if (Vars.PerformanceMode == NoGibs)
                count = 0;
            else if (Vars.PerformanceMode == ReducedGibs)
                count = Math.Max((int)(count * (ConVars?.Get<FuncBreakReductionFactorConVar>()?.Value ?? 0.5f)), 1);
        }

        return count;
    }

    public override void OnThink(string context)
    {
        if (context == RemoveContext)
            Remove();
    }
}

/// <summary>
/// The map keys every <see cref="BreakableBrush"/> declares.
/// </summary>
[ScriptVars]
public partial class BreakableVars
{
    public int Health { get; set; } = 1;

    /// <summary>Damage below this is ignored entirely. Stops a crate dying to gunfire it should shrug off.</summary>
    [VarName("minhealthdmg")]
    public int MinimumDamage { get; set; }

    /// <summary>m_Material: glass, wood, metal and the rest, glass when unset.</summary>
    [VarName("material")]
    public int Material { get; set; }

    /// <summary>A chunk group of propdata.txt's breakable models in place of the material's.</summary>
    [VarName("gibmodel")]
    public string GibModel { get; set; } = string.Empty;

    /// <summary>An index into pFGDPropData: the propdata.txt section it reads.</summary>
    [VarName("propdata")]
    public int PropData { get; set; }

    /// <summary>m_Explosion: which way its gibs fly.</summary>
    [VarName("explosion")]
    public int Explosion { get; set; }

    /// <summary>m_GibDir, for a precise gib direction.</summary>
    [VarName("gibdir")]
    public QAngle GibDir { get; set; }

    /// <summary>m_ExplosionMagnitude: its explosion's damage, which its propdata may replace.</summary>
    [VarName("explodemagnitude")]
    public int ExplodeMagnitude { get; set; }

    /// <summary>m_PerformanceMode: whether its gibs are trimmed.</summary>
    public int PerformanceMode { get; set; }
}
