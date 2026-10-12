using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// <c>DT_TEKillPlayerAttachments</c> — "drop everything stuck to that player",
/// and the smallest table in the whole inventory: one prop, five bits.
///
/// <para>
/// From <c>te_killplayerattachments.cpp:69</c>:
/// </para>
/// <code>
/// IMPLEMENT_SERVERCLASS_ST(CTEKillPlayerAttachments, DT_TEKillPlayerAttachments)
///     SendPropInt( SENDINFO(m_nPlayer), 5, SPROP_UNSIGNED ),
/// END_SEND_TABLE()
/// </code>
///
/// <para>
/// <b>FIVE IS A LITERAL HERE AND A SYMBOL IN <c>DT_TEPlayerDecal</c>.</b> That
/// table writes <c>Q_log2( MAX_PLAYERS )</c> for the same quantity — a player
/// SLOT, not an edict — and it resolves to five in this build
/// (<c>Q_log2(33)</c> is 5, floor of log2 rather than a bit count). The two
/// agree today and the port does not unify them: a literal in stock's own table
/// stays a literal here and a symbol stays a symbol (plan 3.1), because the
/// symbol is what tracks <c>MAX_PLAYERS</c> moving under a different set of
/// defines and the literal is what stock committed to on this line. Writing
/// <see cref="TeBits.MaxPlayersLog2"/> here would be inventing a dependency
/// stock does not have, in the same way restating the ten of
/// <c>ANIMATION_SKIN_BITS</c> would be dropping one it does.
/// </para>
///
/// <para>
/// NO CALLER, LIVE OR OTHERWISE. Nothing in this build or in stock's own server
/// calls <c>te-&gt;KillPlayerAttachments</c> — it is reachable only through
/// <c>CTempEntsSystem</c>'s virtual, and the client half
/// (<c>c_te_killplayerattachments.cpp</c>) is registered and waiting. It is
/// ported for the reason plan 0.2b gives: the two sides' class counts have to
/// come out equal, and a class left behind is a hole
/// <c>cs_sv_te_dump</c> would report forever.
/// </para>
///
/// <para>
/// <b><c>BaseTable</c> HAS GONE, AND THIS DECLARATION NOW SAYS ITS BASE BY
/// DERIVING FROM IT.</b> It was spelt out for as long as
/// <see cref="TeBaseTempEntity"/> was a <c>struct</c> — a class cannot derive
/// from one — and that file belonged to the lane converting the twenty-two
/// tables that nest it. That lane has landed, so the argument goes and the
/// declaration becomes <c>: TeBaseTempEntity</c>. The wire is identical either
/// way: <c>DT_BaseTempEntity</c> is empty, so the base contributes no props and
/// no bytes, and it is only the NAME of the nested table that has to survive.
/// </para>
/// </summary>
[TempEntity]
public class TeKillPlayerAttachments : TeBaseTempEntity
{
    /// <summary>
    /// Whose attachments. A player SLOT in five bits — stock's literal, not
    /// <c>Q_log2( MAX_PLAYERS )</c>; see the class note for why the two are not
    /// unified. Stock's prop name was <c>m_nPlayer</c>.
    /// </summary>
    [TeProp(Bits = 5, Flags = TeFlags.Unsigned)]
    public int Player;
}
