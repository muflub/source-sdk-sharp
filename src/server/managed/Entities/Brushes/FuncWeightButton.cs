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
/// CWeightButton (weight_button.cpp): a plate that presses when the stress on
/// it passes WeightToActivate and lets go when it falls below. SOLID_VPHYSICS
/// with a shadow, read every tick by CalculateObjectStress, which sums the
/// normal force of each thing pressing on the plate by gravity into kilograms
/// and, the plate's shadow being neither moveable nor static, adds the
/// plate's own mass: an empty stock plate already weighs something.
/// </summary>
[ScriptedEntity(EntityNames.FuncWeightButton, ScriptedEntityKind.Brush)]
public sealed class FuncWeightButton : ScriptedEntity
{
    public FuncWeightButton(IObjectStressQuery stress, IEngineEntityPhysics physics)
    {
        Stress = stress;
        Physics = physics;
    }

    /// <summary>The plate's stress, as CalculateObjectStress sums it.</summary>
    private readonly IObjectStressQuery Stress;

    /// <summary>Whether the plate has an object to weigh.</summary>
    private readonly IEngineEntityPhysics Physics;

    public readonly Output OnPressed = new();
    public readonly Output OnReleased = new();

    /// <summary>What a map may set on this entity. See <see cref="FuncWeightButtonVars"/>.</summary>
    [ScriptVars] public FuncWeightButtonVars Vars = new();

    /// <summary>m_bHasBeenPressed.</summary>
    private bool _pressed;

    public override void OnSpawn()
    {
        // CWeightButton::Spawn: SOLID_VPHYSICS with a shadow (VPhysicsInitShadow).
        SetSolidity(SolidType.VPhysics, SolidFlags.None);
        InitShadowPhysics();
        _pressed = false;

        // TriggerThink, every tick.
        SetNextThink(0f);
    }

    /// <summary>CWeightButton::TriggerThink: no object, no more thinks; otherwise weigh and look again next tick.</summary>
    public override void OnThink()
    {
        if (Physics.PhysicsObject(Handle) == 0)
        {
            StopThinking();
            return;
        }

        Weigh(Stress.StressOf(Handle));
        SetNextThink(0f);
    }

    /// <summary>
    /// The latch, given the stress on the plate. Equal to the threshold is
    /// neither pressed nor released: stock's pair of strict comparisons.
    /// </summary>
    internal void Weigh(float stress)
    {
        if (stress > Vars.WeightToActivate && !_pressed)
        {
            OnPressed.Fire();
            _pressed = true;
        }
        else if (stress < Vars.WeightToActivate && _pressed)
        {
            OnReleased.Fire();
            _pressed = false;
        }
    }
}

/// <summary>
/// The map keys <see cref="FuncWeightButton"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncWeightButtonVars
{
    /// <summary>Kilograms that have to be on the plate before it presses.</summary>
    public float WeightToActivate { get; set; }
}
