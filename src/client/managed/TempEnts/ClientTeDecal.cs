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
/// <c>CTEDecal</c>, played. The bullet-hole decal, aimed along the segment that
/// stopped the shot.
///
/// <para>
/// The whole of <c>c_te_decal.cpp</c>'s <c>TE_Decal</c>, which is what
/// <c>PostDataUpdate</c> calls with the five wire fields:
/// </para>
/// <code>
/// trace_t tr;
/// // Special case for world entity with hitbox:
/// if ( (entity == 0) &amp;&amp; (hitbox != 0) )
/// {
///     Ray_t ray;
///     ray.Init( *start, *pos );
///     staticpropmgr-&gt;AddDecalToStaticProp( *start, *pos, hitbox - 1, index, false, tr );
/// }
/// else
/// {
///     C_BaseEntity* ent;
///     if ( ( ent = cl_entitylist-&gt;GetEnt( entity ) ) == NULL )
///         return;
///     ent-&gt;AddDecal( *start, *pos, *pos, hitbox, index, false, tr );
/// }
/// </code>
///
/// <para>
/// THIS CLASS NEVER REACHED <c>effects-&gt;DecalShoot</c>, which is why lane CD
/// could not port it over <c>ClientDecalShoot</c> and reported it instead. Its
/// two arms are the static-prop one and the entity one, and the entity one was
/// behaviour ledger row C2. <c>ClientDecalStaticProp</c> is the slot lane CX
/// added for the first; <see cref="ClientEntityDecal"/> is the managed
/// <c>AddDecal</c> that closes the second.
/// </para>
///
/// <para>
/// BEHAVIOUR LEDGER ROW C2 IS CLOSED HERE, and it was CLOSED rather than
/// dropped: the <c>else</c> still runs, on the same arguments, and now ends in
/// a decal instead of in a warning. It covered more than its title said — any
/// entity at all, AND the world when no hitbox came with it, because
/// <c>cl_entitylist-&gt;GetEnt( 0 )</c> is the world entity and <c>AddDecal</c>
/// is what stamps a brush model — and all of that reaches
/// <see cref="ClientEntityDecal.AddDecal"/>, which has both arms.
/// </para>
///
/// <para>
/// <c>Ray_t ray; ray.Init(...)</c> did not come across because it never went
/// anywhere: stock builds it on the line above the static-prop call and then
/// passes the two points, not the ray. It is dead in stock too. (The OTHER ray,
/// the one <c>AddDecal</c> builds for the studio arm, is real — and is built
/// natively, because the bloat stock applies to it is a multiply on a delta.)
/// </para>
///
/// <para>
/// NOT PORTED: <c>RecordDecal</c>, the tool-recording KeyValues message. SFM is
/// dead (user ruling 2026-10-19). Stock's <c>Precache()</c> was empty.
/// </para>
/// </summary>
[ClientTempEnt("CTEDecal")]
public sealed class ClientTeDecal : ClientTempEnt<TeDecal>
{
    private readonly IEngineEffects _effects;
    private readonly IEngineWorldQuery _world;

    public ClientTeDecal(IEngineEffects effects, IEngineWorldQuery world)
    {
        _effects = effects;
        _world = world;
    }

    public override void Play(TeDecal te)
    {
        // All three UNSIGNED on the wire (SPROP_UNSIGNED), so read unsigned: an
        // edict, a hitbox number and a decal index are counts, never quantities.

        // Stock's own comment on this test is "Special case for world entity
        // with hitbox", and the hitbox is what makes it special: on the WORLD,
        // a non-zero hitbox is the engine saying "static prop number
        // hitbox - 1". That subtraction lives here, beside the test that reads
        // the same convention, rather than on the far side of the ABI.
        if (te.Entity == 0 && te.Hitbox != 0)
        {
            _effects.ClientDecalStaticProp(te.Start, te.Origin, te.Hitbox - 1, te.Index);
            return;
        }

        // Stock's else, and the arm behaviour ledger row C2 was open for.
        //
        // THE CENTRE IS THE ORIGIN AND SO IS THE RAY'S END. Stock passes
        // `*start, *pos, *pos` -- the second and third arguments are the same
        // vector HERE and are not in c_te_projecteddecal.cpp, which is why
        // AddDecal takes them separately rather than deriving one from the
        // other.
        ClientEntityDecal.AddDecal(_world, _effects, te.Entity,
                                   rayStart: te.Start, rayEnd: te.Origin,
                                   decalCenter: te.Origin, decalIndex: te.Index);
    }
}
