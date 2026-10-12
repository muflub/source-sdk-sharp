using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_muzzleflash: the flash of a gun going off, where a map asks for one.
///
/// <para>
/// This is the only entity in the particle, trail and smoke family that could
/// cross, and the reason is the shape of its delivery rather than the size of
/// its code. Every other row there is a networked entity whose STOCK CLIENT
/// CLASS runs the emitter — a managed entity never appears on the wire as
/// <c>SmokeTrail</c> or <c>CSteamJet</c>, so moving the classname would deliver
/// the entity perfectly and draw nothing. A muzzle flash is a temp entity
/// carrying a position, a set of angles and a size, read straight out of the
/// message by whoever receives it. There is no entity index in it to be NULL.
/// </para>
///
/// <para>
/// What stock does that this does not is re-parent itself to an attachment on
/// its parent model at spawn. That is now POSSIBLE rather than absent — the
/// hierarchy takes an attachment index and the shell claims a map's
/// <c>parentattachment</c> beside its <c>parentname</c> — so this is a port
/// waiting to be done rather than a stated limit. The paragraph this replaces
/// said "there is no parenting on the parallel hierarchy at all,
/// <c>SetParent</c> appears nowhere in the ABI", and both halves of that are
/// now false.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvMuzzleflash)]
public sealed class EnvMuzzleFlash : ScriptedEntity
{
    public EnvMuzzleFlash(IEngineEffects effects)
    {
        Effects = effects;
    }

    /// <summary>the small effects.</summary>
    private readonly IEngineEffects Effects;
    /// <summary>
    /// Stock's MUZZLEFLASH_TYPE_DEFAULT, from <c>shareddefs.h</c>.
    ///
    /// It is the only value stock's env_muzzleflash ever passes, and it is a
    /// constant here rather than a key because stock does not offer the choice
    /// to a mapper either — the other types belong to weapons.
    /// </summary>
    internal const int DefaultType = 1;

    /// <summary>What a map may set on this entity. See <see cref="EnvMuzzleFlashVars"/>.</summary>
    [ScriptVars] public EnvMuzzleFlashVars Vars = new();

    /// <summary>How big, as the map asked. Test-visible.</summary>
    internal float Scale => Vars.Scale;

    [ScriptedEvent("Fire")]
    private void OnFire()
        => Effects.MuzzleFlashEffect(Origin, Entities.GetAngles(Handle), Vars.Scale, DefaultType);
}

/// <summary>
/// The map keys <see cref="EnvMuzzleFlash"/> declares.
/// </summary>
[ScriptVars]
public partial class EnvMuzzleFlashVars
{
    /// <summary>How big the flash is. Stock leaves it at zero if the mapper says nothing.</summary>
    public float Scale { get; set; }

    /// <summary>
    /// The attachment on the parent model to sit on.
    ///
    /// Declared so a stock map's key is read rather than warned about, and
    /// unused: there is no parenting on the parallel hierarchy for it to name a
    /// point on. See the class comment.
    /// </summary>
    [VarName("parentattachment")]
    public string ParentAttachment { get; set; } = string.Empty;
}
