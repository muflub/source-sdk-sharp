//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.TempEnts;

/// <summary>
/// <c>CTEEffectDispatch</c>, played. <b>ONE TEMP ENTITY STANDING FOR FIFTY-TWO
/// CLIENT EFFECTS</b>, and the busiest one in the build.
///
/// <para>
/// The whole of <c>c_te_effect_dispatch.cpp</c>'s
/// <c>C_TEEffectDispatch::PostDataUpdate</c>:
/// </para>
/// <code>
/// const char *pEffectName =
///     g_StringTableEffectDispatch-&gt;GetString( m_EffectData.GetEffectNameIndex() );
/// if ( pEffectName )
/// {
///     DispatchEffectToCallback( pEffectName, m_EffectData );
///     RecordEffect( pEffectName, m_EffectData );
/// }
/// </code>
///
/// <para>
/// <b>THE ONLY NESTED DATATABLE IN THE INVENTORY, AND THE FIRST ONE ON THE WIRE
/// WITH PROPS IN IT.</b> Forty-three tables nest a BASE, which the engine names
/// <c>"baseclass"</c> and puts at offset zero; this one nests
/// <c>DT_EffectData</c> as a FIELD, under its own name, with twenty-eight props
/// under it. Lane CN put the first nested table on a live wire with
/// <c>CTEArmorRicochet</c> — but that one nests <c>DT_TEMetalSparks</c> as a
/// base and declares no props of its own, so the nesting carried no data. This
/// is where the mechanism is actually exercised.
/// </para>
///
/// <para>
/// <b>AND IT IS WHY THIS CLASS READS ITS FIELDS BY THEIR OWN NAMES.</b>
/// <c>SendProxy_DataTableToDataTable</c> shifts the object pointer by the
/// datatable prop's offset, so the nested table's props live at
/// <c>m_EffectData</c>'s offset plus their own — and
/// <c>TempEntRegistry.AddFieldsOf</c> flattens exactly that into
/// <see cref="TempEntType.Fields"/>. So <c>m_vOrigin</c> is addressable here
/// even though <c>DT_TEEffectDispatch</c> declares one prop and it is not that
/// one. There is no nested reader and there does not need to be.
/// </para>
///
/// <para>
/// <b>THE NESTED TABLE IS NOT AT BLOB OFFSET ZERO</b>, which cost lane S4 a
/// segfault: <c>SendPropDataTable</c> marks a prop <c>SPROP_COLLAPSIBLE</c> when
/// the proxy is the identity AND the offset is zero, and a collapsed table is
/// applied by the receiver at the OBJECT BASE. <c>TeClassBuilder.NestedTablePad</c>
/// puts four bytes in front of it. This class does not have to know that — it
/// reads by name — but a reader chasing an offset in a dump does.
/// </para>
///
/// <para>
/// <b>BEHAVIOUR LEDGER ROW C7 WAS NEITHER CLOSED NOR HIDDEN HERE, AND CARRYING
/// THE NAME UNCHANGED IS WHAT LET IT BE CLOSED PROPERLY LATER.</b>
/// <c>DispatchEffect("Impact")</c> is the busiest dispatch name in this build
/// and for its whole life <b>it never played</b>:
/// <c>DECLARE_CLIENT_EFFECT( "Impact", ... )</c> existed only in
/// <c>tf/tf_fx_impacts.cpp</c>, <c>hl2/fx_hl2_impacts.cpp</c> and
/// <c>sdk/sdk_fx_impacts.cpp</c>, none of which is compiled into this client, so
/// the name reached <c>DispatchEffectToCallback</c>, matched none of the
/// fifty-two registrations, and printed stock's own
/// <c>"DispatchEffect: effect '%s' not found on client"</c>.
/// <b>Substituting a name that did resolve would have turned a tracked gap into
/// an untracked one.</b> Lane <c>fx-impact</c> closed the row the way the row
/// allows — by registering a callback under that exact spelling in
/// <c>sourcesharp/game/client/shell_impact_effects.cpp</c> — and <b>nothing in
/// this file changed</b>, which is the whole dividend of having carried the
/// name rather than a working substitute for it.
/// </para>
///
/// <para>
/// <b>THE +1 ON <c>m_nSurfaceProp</c> COMES BACK OFF HERE.</b> Stock's send
/// proxy is <c>SendProxy_ShortAddOne</c> and the client's read proxy is
/// <c>RecvProxy_ShortSubOne</c>; the proxies are gone (plan 2B) and
/// <c>TempEnts.DispatchEffect</c> writes <c>value + 1</c>, so this is the other
/// half of that handshake. Dropping it shifts every surface property by one —
/// a different impact sound and different debris, with nothing anywhere saying
/// so. Its neighbour <c>m_iEffectName</c>, three props away, has no such
/// arithmetic and must not acquire any.
/// </para>
///
/// <para>
/// <b>THE INDEX IS RESOLVED THROUGH THE ENGINE'S OWN TABLE.</b>
/// <see cref="IEngineEffects.EffectDispatchName"/> is
/// <c>g_StringTableEffectDispatch-&gt;GetString</c> and nothing else, which is
/// the client mirror of the server's <c>EffectDispatchIndex</c>. A managed
/// dictionary of names would be a second numbering of one table, and the
/// symptom of two numberings disagreeing is the WRONG effect rather than none —
/// which reads as a bug in that effect.
/// </para>
///
/// <para>
/// The <c>RecordEffect</c> tool message is not carried; docs/divergences.md
/// covers all eighteen recorders. <c>TE_DispatchEffect</c> — a client
/// dispatching for itself — still reaches <c>DispatchEffectToCallback</c>
/// directly in <c>c_te_effect_dispatch.cpp</c>, which is the SAME function this
/// path ends at, so the two are one description already and the file's
/// recording survives on that path.
/// </para>
/// </summary>
[ClientTempEnt("CTEEffectDispatch")]
public sealed class ClientTeEffectDispatch : IClientTempEnt
{
    private readonly IEngineEffects _effects;
    private readonly IClientEffectDispatch _dispatch;

    public ClientTeEffectDispatch(IEngineEffects effects, IClientEffectDispatch dispatch)
    {
        _effects = effects;
        _dispatch = dispatch;
    }

    /// <summary>
    /// The name the last dispatch resolved to, or empty if it resolved to
    /// nothing. Kept so a test and <c>cs_cl_te_report</c> can say WHICH effect
    /// arrived — the counters cannot, because one temp entity stands for
    /// fifty-two.
    /// </summary>
    public string LastEffectName { get; private set; } = string.Empty;

    /// <summary>
    /// How many arrived carrying an index the string table does not hold. Stock
    /// draws nothing for those and so does this; counting them is how a table
    /// that failed to replicate is told apart from a client with no dispatches.
    /// </summary>
    public int Unresolved { get; private set; }

    public void Play(in TempEntReader te)
    {
        // THE INDEX FIRST, AND STILL BY NAME. A name that does not resolve is a
        // dispatch that does not happen -- stock's `if ( pEffectName )` -- and
        // reading one field to decide that is the point: the declaration form
        // below reads all twenty-two through reflection, which is the right
        // trade for a dispatch that is going to happen and the wrong one for a
        // dispatch that is not. This is the single deliberate by-name read left
        // on this boundary.
        int nameIndex = te.UInt("EffectName");
        string name = _effects.EffectDispatchName(nameIndex);

        if (string.IsNullOrEmpty(name))
        {
            LastEffectName = string.Empty;
            Unresolved++;
            return;
        }

        Play(TempEntDeclarationReader.Read<TeEffectDispatch>(in te).EffectData, name);
    }

    /// <summary>
    /// The locally-forwarded entry: a native forward built this state from the
    /// arguments it was called with, so there was never a blob to decode and the
    /// early-out above has nothing to save.
    /// </summary>
    public void Play(TempEntState state)
    {
        TeEffectData d = ((TeEffectDispatch)state).EffectData;

        string name = _effects.EffectDispatchName(d.EffectName);

        if (string.IsNullOrEmpty(name))
        {
            LastEffectName = string.Empty;
            Unresolved++;
            return;
        }

        Play(d, name);
    }

    private void Play(TeEffectData d, string name)
    {
        LastEffectName = name;

        // THE REST THROUGH THE DECLARATION. Twenty-two fields used to be
        // twenty-two string keys and twenty-two hand-picked accessors here; the
        // schema already says the name, the type and the signedness of every one
        // of them, so this asks it instead. What is left below is the part that
        // is genuinely this boundary's work: mapping the wire's shape onto
        // CEffectData's.
        //
        // ANGLES AND NORMAL are the reason that mapping is not a memcpy. The
        // struct's field order is CEffectData's and the wire's is
        // DT_EffectData's, and they disagree -- see TeEffectData, where the swap
        // is the transcription error cs_sv_te_dump cannot see. Assigning by
        // MEMBER makes the disagreement something the compiler carries rather
        // than something a string has to get right.
        var data = new EffectData
        {
            Origin = d.Origin,
            Start  = d.Start,
            Angles = d.Angles,
            Normal = d.Normal,

            Flags     = d.Flags,
            Magnitude = d.Magnitude,
            Scale     = d.Scale,
            Radius    = d.Radius,

            // SIGNED, five bits and no SPROP_UNSIGNED: -1 is "no attachment"
            // and it has to survive. The declaration says so, so the reader
            // already read it signed and this is a plain assignment.
            AttachmentIndex = d.AttachmentIndex,

            // THE PROXY'S ARITHMETIC, undone -- the one read on this boundary
            // that is not a copy. See the class note; the wire carries the
            // surface index plus one so that -1 fits an unsigned eight-bit prop.
            SurfaceProp = d.SurfaceProp - 1,

            Material   = d.Material,
            DamageType = d.DamageType,
            HitBox     = d.HitBox,
            EntIndex   = d.EntIndex,
            Color      = d.Color,

            // One bit on the wire and one byte in the blob; an int across the
            // ABI, because a bool has no fixed width in C.
            CustomColors = d.CustomColors ? 1 : 0,
            Color1       = d.Color1,
            Color2       = d.Color2,

            ControlPoint1          = d.ControlPoint1 ? 1 : 0,
            ControlPointAttachment = d.ParticleAttachment,
            ControlPointOffset     = d.ControlPointOffset,
        };

        // m_iEffectName is deliberately NOT in the struct: the NAME is what the
        // dispatch table (EffectDispatchTable, managed) resolves.
        _dispatch.Dispatch(name, in data);
    }
}
