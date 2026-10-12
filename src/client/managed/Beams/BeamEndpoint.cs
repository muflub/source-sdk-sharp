namespace SourceSharp.Client.Beams;

/// <summary>What native says about a beam endpoint's entity (BeamEntityFact_t, shell_beam_render.cpp).</summary>
[Flags]
public enum BeamEntityFacts
{
    None = 0,

    /// <summary>The client holds something at the handle.</summary>
    Resolved = 0x01,

    /// <summary>It is an SDK entity.</summary>
    Sdk = 0x02,

    /// <summary>It renders.</summary>
    Renderable = 0x04,

    /// <summary>It is a managed player.</summary>
    Player = 0x08,

    /// <summary>The attachment asked for answered.</summary>
    Attachment = 0x10,

    /// <summary>It has a collideable (so a world-space centre).</summary>
    Collideable = 0x20,
}

/// <summary>Which hierarchy answers a beam endpoint.</summary>
public enum BeamEndpointRoute
{
    /// <summary>Nothing at the handle: the point is left alone and the answer is false.</summary>
    Nothing = 0,

    /// <summary>An SDK entity: stock's ComputeBeamEntPosition, whose body had been cut to <c>return false</c>.</summary>
    SdkEntity,

    /// <summary>A managed entity, answered from its renderable and collideable.</summary>
    ManagedRenderable,
}

/// <summary>Where a managed endpoint with no attachment answer lands.</summary>
public enum BeamEndpointFallback
{
    RenderOrigin = 0,

    /// <summary>A player's: stock's "player origins are at their feet" correction.</summary>
    WorldSpaceCenter,
}

/// <summary>One endpoint's plan.</summary>
public readonly record struct BeamEndpointPlan(
    BeamEndpointRoute Route, bool TryAttachment, bool HitboxesRefused, BeamEndpointFallback Fallback);

/// <summary>
/// The beam endpoint's route (behaviour ledger row C8), as
/// shell_beam_endpoint_client.cpp's beam_endpoint.h decided it before the C2
/// beam port moved the decision here. A beam keeps its endpoint as a handle;
/// the hierarchy behind the handle decides how its point is found:
/// <list type="bullet">
/// <item>nothing at the handle (out of the PVS, freed, a stale serial): the
/// point is LEFT ALONE and the answer is false, stock's documented behaviour
/// for a beam that should stay a moment after its entity died;</item>
/// <item>an SDK entity: stock's function, which had been reduced to its two
/// refusals (so false);</item>
/// <item>a managed entity with a renderable: its attachment when one was
/// asked for and answers; a HITBOX index is refused (the managed hierarchy has
/// no hitbox set) and falls through as stock falls through for an endpoint
/// with no C_BaseAnimating; then a player's centre, else the render origin.</item>
/// </list>
/// </summary>
public static class BeamEndpoint
{
    public static BeamEndpointPlan PlanFor(bool resolved, bool isSdkEntity, bool hasRenderable,
                                           bool attachmentIndexIsHitboxIndex, bool isPlayer)
    {
        if (!resolved)
            return default;

        // The SDK hierarchy answers for itself, and none of the rest applies.
        if (isSdkEntity)
            return new BeamEndpointPlan(BeamEndpointRoute.SdkEntity, false, false, BeamEndpointFallback.RenderOrigin);

        if (!hasRenderable)
            return default;

        return new BeamEndpointPlan(
            BeamEndpointRoute.ManagedRenderable,
            TryAttachment: !attachmentIndexIsHitboxIndex,
            HitboxesRefused: attachmentIndexIsHitboxIndex,
            Fallback: isPlayer ? BeamEndpointFallback.WorldSpaceCenter : BeamEndpointFallback.RenderOrigin);
    }
}
