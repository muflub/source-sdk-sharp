using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_zoom: pulls a player's view in, and lets it back out.
///
/// The interpolation is the engine's — it owns the field of view and eases
/// between two of them. What is a decision is how far, how fast, and for whom,
/// which is the whole of this class.
/// </summary>
[ScriptedEntity(EntityNames.EnvZoom)]
public sealed class EnvZoom : ScriptedEntity
{
    public EnvZoom(IEnginePlayers players)
    {
        Players = players;
    }

    /// <summary>the players this entity acts on.</summary>
    private readonly IEnginePlayers Players;
    /// <summary>What a map may set on this entity. See <see cref="EnvZoomVars"/>.</summary>
    [ScriptVars] public EnvZoomVars Vars = new();

    /// <summary>
    /// Zooms whoever set it off.
    ///
    /// The activator rather than everybody, which is stock's and is the only
    /// reading that makes sense: a zoom is a thing done to one pair of eyes, and
    /// a map firing this from a trigger means the player who walked in.
    /// </summary>
    [ScriptedEvent("Zoom")]
    private void OnZoom(InputData input) => Apply(input.Activator, Vars.Fov, Vars.Rate);

    /// <summary>
    /// Hands the view back.
    ///
    /// A field of view of ZERO is the release, not a zoom to nothing: the engine
    /// tracks who requested the zoom, and zero from that same requester is how
    /// one says it has finished. The player then returns to their own setting
    /// rather than to a number this entity chose.
    /// </summary>
    [ScriptedEvent("UnZoom")]
    private void OnUnZoom(InputData input) => Apply(input.Activator, 0, Vars.Rate);

    private void Apply(EntityHandle activator, int fov, float rate)
    {
        if (!Entities.IsPlayer(activator))
            return;

        // This entity is offered as the requester, and on this hierarchy it
        // cannot actually be one: SetFOV takes a CBaseEntity owner and asserts
        // it is not null, so the shim substitutes the player. The consequence is
        // that two env_zooms on one player share an owner and can release each
        // other -- see docs/divergences.md.
        Players.SetFov(activator, Handle, fov, rate);
    }
}

/// <summary>
/// The map keys <see cref="EnvZoom"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvZoomVars
{
    /// <summary>Seconds the change takes. Zero snaps.</summary>
    public float Rate { get; set; } = 1f;

    /// <summary>The field of view to pull in to, in degrees.</summary>
    public int Fov { get; set; } = 75;
}
