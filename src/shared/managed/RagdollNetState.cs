using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a corpse networks beyond its model, origin and angles — the impulse
/// the client throws it with, and which bone took the hit.
///
/// <para>
/// SMALL ON PURPOSE, and the smallness is the design rather than an omission.
/// A ragdoll is twenty-four bones with a position and an angle each, and there
/// are two ways to get one onto a screen. A SERVER ragdoll simulates it once
/// and networks all forty-eight values every tick, costing an edict and a great
/// deal of wire. A CLIENT ragdoll networks the handful of numbers needed to
/// START one and lets every client run the same solve locally.
/// </para>
///
/// <para>
/// This is the second, which is what stock does too:
/// <c>CBaseAnimating::BecomeRagdollOnClient</c> sends no ragdoll at all — it
/// sets <c>m_nRenderFX = kRenderFxRagdoll</c> and <c>m_vecForce</c> and lets
/// the far side build one. The model index, the origin and the angles the
/// solve starts from already ride the standalone shell for every entity that
/// has a model, so the only things left to declare are the two below.
/// </para>
///
/// <para>
/// DIVERGENCE, recorded rather than hidden: stock fills its force from
/// <c>m_vecTotalBulletForce</c>, an accumulation of every hit's direction and
/// magnitude over the life of the player. This mod's damage pipeline
/// (<c>ScriptedEntity.OnDamaged</c>) carries a magnitude and a type and no
/// direction, so there is no such vector to send. <see cref="Force"/> carries
/// the player's velocity at the moment of death instead — a body that was
/// running when it died keeps going, which is the visible half of what stock's
/// force does, and a body shot standing still drops where it stood rather than
/// being flung. Giving the damage path a direction is what would close it.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsRagdoll)]
public partial class RagdollNetState
{
    /// <summary>
    /// Which bone the impulse lands on, or -1 for the body's centre.
    ///
    /// <para>
    /// Stock's <c>m_nForceBone</c>, and it is sent as a <c>short</c> rather
    /// than an <c>int</c> for the same reason stock sends it as eight bits: a
    /// studio model is capped at <c>MAXSTUDIOBONES</c> and a bone index has
    /// never needed thirty-two of them. -1 has to survive the trip, which is
    /// why it is signed.
    /// </para>
    /// </summary>
    public partial short ForceBone { get; set; }

    /// <summary>
    /// The entity index of the player this body came from, or -1.
    ///
    /// <para>
    /// THE POSE IT COLLAPSES FROM IS WHY THIS IS HERE, and the bug it fixes was
    /// visible before it was understood: a body built from the model's
    /// REFERENCE pose comes out as a long stretched smear across the floor. A
    /// <c>.phy</c>'s constraint limits are authored against a real animation
    /// pose, so collapsing from the arms-out reference skeleton starts every
    /// joint outside its limits and the first solve tears the body apart.
    /// </para>
    ///
    /// <para>
    /// Stock networks <c>m_hPlayer</c> on <c>hl2mp_ragdoll</c> for exactly this
    /// and reads <c>pPlayer-&gt;GetSequence()</c> off it, with an
    /// <c>Assert( false ); // missing walk_lower?</c> if there is none — it
    /// cares a great deal which pose a body starts in. An INDEX rather than a
    /// handle because the client resolves it through
    /// <c>IClientStandalones.GetByIndex</c>, which is what that map is for.
    /// </para>
    ///
    /// <para>
    /// -1 is an ordinary answer, not a failure: the player may be dormant, out
    /// of PVS, or simply not decoded yet on the machine drawing the body. The
    /// client falls back to resolving an idle on the body's own model, which is
    /// still a real pose and still collapses correctly.
    /// </para>
    /// </summary>
    public partial short PlayerIndex { get; set; }

    /// <summary>
    /// The impulse the body starts with, in units per second.
    ///
    /// <para>
    /// See the divergence note on the class: this is the death VELOCITY, not
    /// stock's accumulated bullet force. Zero is an ordinary value — most
    /// deaths happen standing still — and means "collapse where you stand"
    /// rather than "no ragdoll".
    /// </para>
    /// </summary>
    public partial Vector Force { get; set; }

    /// <summary>
    /// Where the body collapses, in world space.
    ///
    /// <para>
    /// CARRIED SEPARATELY FROM THE ENTITY'S OWN ORIGIN, which sounds redundant
    /// and is the difference between a body and a smear. The entity's origin
    /// has NOT been applied yet when the first update reaches
    /// <c>OnDataChanged</c>: measured, the server spawned a body at
    /// <c>(960.0, 2560.0, 1.8)</c> and the client collapsed it at
    /// <c>(0.0, 0.0, 0.0)</c>. Every ragdoll element is placed from the
    /// skeleton, the skeleton is built at the entity's transform, so the whole
    /// body was built at the MAP ORIGIN and then drawn stretched between there
    /// and where it really was.
    /// </para>
    ///
    /// <para>
    /// Stock carries <c>m_vecRagdollOrigin</c> on <c>hl2mp_ragdoll</c> for
    /// exactly this reason and slams it in both branches of
    /// <c>CreateHL2MPRagdoll</c> — <c>SetNetworkOrigin</c> then
    /// <c>SetAbsOrigin</c> — rather than trusting the entity's own. The managed
    /// client cannot slam it (<c>EntitySetOrigin</c> is a server-only slot), so
    /// it WAITS for the shell to apply the transform and uses this as the
    /// expected value to recognise when that has happened — see
    /// <c>ClientSsRagdoll.Placed</c>. The angles ride along with the origin
    /// when the shell applies it, so they need no field of their own.
    /// </para>
    /// </summary>
    public partial Vector Origin { get; set; }

}
