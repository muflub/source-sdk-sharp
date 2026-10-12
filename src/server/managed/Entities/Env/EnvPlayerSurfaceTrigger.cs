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
/// env_player_surface_trigger: notices when a player walks onto a different
/// kind of ground.
///
/// The first entity here driven by a PUSH rather than a poll. What the ground is
/// made of is known inside the movement code, at the moment the trace that found
/// it is still in hand, and nothing outside can ask as cheaply afterwards — so
/// the engine calls out on change and this listens. See OnPlayerSurfaceChanged
/// in sourcesharp.h.
/// </summary>
[ScriptedEntity(EntityNames.EnvPlayerSurfaceTrigger)]
public sealed class EnvPlayerSurfaceTrigger : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="EnvPlayerSurfaceTriggerVars"/>.</summary>
    [ScriptVars] public EnvPlayerSurfaceTriggerVars Vars = new();

    public readonly Output OnSurfaceChangedToTarget = new("OnSurfaceChangedToTarget");
    public readonly Output OnSurfaceChangedFromTarget = new("OnSurfaceChangedFromTarget");

    /// <summary>The module's triggers, which this one joins while it is live.</summary>
    public EnvPlayerSurfaceTrigger(IEntityRoster<EnvPlayerSurfaceTrigger> triggers)
    {
        _triggers = triggers;
    }

    /// <summary>
    /// Every live one, because the push arrives at the module rather than at an
    /// entity and has to be handed on. The module's roster; it was a static
    /// list.
    ///
    /// A list the instances keep themselves, which works now and would not have
    /// a week ago: OnRemoved never used to fire for an entity on this hierarchy
    /// — a virtual called from a base destructor dispatches to the base — so a
    /// list like this would have grown stale handles forever. See
    /// docs/divergences.md.
    /// </summary>
    private readonly IEntityRoster<EnvPlayerSurfaceTrigger> _triggers;

    /// <summary>What the player was last known to be standing on.</summary>
    private int _current;

    public override void OnSpawn() => _triggers.Add(this);

    public override void OnRemoved() => _triggers.Remove(this);

    /// <summary>
    /// The push, fanned out to everything watching.
    /// </summary>
    /// <param name="triggers">The triggers to hand it to: the module's roster.</param>
    public static void PlayerSurfaceChanged(IEnumerable<EnvPlayerSurfaceTrigger> triggers,
                                            EntityHandle player, int gameMaterial)
    {
        // Nought means the player is not on the ground. Stock ignores it, and
        // the comment beside it says why: otherwise a bunny-hopper leaves every
        // trigger the moment they jump, and comes back to it on landing.
        if (gameMaterial == 0)
            return;

        // A copy, because an output fired below can remove an entity — and on
        // this hierarchy that now really does reach OnRemoved and edit the list.
        foreach (var trigger in triggers.ToArray())
            trigger.Notice(gameMaterial);
    }

    private void Notice(int gameMaterial)
    {
        if (Vars.Disabled || !ShouldFire(_current, gameMaterial, Vars.Target))
            return;

        _current = gameMaterial;

        // Deferred by a tick, which is stock's and is not fussiness: with
        // sv_alternateticks two surface changes can land in one tick, and firing
        // straight from the push would send both. Thinking once collapses them.
        SetNextThink(0f);
    }

    public override void OnThink()
    {
        if (_current == Vars.Target)
            OnSurfaceChangedToTarget.Fire();
        else
            OnSurfaceChangedFromTarget.Fire();

        StopThinking();
    }

    /// <summary>
    /// Whether a change is one this trigger cares about.
    ///
    /// Both halves matter and the second is easy to miss: it fires when the
    /// player steps ONTO the watched material and again when they step OFF it,
    /// and nothing at all for a change between two materials it was not asked
    /// about. Static and taking all three so it can be tested without an engine.
    /// </summary>
    internal static bool ShouldFire(int current, int arriving, int target)
        => arriving != current && (arriving == target || current == target);

    [ScriptedEvent("Enable")]
    private void OnEnable() => Vars.Disabled = false;

    [ScriptedEvent("Disable")]
    private void OnDisable() => Vars.Disabled = true;
}

/// <summary>
/// The map keys <see cref="EnvPlayerSurfaceTrigger"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvPlayerSurfaceTriggerVars
{
    /// <summary>The material this one is watching for. A surfaceprop game material.</summary>
    [VarName("gamematerial")]
    public int Target { get; set; }

    [VarName("StartDisabled")]
    public bool Disabled { get; set; }
}
