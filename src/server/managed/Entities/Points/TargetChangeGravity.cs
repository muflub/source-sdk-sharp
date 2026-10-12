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
/// Changes how fast the player who set it off falls, and puts it back.
///
/// The narrow cousin of <c>trigger_gravity</c>: that one is a volume and acts on
/// whoever is standing in it, this one is a point with no shape at all and acts
/// on the ACTIVATOR — whoever pressed the button that fired it. Nothing about
/// where the entity itself sits matters.
///
/// Server gravity is untouched. Despite the name it has never been
/// <c>sv_gravity</c> that this writes; it is the per-player scale on top of it,
/// which is what lets one player be light while the round carries on normally
/// for everyone else.
/// </summary>
[ScriptedEntity(EntityNames.TargetChangegravity)]
public sealed class TargetChangeGravity : ScriptedEntity
{
    public TargetChangeGravity(IEngineHost host, IEngineEntityPhysics physics)
    {
        Host = host;
        Physics = physics;
    }

    /// <summary>the process and the match.</summary>
    private readonly IEngineHost Host;

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    /// <summary>What a map may set on this entity. See <see cref="TargetChangeGravityVars"/>.</summary>
    [ScriptVars] public TargetChangeGravityVars Vars = new();

    /// <summary>Ordinary falling, which is what a scale of one means.</summary>
    private const float Normal = 1f;

    [ScriptedEvent] private void OnChangeGrav(InputData input) => Apply(input.Activator, ScaleFor(Vars.Gravity));
    [ScriptedEvent] private void OnResetGrav(InputData input) => Apply(input.Activator, Normal);

    /// <summary>
    /// The scale actually applied, which is not quite the number the map wrote.
    ///
    /// Zero is not "no gravity" anywhere in Source — the movement code reads a
    /// scale of zero as "nothing was set here" and falls back to normal. Saying
    /// so here means a map that wrote zero and the reset path go down the same
    /// line, rather than both relying on the engine to keep agreeing.
    ///
    /// A negative scale is left alone. It makes the activator fall upwards,
    /// which stock allows and which is a perfectly good thing for a map to want.
    /// </summary>
    internal static float ScaleFor(float written) => written == 0f ? Normal : written;

    /// <summary>
    /// Players only, which is stock's rule — it reaches for the activator
    /// through <c>ToBasePlayer</c> and gives up if that is null. A crate that
    /// pressed the button is not made to float.
    /// </summary>
    private void Apply(EntityHandle activator, float scale)
    {
        // With no engine to ask — the tests — the activator is taken at its
        // word. The same reading as the touch path: a guard that cannot be
        // evaluated must not silently swallow the thing it guards.
        if (Host.IsInitialized && !Entities.IsPlayer(activator))
            return;

        Physics.SetGravity(activator, scale);
    }
}

/// <summary>
/// The map keys <see cref="TargetChangeGravity"/> declares.
/// </summary>
[ScriptVars]
public partial class TargetChangeGravityVars
{
    /// <summary>
    /// The scale to put the activator on. A multiplier of <c>sv_gravity</c>
    /// rather than a replacement for it: 2 falls twice as fast, 0.3 drifts.
    /// </summary>
    public float Gravity { get; set; }
}
