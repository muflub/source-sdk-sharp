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
/// A brush that breaks when it has taken enough damage. Crates, glass, the
/// scenery a firefight leaves in pieces.
/// </summary>
[ScriptedEntity(EntityNames.FuncBreakable, ScriptedEntityKind.Brush)]
public sealed class FuncBreakable : ScriptedEntity
{
    public readonly Output OnBreak = new();
    public readonly Output OnHealthChanged = new();
    public readonly Output OnTakeDamage = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncBreakableVars"/>.</summary>
    [ScriptVars] public FuncBreakableVars Vars = new();

    private bool _broken;

    /// <summary>
    /// The health the damage fraction is measured against. Computed ONCE, in
    /// <see cref="OnSpawn"/>, which a hot reload does not run again -- so it is
    /// <c>[SaveField]</c>. Uncarried, the rebuilt instance divided by zero (or
    /// whatever the default held) and every <c>OnHealthChanged</c> after a
    /// <c>cs_sv_reload</c> reported nonsense (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _startingHealth;

    public override void OnSpawn()
    {
        SetSolidity(BrushSolidity.Solid);
        SetVisible(true);

        if (Vars.Health <= 0)
            Vars.Health = 1;

        _startingHealth = Vars.Health;
        Health = Vars.Health;
    }

    //-------------------------------------------------------------------------
    // How much damage this actually takes.
    //
    // The minimum is the interesting rule: a crate with minhealthdmg set is
    // meant to survive small arms and yield to something heavier, so damage
    // under the threshold is not reduced — it is ignored.
    //-------------------------------------------------------------------------
    public override int OnDamaged(EntityHandle attacker, float damage, DamageType damageType)
    {
        if (_broken)
            return 0;

        int taken = (int)damage;

        if (taken < Vars.MinimumDamage)
            return 0;

        OnTakeDamage.Fire();
        OnHealthChanged.Fire(Math.Max(0, Health - taken) / (float)_startingHealth);

        return taken;
    }

    public override void OnKilled(EntityHandle attacker) => Break();

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

    private void Break()
    {
        // Once. A crate hit by two shots in the same tick would otherwise fire
        // its outputs twice and be removed twice.
        if (_broken)
            return;

        _broken = true;
        OnBreak.Fire();
        Remove();
    }
}

/// <summary>
/// The map keys <see cref="FuncBreakable"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncBreakableVars
{
    public int Health { get; set; } = 1;

    /// <summary>Damage below this is ignored entirely. Stops a crate dying to gunfire it should shrug off.</summary>
    [VarName("minhealthdmg")]
    public int MinimumDamage { get; set; }
}
