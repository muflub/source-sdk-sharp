using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_funnel: a cone of sprites that falls into a point, or flies out of one.
///
/// <para>
/// One temp entity and then the entity removes itself, which makes it the
/// simplest thing in this batch and the one with the most surprising interface:
/// it has no input at all. Stock drives it from Use, so the only way a map can
/// fire one is a connection to its <c>Use</c>, and the entity is single-shot —
/// it deletes itself on the same frame.
/// </para>
///
/// <para>
/// Both of those are kept, and the second is why: a funnel that could be fired
/// twice would be a different entity, and every map that wants two has placed
/// two. A named "Fire" input IS added alongside Use, because an input that
/// cannot be typed into <c>ent_fire</c> cannot be tested from outside the game,
/// and the whole verification method here depends on that. Recorded in
/// docs/divergences.md as an addition rather than a change.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvFunnel)]
public sealed class EnvFunnel : ScriptedEntity
{
    public EnvFunnel(IEngineEffects effects, IEnginePrecache precache)
    {
        Effects = effects;
        Precache = precache;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;

    /// <summary>asking for what this entity will need before the level runs.</summary>
    private readonly IEnginePrecache Precache;
    /// <summary>Outward rather than inward.</summary>
    private const int ReverseFlag = 1;

    /// <summary>
    /// The sprite the funnel is made of. Stock's, and it is not a keyvalue —
    /// a mapper cannot choose it, so neither can this.
    /// </summary>
    private const string FunnelSprite = "sprites/flare6.vmt";

    /// <summary>
    /// The precached funnel sprite, fetched once in <see cref="OnSpawn"/>,
    /// which a hot reload does not run again -- so it is <c>[SaveField]</c>.
    /// Uncarried it would come back -1 and every funnel after a
    /// <c>cs_sv_reload</c> would silently draw nothing
    /// (OnSpawnFieldReloadTests).
    /// </summary>
    [SaveField] private int _sprite = -1;

    public override void OnSpawn()
    {
        _sprite = Precache.PrecacheModel(FunnelSprite);

        // Nothing to see and nothing to walk into. A point entity has no model
        // anyway; stock says so explicitly and it costs nothing to be as clear.
        SetVisible(false);
    }

    [ScriptedEvent("Use")]
    private void OnUse() => Fire();

    /// <summary>
    /// The addition. Same behaviour as Use, under a name a console command can
    /// reach — see the note on the class.
    /// </summary>
    [ScriptedEvent("Fire")]
    private void OnFire() => Fire();

    private void Fire()
    {
        if (_sprite <= 0)
            return;

        Effects.FunnelEffect(Origin, _sprite, (SpawnFlags & ReverseFlag) != 0);

        // Gone, exactly as stock: it schedules SUB_Remove for the current
        // frame. Removing directly rather than through a think is the same
        // thing a frame earlier, and there is nothing left that could observe
        // the difference.
        Remove();
    }
}
