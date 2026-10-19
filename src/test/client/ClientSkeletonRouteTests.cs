using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's skeleton route (plan physics_bones_native_zero 1.2, client
/// half): filled, every client bone member the engine bindings answer is the
/// managed skeleton's; there is no native slot behind any of them.
/// </summary>
public class ClientSkeletonRouteTests
{
    private static readonly EntityHandle Standalone = new(0x00010007u);

    private sealed class Fake : IClientStandaloneSkeletons
    {
        public List<string> Calls { get; } = [];

        public bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out QAngle angles, out bool standalone)
        {
            Calls.Add("attachment");
            origin = default;
            angles = default;
            standalone = true;
            return true;
        }

        public int LookupAttachment(EntityHandle entity, string name) { Calls.Add("lookup"); return 7; }
        public int CachedBoneCount(EntityHandle entity) { Calls.Add("count"); return 9; }
        public StandaloneBones? BoneMatrices(EntityHandle entity) { Calls.Add("matrices"); return null; }
        public bool IsPoseable(EntityHandle entity) { Calls.Add("poseable"); return true; }
        public string StudioName(EntityHandle entity) { Calls.Add("name"); return "x.mdl"; }

        public bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart)
        {
            Calls.Add($"seq {sequence} {playbackRate} {restart}");
            return true;
        }

        public SkeletonScaleChange SetModelScale(EntityHandle entity, float scale)
        {
            Calls.Add($"scale {scale}");
            return SkeletonScaleChange.Changed;
        }

        public void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order, bool restart)
            => Calls.Add($"layer {slot} {sequence} {weight}");

        public int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle)
        {
            Calls.Add("layer?");
            sequence = 0; weight = 0; order = 0; cycle = 0;
            return 0;
        }

        public bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping, out int starts)
        {
            Calls.Add("state?");
            sequence = 0; cycle = 0; duration = 0; looping = false; starts = 0;
            return true;
        }

        public bool SequenceInfo(EntityHandle entity, out float duration, out bool looping)
        {
            Calls.Add("info?");
            duration = 0; looping = false;
            return true;
        }

        public float SequenceGroundSpeed(EntityHandle entity, int sequence) { Calls.Add("speed?"); return 0; }
        public int PoseParameterCount(EntityHandle entity) { Calls.Add("posecount?"); return 0; }
        public int LookupPoseParameter(EntityHandle entity, string name) { Calls.Add("poselookup?"); return -1; }
        public string PoseParameterName(EntityHandle entity, int parameter) { Calls.Add("posename?"); return ""; }

        public bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end)
        {
            Calls.Add("poserange?");
            start = 0; end = 0;
            return false;
        }

        public float SetPoseParameter(EntityHandle entity, int parameter, float value) { Calls.Add($"pose {parameter} {value}"); return 0; }
        public float GetPoseParameter(EntityHandle entity, int parameter) { Calls.Add("pose?"); return 0; }
        public void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex) => Calls.Add($"merge {sourceEntityIndex}");

        public int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains)
        {
            Calls.Add("procedural?");
            computed = 0; jiggle = 0; ikChains = 0;
            return 0;
        }
    }

    private static (IEngineEntityRender Render, Fake Fake) Routed()
    {
        var fake = new Fake();
        var route = new StandaloneSkeletonRoute { Skeletons = fake };
        return (SideBindings.Of<IEngineEntityRender>(new EngineSide(Side.Client), new StandaloneMirror(), route), fake);
    }

    [Fact]
    public void AFilledClientRouteAnswersEveryClientBoneMember()
    {
        var (render, fake) = Routed();

        render.SetPoseParameter(Standalone, 0, 45f);
        render.GetPoseParameter(Standalone, 0);
        render.PoseParameterCount(Standalone);
        render.LookupPoseParameter(Standalone, "aim_yaw");
        render.PoseParameterName(Standalone, 0);
        render.PoseParameterRange(Standalone, 0, out _, out _);
        render.SetAnimLayer(Standalone, 1, 2, 0.5f, 1f, 0, restart: true);
        render.AnimLayer(Standalone, 1, out _, out _, out _, out _);
        render.SetBoneMergeSource(Standalone, 4);
        render.ProceduralBones(Standalone, out _, out _, out _);
        render.SequenceGroundSpeed(Standalone, 3);

        Assert.Equal(new[]
        {
            "pose 0 45", "pose?", "posecount?", "poselookup?", "posename?", "poserange?",
            "layer 1 2 0.5", "layer?", "merge 4", "procedural?", "speed?",
        }, fake.Calls);
    }
}
