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
/// Hurts whoever stands in it. Damage repeats while they remain, which is what
/// makes a pit or a fire dangerous rather than merely once-painful.
/// </summary>
[ScriptedEntity(EntityNames.TriggerHurt, ScriptedEntityKind.Brush)]
public sealed class TriggerHurt : TriggerVolume
{
    public TriggerHurt(IGameClock clock, IEngineHost host, IEngineDamage damage,
                       PvsCheckClient pvs)
        : base(clock)
    {
        _pvs = pvs;
        _damage = damage;
        _host = host;
    }

    private readonly IEngineHost _host;
    private readonly IEngineDamage _damage;
    private readonly PvsCheckClient _pvs;

    public readonly Output OnHurt = new();
    public readonly Output OnHurtPlayer = new();

    /// <summary>What a map may set on this entity. See <see cref="TriggerHurtVars"/>.</summary>
    [ScriptVars] public TriggerHurtVars TriggerHurtVars = new();

    /// <summary>Stock's HurtThink interval: half a second's damage, every half second.</summary>
    public const float Interval = 0.5f;

    /// <summary>RadiationThink's interval.</summary>
    public const float RadiationInterval = 0.25f;

    /// <summary>TRIGGER_HURT_FORGIVE_TIME: how long a doubling trigger must hurt nobody to reset.</summary>
    public const float ForgiveTime = 3f;

    /// <summary>A player's cap for negative damage (TakeHealth's max health).</summary>
    public const int PlayerMaxHealth = 100;

    /// <summary>
    /// The damage the map asked for, snapshotted ONCE in <see cref="OnSpawn"/>
    /// so a doubling trigger can fall back to it, which a hot reload does not
    /// run again -- so it is <c>[SaveField]</c>. Uncarried, a rebuilt trigger
    /// whose damage had been raised would reset it to zero on the forgive
    /// timer (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private float _originalDamage;
    private float _lastDamageTime;
    private float _damageResetTime;
    private bool _thinking;
    private readonly List<EntityHandle> _hurt = new();

    private DamageType Type => (DamageType)TriggerHurtVars.DamageType;
    private bool Radiation => (Type & DamageType.Radiation) != 0;

    public override void OnSpawn()
    {
        base.OnSpawn();
        _originalDamage = TriggerHurtVars.Damage;

        // A radiation trigger thinks from spawn: it pings geiger counters
        // whether or not anything is inside.
        if (Radiation)
        {
            _thinking = true;
            SetNextThink(_host.RandomFloat(0f, 0.5f));
        }
    }

    [ScriptedEvent("SetDamage")]
    private void OnSetDamage(float damage) => TriggerHurtVars.Damage = damage;

    // Stock's Touch: start the hurt think if nothing is thinking.
    protected override void Fire(EntityHandle toucher)
    {
        if (_thinking)
            return;

        _thinking = true;
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (Radiation)
        {
            RadiationThink();
            return;
        }

        // HurtThink: think again only if someone was hurt.
        if (HurtAllTouchers(Interval) <= 0)
            _thinking = false;
        else
            SetNextThink(Interval);
    }

    private void RadiationThink()
    {
        // UTIL_FindClientInPVS: the one check client, if the box is in its PVS.
        var (mins, maxs) = WorldBox();
        if (_pvs.FindClientInPvs(mins, maxs, Clock.Now) is { } player)
            player.NotifyNearbyRadiationSource(DistanceToBox(mins, maxs, Entities.GetWorldSpaceCenter(player.Handle)) * 3f);

        float dt = Clock.Now - _lastDamageTime;
        if (dt >= 0.5f)
            HurtAllTouchers(dt);

        SetNextThink(RadiationInterval);
    }

    private (Vector Mins, Vector Maxs) WorldBox()
    {
        var (mins, maxs) = Entities.GetBounds(Handle);
        return (Origin + mins, Origin + maxs);
    }

    /// <summary>CalcDistanceFromPoint: zero inside the box, else to its nearest point.</summary>
    public static float DistanceToBox(Vector mins, Vector maxs, Vector point)
    {
        float dx = MathF.Max(0f, MathF.Max(mins.X - point.X, point.X - maxs.X));
        float dy = MathF.Max(0f, MathF.Max(mins.Y - point.Y, point.Y - maxs.Y));
        float dz = MathF.Max(0f, MathF.Max(mins.Z - point.Z, point.Z - maxs.Z));
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Stock's HurtAllTouchers: damage-per-second times <paramref name="dt"/>
    /// to everything inside, then the double-forgiveness model. The count hurt.
    /// </summary>
    private int HurtAllTouchers(float dt)
    {
        float damage = TriggerHurtVars.Damage * dt;
        _lastDamageTime = Clock.Now;
        _hurt.Clear();

        int hurt = 0;
        if (!Disabled)
        {
            foreach (var toucher in Inside.ToArray())
            {
                if (Entities.IsValid(toucher) && HurtEntity(toucher, damage))
                    hurt++;
            }
        }

        if (TriggerHurtVars.DamageModel == DoubleForgiveness)
        {
            if (hurt == 0)
            {
                if (Clock.Now > _damageResetTime)
                    TriggerHurtVars.Damage = _originalDamage;
            }
            else
            {
                TriggerHurtVars.Damage = MathF.Min(TriggerHurtVars.Damage * 2f, TriggerHurtVars.DamageCap);
                _damageResetTime = Clock.Now + ForgiveTime;
            }
        }

        return hurt;
    }

    /// <summary>DAMAGEMODEL_DOUBLE_FORGIVENESS.</summary>
    public const int DoubleForgiveness = 1;

    /// <summary>Stock's HurtEntity: heal for negative damage, else the damage path.</summary>
    private bool HurtEntity(EntityHandle other, float damage)
    {
        bool player = Entities.IsPlayer(other);

        if (damage < 0f)
        {
            // TakeHealth: a player up to a hundred; something with no health takes none.
            int health = Entities.GetHealth(other);
            if (player && health > 0 && health < PlayerMaxHealth)
                Entities.SetHealth(other, Math.Min(PlayerMaxHealth, health + (int)-damage));
        }
        else if (TriggerHurtVars.NoDmgForce)
        {
            _damage.Apply(other, Handle, damage, Type);
        }
        else
        {
            // GuessDamageForce away from the trigger's centre, at the victim.
            Vector at = Entities.GetWorldSpaceCenter(other);
            Vector away = (at - Entities.GetWorldSpaceCenter(Handle)).Normalized;
            _damage.TakeDamage(other, Handle, damage, Type, away, at);
        }

        if (player)
            OnHurtPlayer.Fire();
        else
            OnHurt.Fire();

        _hurt.Add(other);
        return true;
    }

    // Stock's EndTouch: one who leaves never having been hurt takes half now.
    public override void OnEndTouch(EntityHandle toucher)
    {
        if (Inside.Contains(toucher) && !_hurt.Contains(toucher) && !Disabled)
            HurtEntity(toucher, TriggerHurtVars.Damage * 0.5f);

        base.OnEndTouch(toucher);
    }
}

/// <summary>
/// The map keys <see cref="TriggerHurt"/> declares.
/// </summary>
[ScriptVars]
public partial class TriggerHurtVars
{
    /// <summary>Damage per second (stock's "damage"); negative heals.</summary>
    public float Damage { get; set; } = 20f;

    /// <summary>The double-forgiveness model's ceiling ("damagecap"), 20 as stock's constructor.</summary>
    public float DamageCap { get; set; } = 20f;

    /// <summary>The DMG_ bits dealt ("damagetype").</summary>
    public int DamageType { get; set; }

    /// <summary>0 normal, 1 double forgiveness ("damagemodel").</summary>
    public int DamageModel { get; set; }

    /// <summary>No physics force ("nodmgforce").</summary>
    public bool NoDmgForce { get; set; }
}
