#if DEBUG
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Horde;

/// <summary>
/// <c>tf2_descent_robot_pose &lt;robot&gt; &lt;body_pitch&gt; [body_yaw] [bone]</c> (debug builds,
/// CR11's live proof): leans a robot row's body through its own pose dials (the net state for the
/// clients and the server's skeleton), then prints the pose the server's skeleton holds, where the
/// server put a bone (<c>bip_head</c> unless named) before and after, and what a shot line from
/// across the body (from its right), through each of the two places, hits on the same tick (the engine's
/// MASK_SHOT trace, which tests the body's hitboxes): so the robot has no time to react. An idle
/// robot keeps the lean: the motion writes a pose only when its own changes.
/// </summary>
[ConCommand("tf2_descent_robot_pose", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_pose <robot> <body_pitch> [body_yaw] [bone]: lean a robot's body, print the server skeleton's pose and bone, and shoot a line at the bone's old and new place")]
public sealed class RobotPoseCommand(IHordeCurrent horde, IEngineEntityRender render, IEngineEntities entities, IEngineWorldQuery world, ILog log)
{
    /// <summary>How far to the side of the bone the shot lines start.</summary>
    public const float Stand = 128f;

    public void Execute(int robot, float bodyPitch, float bodyYaw = 0f, string bone = "bip_head")
    {
        if (RobotStatusLines.Body(horde, robot, out string why) is not { } body)
        {
            log.Msg($"[TF2Descent/Pose] robot_pose: {why}\n");
            return;
        }

        int b = entities.LookupBone(body.Handle, bone);
        bool before = entities.GetBonePosition(body.Handle, b, out Vector was);

        body.Pose(body.State.MoveX, body.State.MoveY, bodyYaw, bodyPitch);

        int pitch = render.LookupPoseParameter(body.Handle, "body_pitch");
        int yaw = render.LookupPoseParameter(body.Handle, "body_yaw");
        bool after = entities.GetBonePosition(body.Handle, b, out Vector now);
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Pose] robot {robot} entindex {entities.GetIndex(body.Handle)} model {body.Model}: server body_pitch {render.GetPoseParameter(body.Handle, pitch):0.###} " +
            $"body_yaw {render.GetPoseParameter(body.Handle, yaw):0.###}; server {bone} (#{b}) before {Place(before, was)} after {Place(after, now)}; " +
            $"origin {body.Position.X:0.##} {body.Position.Y:0.##} {body.Position.Z:0.##} yaw {body.Yaw:0.##}\n"));

        if (!before || !after)
            return;

        // Across the body, from its right: a lean moves the head along the facing, so a line
        // along the facing would meet the body wherever the head went.
        float rad = body.Yaw * MathF.PI / 180f;
        var across = new Vector(MathF.Sin(rad), -MathF.Cos(rad), 0f);
        Shoot(body, "the place before the pose", was, across);
        Shoot(body, "the place after the pose", now, across);
    }

    private void Shoot(RobotBody body, string what, Vector target, Vector across)
    {
        Vector from = target + across * Stand;
        Vector to = target - across * Stand;
        TraceResult tr = world.TraceLine(from, to, TraceMask.Shot);
        string hit = !tr.Hit ? "nothing"
            : tr.Entity == body.Handle ? "the body"
            : $"entity {entities.GetIndex(tr.Entity)} {entities.GetClassName(tr.Entity)}";
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[TF2Descent/Pose]   shot through {what} ({target.X:0.##} {target.Y:0.##} {target.Z:0.##}): hits {hit} at {tr.End.X:0.##} {tr.End.Y:0.##} {tr.End.Z:0.##}\n"));
    }

    private static string Place(bool placed, Vector at) =>
        placed ? string.Create(CultureInfo.InvariantCulture, $"{at.X:0.##} {at.Y:0.##} {at.Z:0.##}") : "none";
}
#endif
