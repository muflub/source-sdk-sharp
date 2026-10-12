namespace SourceSharp.Abi;

/// <summary>
/// The <c>EF_*</c> effect bits, as <c>src/public/const.h</c> declares them.
///
/// <para>
/// This is <c>m_fEffects</c>'s vocabulary, and the storage it names is
/// <see cref="EngineBlockHead.Effects"/> — one word in every entity's block,
/// networked by the <c>m_fEffects</c> prop in
/// <c>src/sourcesharp/game/shared/standalone_nettable.h</c>.
/// </para>
///
/// <para>
/// DECLARED HERE RATHER THAN INFERRED. The values are the engine's, not this
/// port's: they go on the wire and they are compared against bits native code
/// reads out of the same word (<c>ViewSubject::EffectActive</c>), so a number
/// invented on this side would be a silent disagreement about what a hidden
/// entity is. <c>EngineBlockTests</c> pins each one against the C++ header.
/// </para>
/// </summary>
[System.Flags]
public enum EntityEffect
{
    /// <summary>Nothing set.</summary>
    None = 0,

    /// <summary>EF_BONEMERGE — bone merge on the client.</summary>
    BoneMerge = 0x001,

    /// <summary>EF_BRIGHTLIGHT — a dlight centred on the entity's origin.</summary>
    BrightLight = 0x002,

    /// <summary>EF_DIMLIGHT — the player flashlight. Read by <c>SourceSharp_ViewerEffectActive</c>.</summary>
    DimLight = 0x004,

    /// <summary>EF_NOINTERP — do not interpolate the next frame.</summary>
    NoInterp = 0x008,

    /// <summary>EF_NOSHADOW — casts no shadow.</summary>
    NoShadow = 0x010,

    /// <summary>
    /// EF_NODRAW — do not draw this entity.
    ///
    /// <para>
    /// The one this port needed and did not have: before step 8e-render,
    /// <c>CStandaloneEntity::SetVisible(false)</c> zeroed the model index
    /// instead, so an invisible managed brush arrived on the client with no
    /// shape at all and the three shims that draw inside one — dust, smoke,
    /// precipitation — refused it for having no model.
    /// </para>
    /// </summary>
    NoDraw = 0x020,

    /// <summary>EF_NORECEIVESHADOW — receives no shadow.</summary>
    NoReceiveShadow = 0x040,

    /// <summary>EF_BONEMERGE_FASTCULL — the cheap bone-merge cull.</summary>
    BoneMergeFastCull = 0x080,

    /// <summary>EF_ITEM_BLINK — blink an item so the player notices it.</summary>
    ItemBlink = 0x100,

    /// <summary>EF_PARENT_ANIMATES — assume the parent is animating.</summary>
    ParentAnimates = 0x200,
}
