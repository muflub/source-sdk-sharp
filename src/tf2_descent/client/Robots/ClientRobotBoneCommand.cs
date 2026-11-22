#if DEBUG
using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Client;

namespace TF2.Descent.Client.Robots;

/// <summary>
/// <c>tf2_descent_robot_bone_cl &lt;entindex&gt; [bone index]</c> (debug builds, CR11's live proof):
/// where this client draws a robot's bone (6, a bot's <c>bip_head</c>, unless given), from its own
/// skeleton at the pose it eased to, beside the networked dials; the server's half is
/// <c>tf2_descent_robot_pose</c>, which prints the same bone's index.
/// </summary>
[ConCommand("tf2_descent_robot_bone_cl", Flags = ConVarFlags.Cheat,
            Help = "tf2_descent_robot_bone_cl <entindex> [bone index]: where this client draws a robot's bone, and its pose dials")]
public sealed class ClientRobotBoneCommand(ILog log, ClientRobotPoses robots, IEngineEntities entities, IEngineEntityRender render)
{
    public void Execute(int entindex, int bone = 6)
    {
        foreach (var r in robots.Live)
        {
            if (entities.GetIndex(r.Handle) != entindex)
                continue;

            StandaloneBones? bones = render.BoneMatrices(r.Handle);
            string at = bones is not null && bone >= 0 && bone < bones.Count
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{bones.BoneToWorld[bone * StandaloneBones.FloatsPerMatrix + 3]:0.##} {bones.BoneToWorld[bone * StandaloneBones.FloatsPerMatrix + 7]:0.##} {bones.BoneToWorld[bone * StandaloneBones.FloatsPerMatrix + 11]:0.##}")
                : "none";
            log.Msg(string.Create(CultureInfo.InvariantCulture,
                $"[TF2Descent/Pose] client robot {entindex}: drawn body_pitch {r.Current(2):0.###}/{r.Target(2):0.###} body_yaw {r.Current(3):0.###}/{r.Target(3):0.###}; " +
                $"client bone #{bone} {at}\n"));
            return;
        }

        log.Msg($"[TF2Descent/Pose] client holds no robot at entindex {entindex}\n");
    }
}
#endif
