//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// Which arm of <see cref="ClientEntityDecal.AddDecal"/> ran.
///
/// <para>
/// Stock's <c>AddDecal</c> returns nothing and its callers ignore what it did.
/// This answers, because two of the four outcomes are SILENT in stock and a
/// caller that cannot tell them from a decal has no way to say which happened —
/// and "the decal is not appearing" is the report both of them produce.
/// </para>
/// </summary>
public enum EntityDecalArm
{
    /// <summary>
    /// There is no such entity on this client, so nothing was drawn. Stock's
    /// <c>if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL ) return;</c>,
    /// which is the line above the call rather than a branch inside it.
    /// </summary>
    NoSuchEntity,

    /// <summary><c>C_BaseEntity::AddStudioDecal</c> — an <c>.mdl</c>.</summary>
    StudioModel,

    /// <summary>
    /// <c>C_BaseEntity::AddBrushModelDecal</c> — a brush model, and the WORLD
    /// is one of those.
    /// </summary>
    BrushModel,

    /// <summary>
    /// A model that is neither: a sprite, or an entity carrying no model at
    /// all. Stock's <c>default:</c> arm, whose whole body is
    /// <c>tr.fraction = 1.0f</c> — and <c>tr</c> is a local nobody reads at
    /// either of the two call sites, so this draws nothing and says nothing.
    /// It is stock's behaviour and not a gap.
    /// </summary>
    NoDecalPath,
}

/// <summary>
/// <c>C_BaseEntity::AddDecal</c> (c_baseentity.cpp:3453), for this client's
/// PARALLEL hierarchy as well as the stock one. THE THING BEHAVIOUR LEDGER ROWS
/// C2 AND C3 WERE OPEN FOR.
///
/// <para>
/// Both rows named this member. Its two arms are <c>AddStudioDecal</c> and
/// <c>AddBrushModelDecal</c>, both of them <c>C_BaseEntity</c> members, and
/// <c>C_StandaloneEntity</c> — the shell every managed entity is — declares
/// neither. So a decal aimed at an entity, or at the world with no hitbox, was
/// dropped: silently for the whole port, and after lane CX with a line naming
/// the row. This is the capability those lines were placeholders for.
/// </para>
///
/// <para>
/// STOCK, WITH <c>doTrace</c> FALSE, WHICH IS BOTH CALL SITES:
/// </para>
/// <code>
/// Ray_t ray;
/// ray.Init( rayStart, rayEnd );
/// ray.m_Delta *= 1.1f;                       // bloat a little bit
///
/// switch ( modelinfo-&gt;GetModelType( model ) )
/// {
/// case mod_studio: AddStudioDecal( ray, hitbox, decalIndex, false, tr, maxLOD ); break;
/// case mod_brush:  AddBrushModelDecal( ray, decalCenter, decalIndex, false, tr ); break;
/// default:         tr.fraction = 1.0f; break;
/// }
/// </code>
///
/// <para>
/// THE SWITCH IS HERE AND THE ARMS ARE NATIVE, which is the split lane CX
/// argued for when it refused a trace-and-decal-in-one slot for
/// <c>CTEProjectedDecal</c>. The two arms reach two DIFFERENT engine subsystems
/// — <c>modelrender</c> for a studio model and <c>effects-&gt;DecalShoot</c> for
/// a brush — so neither can move; the choice between them is three lines of
/// decision made from one number, and a decision that lives in a backend is one
/// no <c>#ifndef</c> marks and no scanner finds.
/// </para>
///
/// <para>
/// THE RAY DOES NOT CROSS AND NEITHER DOES THE BLOAT. Both are inside
/// <see cref="IEngineEffects.ClientDecalStudioModel"/>: a <c>Ray_t</c> is not a
/// thing this boundary carries, and <c>ray.m_Delta *= 1.1f</c> multiplies a
/// DELTA. Doing it out here by moving the endpoint would hand the engine
/// <c>(start + d * 1.1f) - start</c>, which is not <c>d * 1.1f</c> in floating
/// point — a rounding, and a rounding on a decal is a decal round the corner.
/// </para>
///
/// <para>
/// <c>hitbox</c> IS NOT PASSED, and that is a reading of stock rather than a
/// trim. <c>AddStudioDecal</c> takes an <c>int hitbox</c> and its body never
/// mentions it; <c>AddBrushModelDecal</c> is not given one. The only thing
/// either call site's hitbox decides is which of the TEMP ENTITY's two arms
/// runs, and that test is already made before this is called.
/// </para>
///
/// <para>
/// <c>decalCenter</c> IS NOT <paramref name="rayEnd"/>. It is stock's third
/// argument and only the brush arm reads it. <c>c_te_decal.cpp</c> passes the
/// same vector for both and <c>c_te_projecteddecal.cpp</c> does not, so a port
/// that collapsed them would agree with one file and quietly move every
/// projected decal in the other.
/// </para>
/// </summary>
public static class ClientEntityDecal
{
    /// <summary>
    /// Stamps a decal on whatever <paramref name="entIndex"/> is, and answers
    /// which arm ran.
    /// </summary>
    /// <param name="world">The side that can say what a client entity's model is.</param>
    /// <param name="effects">The side that draws.</param>
    /// <param name="entIndex">
    /// The entity index. ZERO IS THE WORLD and is an ordinary subject here —
    /// <c>cl_entitylist-&gt;GetEnt( 0 )</c> is the world entity, the world is a
    /// brush model, and that is where an ordinary projected decal lands.
    /// </param>
    /// <param name="rayStart">Stock's <c>rayStart</c>; read by the studio arm only.</param>
    /// <param name="rayEnd">Stock's <c>rayEnd</c>; read by the studio arm only.</param>
    /// <param name="decalCenter">Stock's <c>decalCenter</c>; read by the brush arm only.</param>
    /// <param name="decalIndex">The decal to stamp.</param>
    public static EntityDecalArm AddDecal(IEngineWorldQuery world, IEngineEffects effects, IDecalSettings settings,
                                          int entIndex, Vector rayStart, Vector rayEnd,
                                          Vector decalCenter, int decalIndex)
    {
        // Stock's line ABOVE the call, not a branch inside it: TE_Decal reads
        // `if ( ( ent = cl_entitylist->GetEnt( entity ) ) == NULL ) return;`
        // and TE_ProjectDecal asserts the entity it traced is real. Null here
        // is "there is no such entity", which is NOT ModelType.Bad -- mod_bad
        // is a real answer about an entity that is really there, and folding
        // the two together would make a decal aimed at nothing look the same
        // as one aimed at something with no model.
        ModelType? model = world.ClientEntityModelType(entIndex);

        if (model is null)
            return EntityDecalArm.NoSuchEntity;

        switch (model.Value)
        {
            case ModelType.Studio:
                AddStudioDecal(effects, settings, entIndex, rayStart, rayEnd, decalIndex);
                return EntityDecalArm.StudioModel;

            case ModelType.Brush:
                effects.ClientDecalBrushModel(entIndex, decalCenter, decalIndex);
                return EntityDecalArm.BrushModel;

            default:
                // Stock's default arm, whole. It writes tr.fraction and nothing
                // else, and tr is a local both call sites throw away -- so a
                // decal aimed at a sprite draws nothing, in stock, and this
                // says so by answering rather than by warning. Warning here
                // would be a line the game does not print.
                return EntityDecalArm.NoDecalPath;
        }
    }

    /// <summary>How much longer than the segment the studio decal's ray is: stock's <c>ray.m_Delta *= 1.1f</c>.</summary>
    public const float StudioRayBloat = 1.1f;

    /// <summary>
    /// <c>C_BaseEntity::AddStudioDecal</c> with <c>doTrace</c> false (both of
    /// stock's call sites): nothing when <c>r_drawmodeldecals</c> is off
    /// ("Exit out after doing the trace so any other effects that want to happen
    /// can happen" -- there is no trace here), else the decal along the ray from
    /// <paramref name="rayStart"/> to <paramref name="rayEnd"/>, its DELTA
    /// lengthened a tenth (<c>ray.Init( rayStart, rayEnd ); ray.m_Delta *= 1.1f</c>)
    /// and handed over as the delta, the start untouched. The model instance and
    /// the renderer's AddDecal are the backend's (SourceSharp_ClientDecalStudioModel).
    /// Answers whether it was asked for.
    /// </summary>
    public static bool AddStudioDecal(IEngineEffects effects, IDecalSettings settings, int entIndex,
                                      Vector rayStart, Vector rayEnd, int decalIndex)
    {
        if (!settings.ModelDecals)
            return false;

        effects.ClientDecalStudioModel(entIndex, rayStart, StudioRayDelta(rayStart, rayEnd), decalIndex);
        return true;
    }

    /// <summary><c>( rayEnd - rayStart ) * 1.1f</c>, in single precision as stock's Ray_t holds it.</summary>
    public static Vector StudioRayDelta(Vector rayStart, Vector rayEnd)
        => new((rayEnd.X - rayStart.X) * StudioRayBloat,
               (rayEnd.Y - rayStart.Y) * StudioRayBloat,
               (rayEnd.Z - rayStart.Z) * StudioRayBloat);
}
