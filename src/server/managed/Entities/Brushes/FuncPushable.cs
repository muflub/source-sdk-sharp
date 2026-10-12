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
/// A crate a walking player shoves out of the way.
///
/// func_breakable's keys with a physics object underneath, and the mass is the
/// whole entity: stock pins it at thirty however big the mapper drew the brush,
/// because what makes this a pushable rather than a wall is that a player wins
/// against it. So the shoving needs no code here at all — vphysics already
/// resolves a player against a light object — and the decision left is whether
/// the thing can be hurt.
///
/// It cannot, unless the map asks. Spawnflag 128 is the mapper wanting
/// func_breakable's damage rules back; without it a pushable is furniture, and
/// shooting furniture should move it rather than destroy it.
/// </summary>
[ScriptedEntity(EntityNames.FuncPushable, ScriptedEntityKind.Brush)]
public sealed class FuncPushable : ScriptedEntity
{
    public FuncPushable(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    public readonly Output OnBreak = new();
    public readonly Output OnHealthChanged = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncPushableVars"/>.</summary>
    [ScriptVars] public FuncPushableVars Vars = new();

    /// <summary>
    /// Stock's SF_PUSH_BREAKABLE. High, because a pushable shares
    /// func_breakable's spawnflags and this one starts above them.
    /// </summary>
    private const int BreakableFlag = 0x0080;

    /// <summary>What stock gives every pushable, whatever its size. See the class note.</summary>
    private const float PushableMass = 30f;

    private bool _broken;

    /// <summary>
    /// The health the damage fraction is measured against. Computed ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance divided by zero and
    /// every <c>OnHealthChanged</c> after a <c>cs_sv_reload</c> reported
    /// nonsense (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _startingHealth;

    /// <summary>
    /// Whether damage does anything to this. Static because a test has no engine
    /// to read spawnflags from, and this is the one rule worth asking about.
    /// </summary>
    public static bool IsBreakable(int spawnFlags) => (spawnFlags & BreakableFlag) != 0;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        _startingHealth = Math.Max(1, Vars.Health);

        // Only a breakable one is given health. Writing it on furniture would
        // make it destructible in the one way that matters — the shell applies
        // whatever OnDamaged returns, and zero is what says "nothing happened".
        if (IsBreakable(SpawnFlags))
            Health = _startingHealth;

        // A brush has its model by now, so the physics object can be made here.
        // See FuncPhysbox for why a prop cannot.
        if (!Physics.InitPhysics(Handle))
        {
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be simulated\n");
            return;
        }

        Mass = PushableMass;
    }

    /// <summary>A map overriding the thirty, which is the only reason it would.</summary>
    [ScriptedEvent] private void OnSetMass(float mass)
    {
        if (mass > 0f)
            Mass = mass;
    }

    // Breaking it by hand works on furniture too: the flag decides whether
    // DAMAGE can destroy it, not whether a map can.
    [ScriptedEvent("Break")] private void OnBreakInput() => Break();

    [ScriptedEvent] private void OnSetHealth(float health)
    {
        _startingHealth = Math.Max(1, (int)health);
        Health = _startingHealth;
        OnHealthChanged.Fire(1f);
    }

    [ScriptedEvent] private void OnAddHealth(float amount) => AdjustHealth((int)amount);
    [ScriptedEvent] private void OnRemoveHealth(float amount) => AdjustHealth(-(int)amount);

    private void AdjustHealth(int delta)
    {
        if (_broken)
            return;

        Health = Math.Max(0, Health + delta);
        OnHealthChanged.Fire(Health / (float)_startingHealth);

        if (Health <= 0)
            Break();
    }

    //-------------------------------------------------------------------------
    // How much of a blow this actually takes.
    //
    // Nothing, without the flag, and that is the entity: stock's OnTakeDamage
    // returns before the base class ever sees the info. It reports nothing
    // either, because there is no output on a pushable to report a hit with.
    //-------------------------------------------------------------------------
    /// <summary>
    /// CPushable reaches CBreakable, and so the base's physics push, only when
    /// it is breakable; then CBreakable refuses a blow under minhealthdmg
    /// (func_break.cpp:1297-1302, 823-845; plan_physics_managed A30).
    /// </summary>
    public override int OnDamagedAt(EntityHandle attacker, float damage, DamageType damageType,
                                    Vector position, Vector force)
    {
        if (!_broken && IsBreakable(SpawnFlags) && (int)damage >= Vars.MinimumDamage)
            VPhysicsTakeDamage(damageType, position, force);
        return OnDamaged(attacker, damage, damageType);
    }

    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_broken || !IsBreakable(SpawnFlags))
            return 0;

        int taken = (int)damage;

        if (taken < Vars.MinimumDamage)
            return 0;

        OnHealthChanged.Fire(Math.Max(0, Health - taken) / (float)_startingHealth);

        return taken;
    }

    public override void OnKilled(EntityHandle attacker) => Break();

    private void Break()
    {
        // Once. A crate hit twice in a tick would otherwise fire its outputs
        // twice and be removed twice.
        if (_broken)
            return;

        _broken = true;
        OnBreak.Fire();
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="FuncPushable"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPushableVars
{
    public int Health { get; set; } = 1;

    /// <summary>Damage below this is ignored entirely, not reduced. func_breakable's rule.</summary>
    [VarName("minhealthdmg")]
    public int MinimumDamage { get; set; }
}
