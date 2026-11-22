using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Server.Horde.Motion;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// A robot that dies falls as a ragdoll, as MvM's robots do (a TF player's
/// death leaves a <c>tf_ragdoll</c>: <c>CTFPlayer::CreateRagdollEntity</c>,
/// tf_player.cpp:15599). A member of the hit chain: a hit that killed leaves
/// one body through the core's <see cref="Corpses.Leave"/>, a separate entity
/// (<c>hl2mp_ragdoll</c>, which every client collapses itself), so the pooled
/// body goes back to the pool as it always did: it is hidden at the death
/// (<see cref="RobotBody.Died"/>) and parked at the next decision.
///
/// <para>
/// The body wears the robot's model and skin, falls where the robot stood
/// facing its way, from the sequence it was playing, moving as it moved, and
/// is thrown by the killing hit's force (a blast's exaggerated as stock's is,
/// tf_player.cpp:12401-12409) on the root element (stock's
/// <c>m_nForceBone</c> default, baseanimating.cpp:315: the hit names no physics
/// bone). It is not solid on the server (<see cref="SsRagdoll.OnSpawn"/>), so
/// it blocks no doorway for the nav or a hero; on the client it collides with
/// the world. The core's cap retires the oldest, and <see cref="Lifetime"/>
/// removes it on the server once every client has faded it.
/// </para>
///
/// <para>
/// AT THE MODEL'S OWN SIZE: a giant's body is not scaled, as stock's
/// <c>tf_ragdoll</c> carries no model scale (CreateRagdollEntity copies none,
/// and the client's jointed body is built from the unscaled collision model).
/// The Tank and the sentry buster leave none (<see cref="RobotLooks.LeavesRagdoll"/>).
/// </para>
/// </summary>
[SideService(Side.Server)]
public sealed class RobotRagdolls(IHordeRules rules, IHordeCurrent horde, RobotMotion motion, Corpses corpses) : IRobotHitSink, IOrderedHook
{
    /// <summary>After the champions' death blast and teleport (First + 540, + 545): the last reader of a hit.</summary>
    public const int Order = ModStages.First + 548;

    /// <summary>
    /// Seconds a robot's body stands on the server: the client's fade
    /// (<c>cl_ragdoll_fade_time</c>, 15 by default) and its alpha run (under
    /// half a second), with room.
    /// </summary>
    public const float Lifetime = 20f;

    /// <summary>The ragdoll element the throw lands on: the root (stock's m_nForceBone default).</summary>
    public const short ForceBone = 0;

    public int HookOrder => Order;

    /// <summary>Bodies left, and deaths that leave none by their kind (the Tank, the buster).</summary>
    public long Left { get; private set; }

    public long Exempt { get; private set; }

    /// <summary>The last body left; none before the first.</summary>
    public EntityHandle Last { get; private set; } = EntityHandle.None;

    public void OnRobotHit(in RobotHit hit)
    {
        if (!hit.Killed || horde.Current is not { Roster: { } roster, Bodies: { } bodies } || hit.Body < 1 || hit.Body > bodies.LastBody)
            return;

        var body = bodies.Body(hit.Body);
        if (body.Parked || body.Robot != hit.Robot)
            return;

        if (!RobotLooks.LeavesRagdoll(rules.Rules.Robots.Template(roster.Template(hit.Robot.Index))))
        {
            Exempt++;
            return;
        }

        AiVector moving = motion.TryView(hit.Body, out var view) ? view.Velocity : default;
        var left = corpses.Leave(Shot(body, hit, HordeVectors.ToEngine(moving)));
        if (left == EntityHandle.None)
            return;

        Last = left;
        Left++;
    }

    /// <summary>The body <paramref name="body"/> leaves when <paramref name="hit"/> kills it, moving at <paramref name="velocity"/>.</summary>
    public static CorpseShot Shot(RobotBody body, in RobotHit hit, Vector velocity) =>
        new(body.Model, HordeVectors.ToEngine(body.Position), new Vector(0f, body.Yaw, 0f))
        {
            Velocity = velocity,
            Force = Throw(hit.Force, hit.Bits),
            ForceBone = ForceBone,
            Skin = body.Skin,
            Sequence = body.State.Sequence > 0 ? body.State.Sequence : (short)0,
            Lifetime = Lifetime,
        };

    /// <summary>
    /// The killing hit's force as the body is thrown with it: a blast that
    /// kills without gibbing is exaggerated, 2.5 times across and twice up
    /// (<c>CTFPlayer::Event_Killed</c>, tf_player.cpp:12401-12409).
    /// </summary>
    public static Vector Throw(Vector force, DamageType bits) =>
        (bits & DamageType.Blast) != 0 ? new Vector(force.X * 2.5f, force.Y * 2.5f, force.Z * 2f) : force;
}
