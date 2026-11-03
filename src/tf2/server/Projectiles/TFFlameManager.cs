//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Conditions;

namespace TF2.Server.Projectiles;

/// <summary>
/// The flame manager's constants: <c>CTFPointManager</c>/<c>CTFFlameManager</c>'s
/// own (<c>tf_point_manager.h</c>, <c>tf_flame.cpp</c>) and the flame
/// attributes at their item-schema defaults — <c>weapon_newflame</c> carries
/// <c>flame_speed</c> 2450, <c>flame_lifetime</c> 0.6,
/// <c>flame_random_life_time_offset</c> 0.1, <c>flame_drag</c> 8.5,
/// <c>flame_up_speed</c> 50, <c>flame_gravity</c> 0 and
/// <c>flame_spread_degree</c> 2.8; every other attribute
/// (<c>HookAttributes</c>, <c>tf_flame.cpp:461-497</c>) keeps its constructor
/// value, so the reflection arms reduce to the slide and the size multipliers
/// to 1.
/// </summary>
public static class TFFlameManagerDefs
{
    /// <summary><c>LINK_ENTITY_TO_CLASS( tf_flame_manager, CTFFlameManager )</c>.</summary>
    public const string ClassName = "tf_flame_manager";

    /// <summary><c>MAX_POINT_MANAGER_POINTS</c> (tf_point_manager.h).</summary>
    public const int MaxPoints = 30;

    public const float FlameSpeed = 2450f;
    public const float FlameLifeTime = 0.6f;
    public const float RandomLifeTimeOffset = 0.1f;
    public const float FlameDrag = 8.5f;
    public const float FlameUp = 50f;
    public const float FlameGravity = 0f;
    public const float SpreadDegree = 2.8f;

    /// <summary><c>tf_flamethrower_boxsize</c>'s declared value (tf_weapon_flamethrower.cpp:60), read live in <c>UpdatePoint</c> × the size multipliers, which default to 1.</summary>
    public const float BoxSize = 12f;

    /// <summary><c>tf_flamethrower_burn_frequency</c> (tf_weapon_flamethrower.cpp:53).</summary>
    public const float BurnFrequency = 0.075f;

    /// <summary><c>tf_flame_min_damage_scale_time</c> and its cap (tf_flame.cpp:19-20).</summary>
    public const float MinDamageScaleTime = 0.5f;
    public const float MinDamageScaleTimeCap = 0.5f;

    /// <summary>The burn index's constants (tf_flame.cpp:24-28).</summary>
    public const float BurnIndexDrainRate = 1.25f;
    public const float BurnIndexPerCollide = 1f;
    public const float BurnIndexRemapX = 10f;
    public const float BurnIndexRemapY = 50f;
    public const float BurnIndexDamageScaleMin = 0.5f;
}

/// <summary>
/// The live flamethrower flame (<c>CTFFlameManager</c> over
/// <c>CTFPointManager</c>, <c>tf_flame.cpp</c> + <c>tf_point_manager.cpp</c>):
/// one logical entity per burst-holding weapon, thinking every tick, whose
/// points each carry their own position, velocity and life. The weapon's
/// tick asks <see cref="UpdateDamage"/> and <see cref="AddPoint"/> of it
/// (<c>tf_weapon_flamethrower.cpp:879-897</c>); the think ages, moves and
/// collides the points (<c>CTFPointManager::Update</c>) and burns what they
/// touch (<c>CTFFlameManager::OnCollide</c>), by the time curve through the
/// burn index, at most once per victim per burn frequency. When its last
/// point dies the manager removes itself (<c>tf_point_manager.cpp:290-304</c>).
///
/// <para>
/// Narrowed to the seam, with the divergences named: stock's collision is the
/// engine trigger's per-entity <c>Touch</c> over a swept box per point
/// (<c>tf_point_manager.cpp:105-132</c>) — the port asks the one
/// <see cref="IEngineWorldQuery.TraceBox"/> per point per tick, so a tick
/// reaches the first thing the swept box stops on (one burn per tick is
/// inside stock's once-per-victim-per-frequency gate anyway). The trigger
/// hull re-centring (<c>:290-304</c>) has no trigger to re-centre. The
/// reflection stomp, the reflection life and damage reduction and the
/// distance damage mode are all behind attributes that default off, so the
/// wall hit slides (<c>OnPointHitWall</c>'s tangent) and the damage scale is
/// the time curve. The sniper's arrow-lighting and the from-behind crit read
/// surfaces the seam does not carry (another player's active weapon, a
/// victim's abs angles), so a teammate burns nothing and the crit flag is
/// the shot's own.
/// </para>
/// </summary>
[ScriptedEntity(TFFlameManagerDefs.ClassName, ScriptedEntityKind.Logical)]
public sealed class TFFlameManager : ScriptedEntity
{
    private readonly IEngineWorldQuery World;
    private readonly IEngineDamage Damage;
    private readonly IEnginePlayers Players;
    private readonly IEngineHost Host;
    private readonly ITFPlayerConditions Conditions;
    private readonly TFDamageScopes Scopes;
    private readonly DamageHitGroups HitGroups;
    private readonly IEngineConsole Console;
    private readonly SeededUniformStream Stream = new();

    public TFFlameManager(IEngineWorldQuery world, IEngineDamage damage, IEnginePlayers players,
                          IEngineHost host, ITFPlayerConditions conditions,
                          TFDamageScopes scopes, DamageHitGroups hitGroups, IEngineConsole console)
    {
        World = world;
        Damage = damage;
        Players = players;
        Host = host;
        Conditions = conditions;
        Scopes = scopes;
        HitGroups = hitGroups;
        Console = console;
    }

    private sealed class FlamePoint
    {
        public Vector Position, PrevPosition, InitialPosition;
        public Vector Velocity, AttackerVelocity;
        public float SpawnTime, LifeTime;
        public int HitWall;
    }

    /// <summary>A named convar's float, or the stock default when the engine has no such value.</summary>
    private float ConVarFloat(string name, float stock)
        => float.TryParse(Console.FindConVarValue(name), NumberStyles.Float,
                          CultureInfo.InvariantCulture, out float value) ? value : stock;

    /// <summary>A point as a fact reads it: <c>flame_point_t</c>'s live fields.</summary>
    public readonly record struct PointView(Vector Position, Vector Velocity, float SpawnTime,
                                            float LifeTime, int HitWall);

    private sealed class Burnt
    {
        public float LastBurn;
        public float Heat;
    }

    private readonly List<FlamePoint> _points = [];
    private int _nextPointIndex;
    private int _spawnCounter;
    private float _lastUpdateTime;

    private EntityHandle _attacker = EntityHandle.None;
    private float _baseDamage;
    private bool _critical;
    private TFWeaponId _weapon;
    private string _weaponName = "";
    private float _burnFrequency = TFFlameManagerDefs.BurnFrequency;
    private bool _attackerIsPyro;

    private readonly Dictionary<EntityHandle, Burnt> _burnt = [];

    /// <summary>Live points, for the effects' log and the facts.</summary>
    public int PointCount => _points.Count;

    public IEnumerable<PointView> Points => _points.Select(p =>
        new PointView(p.Position, p.Velocity, p.SpawnTime, p.LifeTime, p.HitWall));

    /// <summary>
    /// The weapon's deal each firing interval (<c>UpdateDamage</c>,
    /// <c>tf_flame.cpp:459-506</c>): whose flame, what damage, how often a
    /// victim may burn and whether the attacker is the pyro whose victims
    /// take the healing debuff. Stock reads the pyro's class off
    /// <c>m_hAttacker</c> per collide; the seam reads classes by index, so
    /// the weapon's side hands the answer in.
    /// </summary>
    public void UpdateDamage(TFWeaponScript script, in ShotDamage shot, float burnFrequency, bool attackerIsPyro)
    {
        _weapon = script.Id;
        _weaponName = script.Name;
        _baseDamage = shot.Damage;
        _critical = shot.Critical;
        _burnFrequency = burnFrequency;
        _attackerIsPyro = attackerIsPyro;
    }

    /// <summary>
    /// One more flame (<c>AddPoint</c> → <c>InitializePoint</c>,
    /// <c>tf_point_manager.cpp:70-101</c>): born at the muzzle,
    /// <see cref="TFFlameManagerDefs.FlameSpeed"/> along the aim through the
    /// spread, living the life through its jitter, the shooter's velocity
    /// riding along (<c>tf_flame.cpp:250-260</c>). The seed names the slot as
    /// stock's spawn tick + random seed + slot does; the networked tick
    /// itself is the client's catch-up, which the seam does not carry.
    /// </summary>
    public bool AddPoint(Vector muzzle, Vector aim, Vector attackerVelocity)
    {
        if (_points.Count >= TFFlameManagerDefs.MaxPoints)
            return false;

        int index = _nextPointIndex;
        _nextPointIndex = (_nextPointIndex + 1) % TFFlameManagerDefs.MaxPoints;
        Stream.SetSeed(_spawnCounter++ * TFFlameManagerDefs.MaxPoints + index);

        Vector direction = aim;
        if (TFFlameManagerDefs.SpreadDegree > 0f)
        {
            (Vector right, Vector up) = Angles.RightUp(aim);
            direction = ShotSpread.Apply(new Angles.Basis(aim, right, up),
                                         ShotSpread.SpreadOf(TFFlameManagerDefs.SpreadDegree),
                                         index, Stream);
        }

        _points.Add(new FlamePoint
        {
            Position = muzzle,
            PrevPosition = muzzle,
            InitialPosition = muzzle,
            Velocity = direction * TFFlameManagerDefs.FlameSpeed,
            AttackerVelocity = attackerVelocity,
            SpawnTime = Host.CurrentTime,
            LifeTime = TFFlameManagerDefs.FlameLifeTime
                     + Stream.RandomFloat(-TFFlameManagerDefs.RandomLifeTimeOffset,
                                          TFFlameManagerDefs.RandomLifeTimeOffset),
        });
        return true;
    }

    public override void OnSpawn()
    {
        _lastUpdateTime = Host.CurrentTime;
        SetNextThink(0);
    }

    /// <summary>
    /// <c>CTFPointManager::Update</c> (<c>tf_point_manager.cpp:233-306</c>),
    /// back to front: a point past its life or sitting in water dies, the
    /// rest move and collide; with none left the manager removes itself.
    /// <c>PostEntityThink</c> (<c>tf_flame.cpp:345-364</c>) rides on the end:
    /// burnt entries past the frequency are dropped and the heat drains.
    /// </summary>
    public override void OnThink()
    {
        float now = Host.CurrentTime;
        float dt = now - _lastUpdateTime;
        if (dt <= 0f)
        {
            SetNextThink(0);
            return;
        }
        _lastUpdateTime = now;

        bool hadPoints = _points.Count > 0;

        for (int i = _points.Count - 1; i >= 0; i--)
        {
            FlamePoint point = _points[i];

            bool remove = now > point.SpawnTime + point.LifeTime
                       || (World.PointContents(point.Position) & (Contents.Water | Contents.Slime)) != 0;

            if (!remove && !UpdatePoint(point, dt, now))
                remove = true;

            if (remove)
                _points.RemoveAt(i);
        }

        if (hadPoints && _points.Count == 0)
        {
            Remove();
            return;
        }

        SetNextThink(0);

        List<EntityHandle>? stale = null;
        foreach ((EntityHandle victim, Burnt burnt) in _burnt)
        {
            if (now - burnt.LastBurn > _burnFrequency)
                (stale ??= []).Add(victim);
        }
        if (stale != null)
        {
            foreach (EntityHandle victim in stale)
                _burnt.Remove(victim);
        }

        // The heat drains by the constant per frame, as stock's does — its own
        // TODO asks for the time since the last think it does not carry.
        foreach (Burnt burnt in _burnt.Values)
            burnt.Heat = MathF.Max(burnt.Heat - TFFlameManagerDefs.BurnIndexDrainRate, 0f);
    }

    /// <summary>
    /// One point's movement (<c>CTFPointManager::UpdatePoint</c>,
    /// <c>tf_point_manager.cpp:309-395</c>) with the flame's wall response
    /// (<c>CTFFlameManager::OnPointHitWall</c>, <c>tf_flame.cpp:1070-1105</c>).
    /// Returns false when the point dies: started inside a wall.
    /// </summary>
    private bool UpdatePoint(FlamePoint point, float dt, float now)
    {
        // The swept collision box's half extent: the convar stock declares for
        // it (CTFFlameManager::GetRadius, tf_flame.cpp:1209), the constant its
        // declared value.
        float radius = ConVarFloat("tf_flamethrower_boxsize", TFFlameManagerDefs.BoxSize);

        Vector gravity = new Vector(0f, 0f, TFFlameManagerDefs.FlameGravity) * dt;
        Vector newVelocity = point.Velocity + gravity + AdditionalVelocity(point);
        Vector newPos = point.Position + dt * newVelocity;

        SurfaceTraceResult tr = World.TraceBox(point.Position, newPos,
                                               new Vector(-radius, -radius, -radius),
                                               new Vector(radius, radius, radius),
                                               TraceMask.Solid, Handle);

        // Stock removes a point that STARTED inside a wall
        // (tf_point_manager.cpp:331-334); the seam's surface trace carries no
        // startsolid, so that death is not expressible here.

        if (tr.Fraction < 1f)
        {
            point.HitWall++;

            // The special pumpkin-bomb collide is stock's only entity read
            // here (:341-355); the swept box reached a burn target, so burn it.
            if (tr.Entity != EntityHandle.None && IsValidBurnTarget(tr.Entity))
                OnCollide(tr.Entity, point, now);

            // The slide along the wall: the reflection stomp is behind the
            // default-off shouldReflect, the added life behind a 0 default.
            Vector normal = tr.Normal;
            Vector tangent = newVelocity - normal * normal.Dot(newVelocity);
            point.AttackerVelocity = default;
            newVelocity = tangent;
            newPos = tr.End + normal * radius + (1f - tr.Fraction) * dt * tangent;
        }
        else
        {
            // Nothing hit: the drag works the original velocity, as stock's
            // else branch sets it back (:362-367).
            newVelocity = point.Velocity;
        }

        float speed = newVelocity.Length;
        Vector direction = speed > 0f ? newVelocity * (1f / speed) : default;
        speed = Math.Clamp(speed - dt * TFFlameManagerDefs.FlameDrag * speed, 0f, speed);
        point.Velocity = direction * speed + gravity;

        point.PrevPosition = point.Position;
        point.Position = newPos;
        return true;
    }

    /// <summary>
    /// <c>GetAdditionalVelocity</c> (<c>tf_flame.cpp:1191-1205</c>): the float
    /// plus the attacker's velocity projected on the point's own direction.
    /// </summary>
    private Vector AdditionalVelocity(FlamePoint point)
    {
        Vector baseDir = point.Velocity.Normalized;
        return new Vector(0f, 0f, TFFlameManagerDefs.FlameUp) + baseDir * point.AttackerVelocity.Dot(baseDir);
    }

    /// <summary>
    /// <c>IsValidBurnTarget</c> (<c>tf_flame.cpp:618-653</c>), narrowed to
    /// what the seam reads: the attacker themself is no target, a living
    /// player is. Stock's bots, enemy objects and classname breakables ask
    /// surfaces the seam does not carry.
    /// </summary>
    private bool IsValidBurnTarget(EntityHandle target)
    {
        if (!Entities.IsValid(_attacker) || target == _attacker)
            return false;

        return Entities.IsPlayer(target) && Players.IsAlive(target);
    }

    /// <summary>
    /// <c>OnCollide</c> (<c>tf_flame.cpp:679-818</c>): the heat index rises on
    /// every touch, damage or not; the burn-frequency gate; a teammate
    /// nothing (stock lights a sniper's arrow, which the seam cannot reach);
    /// the damage by the scale, through the flamethrower's own bits so the
    /// afterburn lights; the pyro's healing debuff. The burn-list write
    /// closes it either way (:809-817), but not behind the frequency gate's
    /// early return (:702).
    /// </summary>
    private void OnCollide(EntityHandle victim, FlamePoint point, float now)
    {
        bool known = _burnt.TryGetValue(victim, out Burnt? burnt);
        if (known)
        {
            burnt!.Heat += TFDamageModifyRules.RemapValClamped(
                now - point.SpawnTime, 0f, 0.02f,
                2f * TFFlameManagerDefs.BurnIndexPerCollide, TFFlameManagerDefs.BurnIndexPerCollide);
        }

        if (!BCanBurnEntityThisFrame(victim, now))
            return;

        bool teammate = Entities.IsPlayer(victim)
                     && Entities.IsValid(_attacker)
                     && Players.GetTeam(victim) == Players.GetTeam(_attacker);

        if (!teammate)
        {
            float scale = GetFlameDamageScale(point, victim);
            float damage = MathF.Max(scale * _baseDamage, 1f);

            // The flamethrower's own bits carry DMG_IGNITE
            // (g_aWeaponDamageTypes), so the damage layer lights the victim;
            // the kill's custom kind is TF_DMG_CUSTOM_BURNING.
            using (HitGroups.Push(HitGroup.Generic, _critical))
            using (Scopes.Push(TFDamageCustom.Burning, _weapon, _weaponName))
                Damage.TakeDamage(victim, _attacker, damage, TFWeaponDamageTypes.Of(_weapon));

            if (_attackerIsPyro && Entities.IsPlayer(victim))
                Conditions.Add(victim, TFCond.HealingDebuff, 2f, _attacker);
        }

        if (known)
            burnt!.LastBurn = now;
        else
            _burnt[victim] = new Burnt { LastBurn = now, Heat = TFFlameManagerDefs.BurnIndexPerCollide };
    }

    /// <summary>
    /// <c>BCanBurnEntityThisFrame</c> (<c>tf_flame.cpp:555-563</c>): an
    /// unknown victim is free; a known one waited out the frequency.
    /// </summary>
    private bool BCanBurnEntityThisFrame(EntityHandle victim, float now)
        => !_burnt.TryGetValue(victim, out Burnt? burnt)
        || now - burnt.LastBurn >= _burnFrequency;

    /// <summary>
    /// <c>GetFlameDamageScale</c> (<c>tf_flame.cpp:508-553</c>), the shipped
    /// time curve — the distance mode sits behind a default-off cheat convar
    /// — times the burn index modifier, which stock reaches only through a
    /// player victim, and the per-reflection reduction, which defaults to 1.
    /// </summary>
    private float GetFlameDamageScale(FlamePoint point, EntityHandle victim)
    {
        float timeAlive = Host.CurrentTime - point.SpawnTime;
        float lifeMax = point.LifeTime * TFFlameManagerDefs.MinDamageScaleTimeCap;
        float scale = TFDamageModifyRules.RemapValClamped(
            timeAlive, 0f, lifeMax, 1f, TFFlameManagerDefs.MinDamageScaleTime);

        if (Entities.IsPlayer(victim) && _burnt.TryGetValue(victim, out Burnt? burnt))
        {
            scale *= TFDamageModifyRules.RemapValClamped(
                burnt.Heat, TFFlameManagerDefs.BurnIndexRemapX, TFFlameManagerDefs.BurnIndexRemapY,
                TFFlameManagerDefs.BurnIndexDamageScaleMin, 1f);
        }

        return scale;
    }
}
