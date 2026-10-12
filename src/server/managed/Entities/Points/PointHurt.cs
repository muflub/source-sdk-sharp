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
/// Hurts whatever is near it, invisibly, on a timer.
///
/// Not an explosion with the effects turned off, and the difference is why this
/// needed an engine call of its own. An explosion is an ENTITY the engine builds
/// by classname, and it brings a sprite, a sound and a scorch mark that its
/// spawnflags only partly suppress. What a map wants from a point_hurt is the
/// damage on its own — a pool of acid, a room filling with gas — repeated every
/// tenth of a second, where a bang each time would be the bug.
/// </summary>
[ScriptedEntity(EntityNames.PointHurt)]
public sealed class PointHurt : ScriptedEntity
{
    public PointHurt(IEngineDamage damage, IBlastSweep blast)
    {
        Damage = damage;
        Blast = blast;
    }

    /// <summary>hurting things.</summary>
    private readonly IEngineDamage Damage;

    /// <summary>
    /// The untargeted dose's traversal.
    ///
    /// <para>
    /// Added when the radius sweep became managed: it used to run natively
    /// behind <c>SourceSharp_RadiusDamage</c>, so this entity needed nothing
    /// but a way to say "hurt everything near here". See
    /// <see cref="BlastSweep"/>.
    /// </para>
    /// </summary>
    private readonly IBlastSweep Blast;
    /// <summary>What a map may set on this entity. See <see cref="PointHurtVars"/>.</summary>
    [ScriptVars] public PointHurtVars Vars = new();

    private const int StartOnFlag = 1;

    /// <summary>
    /// Who set it going, and therefore who is answerable for anything it kills.
    ///
    /// Stock carries this from whichever input turned it on, so a trap somebody
    /// triggered credits them with the kill. Kept, because it is the difference
    /// between a death message naming a player and naming nothing.
    /// </summary>
    private EntityHandle _activator = EntityHandle.None;

    private bool _on;

    public override void OnSpawn()
    {
        (Vars.Radius, Vars.Damage, Vars.Delay) = Settle(Vars.Radius, Vars.Damage, Vars.Delay);

        if ((SpawnFlags & StartOnFlag) != 0)
        {
            _on = true;
            SetNextThink(0.1f);
        }
    }

    /// <summary>
    /// The keys as the entity will actually use them.
    ///
    /// A map that leaves these unset means the stock defaults, and stock
    /// enforces them by CLAMPING rather than by defaulting: a radius written as
    /// zero becomes 128, not zero. Kept, because a map that wrote a zero
    /// expecting "off" has been getting 128 since 2004, and a map built against
    /// that is relying on it.
    ///
    /// Static and taking the values rather than reading the fields, so it can be
    /// tested without an engine — the same reason EnvShake.RadiusFor is.
    /// </summary>
    internal static (float Radius, int Damage, float Delay) Settle(float radius, int damage, float delay)
        => (radius > 0f ? radius : 128f,
            damage > 0 ? damage : 2,
            delay > 0f ? delay : 0.1f);

    public override void OnThink()
    {
        if (!_on)
            return;

        Hurt();
        SetNextThink(Vars.Delay);
    }

    [ScriptedEvent("TurnOn")]
    private void OnTurnOn(InputData input)
    {
        _activator = input.Activator;
        _on = true;
        SetNextThink(0.1f);
    }

    [ScriptedEvent("TurnOff")]
    private void OnTurnOff(InputData input)
    {
        _activator = input.Activator;
        _on = false;
        StopThinking();
    }

    [ScriptedEvent("Toggle")]
    private void OnToggle(InputData input)
    {
        if (_on)
            OnTurnOff(input);
        else
            OnTurnOn(input);
    }

    /// <summary>
    /// One dose now, whether or not it is running.
    ///
    /// Stock's version leaves the timer running afterwards, because it calls the
    /// think function directly and that function always asks to be called again.
    /// This does not: a Hurt input on an entity nobody turned on should deal its
    /// damage and stop, not switch the trap on as a side effect. See
    /// docs/divergences.md.
    /// </summary>
    [ScriptedEvent("Hurt")]
    private void OnHurt(InputData input)
    {
        _activator = input.Activator;
        Hurt();
    }

    private void Hurt()
    {
        if (Vars.Target.Length == 0)
        {
            // The managed traversal, not Damage.RadiusDamage: the falloff and
            // the blocking rule are BlastSweep's now rather than
            // CGameRules::RadiusDamage's. CLASS_NONE and a null ignore were the
            // native entry point's own arguments and neither survives as a
            // parameter -- nothing in this mod has a faction, and a point_hurt
            // is not an entity a blast has to pass through.
            //
            // The ACTIVATOR is both attacker and inflictor, which is what
            // CTakeDamageInfo( pAttacker, pAttacker, ... ) did inside
            // SourceSharp_RadiusDamage.
            Blast.Apply(Origin, Vars.Damage, Vars.Radius, Vars.DamageType,
                        attacker: _activator, inflictor: _activator,
                        ignored: EntityHandle.None);
            return;
        }

        // Every entity of that name, not the first: a map naming a group of
        // props means all of them, and stock's loop walks the whole list.
        foreach (var victim in Entities.FindAllByName(Vars.Target))
        {
            // The direction is from here to the victim, so anything the damage
            // throws goes AWAY from the source rather than in a fixed compass
            // direction. The force magnitude is the engine's, scaled off the
            // damage — see EntityTakeDamage.
            Vector at = Entities.GetOrigin(victim);

            Damage.TakeDamage(victim, _activator, Vars.Damage, Vars.DamageType,
                                    (at - Origin).Normalized, at);
        }
    }
}

/// <summary>
/// The map keys <see cref="PointHurt"/> declares.
/// </summary>
[ScriptVars]
public partial class PointHurtVars
{
    /// <summary>How far the harm reaches. Ignored entirely when a target is named.</summary>
    [VarName("DamageRadius")]
    public float Radius { get; set; } = 128f;

    public int Damage { get; set; } = 2;

    /// <summary>Seconds between one dose and the next.</summary>
    [VarName("DamageDelay")]
    public float Delay { get; set; } = 0.1f;

    /// <summary>
    /// Source's DMG_* bits, as the map writes them — the enum itself since plan
    /// Step 12. A map still writes the number, and may now write the name.
    /// </summary>
    public DamageType DamageType { get; set; }

    /// <summary>
    /// Hurt only what is called this, wherever it is.
    ///
    /// Not a filter on the radius — it REPLACES the radius. A named target on
    /// the far side of the map is hurt, and something standing on top of this
    /// entity is not.
    /// </summary>
    [VarName("DamageTarget")]
    public string Target { get; set; } = string.Empty;
}
