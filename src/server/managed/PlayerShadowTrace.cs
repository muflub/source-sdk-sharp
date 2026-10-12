using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_playershadow_trace</c>: one line per shadow step and one per
/// recovery update, for the push jam (plan_physics_managed A39). The jam's three
/// suspects are values no counter shows: what the plan aims at from
/// <c>OutWishVel</c>, what the recovery's impulse is against the push, and the
/// arrival time. <c>cs_sv_playershadow</c>'s totals cannot tell the plan's
/// frames from the recovery's; these lines can.
/// </summary>
public sealed class PlayerShadowTrace : IPlayerShadowTrace
{
    /// <summary>The prefix every line carries, for a log filter.</summary>
    public const string Prefix = "[cs_sv_playershadow_trace]";

    private readonly ILog _log;
    private readonly Func<bool> _on;

    public PlayerShadowTrace(ILog log, Func<bool> on)
    {
        _log = log;
        _on = on;
    }

    /// <inheritdoc/>
    public bool On => _on();

    /// <inheritdoc/>
    public void Planned(EntityHandle player, in PlayerShadowState s, in PlayerShadowPlan plan)
    {
        string extra = "";
        if (plan.StepUp != PlayerShadow.StepNone)
            extra += $" step {plan.StepUp}:{F(plan.StepHeight)}";
        if (plan.Destroy != 0)
            extra += " destroy";

        _log.Msg($"{Prefix} #{player.Value} plan cmd {s.CommandIndex}: origin {V(s.Origin)} old {V(s.OldOrigin)} " +
                 $"vel {V(s.Velocity)} wish {V(s.OutWishVel)} shadow {V(s.ShadowOrigin)} contact {s.ContactBits} " +
                 $"leaning {(PlayerShadow.IsLeaning(in s) ? 1 : 0)} ground {s.Flags & GameMovement.FlOnGround} " +
                 $"groundmass {F(s.GroundPhysicsMass)} stepheight {F(s.OutStepHeight)} " +
                 $"ft {F(s.FrameTime)} -> aim {V(plan.Position)} lead {F((plan.Position - s.Origin).Length)} " +
                 $"wishvel {V(plan.WishVelocity)} arrive {F(plan.Arrival)}{extra}\n");
    }

    /// <summary>
    /// One recovery update: where the player and the shadow were, how far
    /// apart, which arm ran, and the impulse given (zero when none).
    /// </summary>
    public void Recovered(EntityHandle player, in PlayerShadowState s, bool touched, float dist, float deltaV,
                          string arm, Vector impulse)
    {
        _log.Msg($"{Prefix} #{player.Value} recover: origin {V(s.Origin)} shadow {V(s.ShadowOrigin)} " +
                 $"vel {V(s.Velocity)} shadowvel {V(s.ShadowVelocity)} touched {(touched ? 1 : 0)} " +
                 $"contact {s.ContactBits} dist {F(MathF.Sqrt(dist))} dv {F(MathF.Sqrt(deltaV))} " +
                 $"arm {arm} impulse {V(impulse)} along {F(Along(impulse, s.Velocity))}\n");
    }

    /// <summary>
    /// The impulse's component along the player's own velocity: negative is a
    /// pull back against the walk, which is the recovery suspect's signature.
    /// </summary>
    public static float Along(Vector impulse, Vector velocity)
    {
        float len = velocity.Length;
        return len > 0f ? impulse.Dot(velocity) / len : 0f;
    }

    private static string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

    private static string V(Vector v) => $"({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
}

/// <summary>Turns on <see cref="PlayerShadowTrace"/>.</summary>
[ConVar(PlayerShadowTraceConVar.ConVarName, "0",
        Help = "Log every player shadow step and recovery update: the aim, the arrival, the impulse.",
        Flags = ConVarFlags.Cheat)]
public sealed class PlayerShadowTraceConVar : BoolConVar
{
    /// <summary>Its console name.</summary>
    public const string ConVarName = "cs_sv_playershadow_trace";
}
