using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using SourceSharp.Shared.Anim;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The server's copy of the shared animation machine, as <see cref="SsPlayer"/>
/// drives it from the tail of its PreThink (phase 0.10).
///
/// <para>
/// The machine's own rules are AnimStateMachineTests'; what is pinned here is
/// the server host's one write. The machine hands its main sequence over on
/// EVERY drive (the client's shell takes the rate from it), and the server's
/// sequence arm — <c>EntitySetSequence</c> — always restarts. Wired straight
/// through, the server's skeleton would restart its gait every tick and pose
/// hitboxes and attachments from frame zero forever: a picture nobody sees,
/// on the side that decides where a shot lands.
/// </para>
/// </summary>
public class PlayerAnimStateDriveTests
{
    private const string Model = "models/humans/group03/male_01.mdl";
    private const int RunSequence = 1;
    private const int JumpSequence = 2;

    /// <summary>An HL2DM-shaped player with an idle (2 s), a run (1 s) and a
    /// jump (0.5 s): the HL2 layer's first asks, as the side's managed studio
    /// header would describe them.</summary>
    private sealed class PlayerModel : IModelDescriptorSource
    {
        private static ModelSequence Seq(int index, string label, string activity, int flags, float duration)
            => new(index, label, activity, flags, duration, System.Array.Empty<ModelSequenceEvent>());

        public ModelDescriptor? Describe(int modelIndex) => ModelDescriptor.Create(
            1, System.Array.Empty<string>(), System.Array.Empty<Vector>(),
            new[]
            {
                Seq(0, "idle_melee", "ACT_HL2MP_IDLE_MELEE", 0x1, 2f),
                Seq(1, "run_melee", "ACT_HL2MP_RUN_MELEE", 0x1, 1f),
                Seq(2, "jump_melee", "ACT_HL2MP_JUMP_MELEE", 0, 0.5f),
            });
    }

    private static (SsPlayer Player, RecordingEngine Engine) Running(AnimStateLayers? layers = null)
    {
        var engine = Fake();
        engine.Returns("EntityRender.GetModelName", Model);
        engine.Returns("Entities.GetAngles", Vector.Zero);
        engine.Returns("Entities.GetEyeAngles", Vector.Zero);

        // The skeleton plays what was last written to it, as the server's does.
        engine.ReturnsEachCall("EntityRender.SequenceState.sequence",
                               () => SkeletonWrites(engine).DefaultIfEmpty(0).Last());
        engine.Ignores("EntityRender.SequenceState", "EntityRender.SequenceState.cycle",
                       "EntityRender.SequenceState.duration", "EntityRender.SequenceState.looping",
                       "EntityRender.SequenceState.starts");

        // The model's dials, by name, as the server's skeleton answers them
        // (ServerSkeletons.LookupPoseParameter); any other name is -1.
        engine.ReturnsEachCall("EntityRender.LookupPoseParameter",
                               () => PoseIndex(engine.AllOf("EntityRender.LookupPoseParameter").Last().ArgAs<string>(1)));
        engine.Ignores("EntityRender.SetPoseParameter", "EntityRender.GetPoseParameter",
                       "EntityRender.SequenceGroundSpeed");

        var models = new ModelDescriptors(_ => 1, _ => Model, new RecordingLog());
        models.AttachSource(new PlayerModel());

        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(), InfiniteAux(), PrintDamage(),
                                  models: models, clock: engine, animLayers: layers)
        {
            Handle = new EntityHandle(0x0001_0003u),
        };
        player.BindAreas(Areas(engine));
        player.State.BindLocal();
        player.State.Alive = true;
        player.State.MaxSpeed = 320f;
        player.State.Flags = 1u;                      // FL_ONGROUND
        player.State.Velocity = new Vector(300f, 0f, 0f);

        return (player, engine);
    }

    private const int BodyPitch = 0;
    private const int BodyYaw = 1;
    private const int MoveX = 2;
    private const int MoveY = 3;

    private static int PoseIndex(string name) => name switch
    {
        "body_pitch" => BodyPitch,
        "body_yaw" => BodyYaw,
        "move_x" => MoveX,
        "move_y" => MoveY,
        _ => -1,
    };

    /// <summary>Every value the machine wrote to one of the server skeleton's dials, in order.</summary>
    private static List<float> PoseWrites(RecordingEngine engine, int parameter)
        => engine.AllOf("EntityRender.SetPoseParameter").Where(c => c.ArgAs<int>(1) == parameter)
                 .Select(c => c.ArgAs<float>(2)).ToList();

    private static List<int> SkeletonWrites(RecordingEngine engine)
        => engine.AllOf("EntityRender.SetSequence").Select(c => c.ArgAs<int>(1)).ToList();

    [Fact]
    public void TheServersSkeletonIsHandedAGaitOnceAndNotEveryTick()
    {
        var (player, engine) = Running();

        for (int tick = 0; tick < 10; tick++)
            player.UpdateAnimState(10f + tick * 0.015f);

        var writes = SkeletonWrites(engine);

        Assert.True(writes.Count == 1,
            $"ten drives of one gait write the server's sequence once, not per tick ({writes.Count})");
        Assert.True(writes[0] == RunSequence, $"and it is the run the machine chose ({writes[0]})");
    }

    [Fact]
    public void ANewGaitReachesTheServersSkeleton()
    {
        var (player, engine) = Running();

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        player.State.Flags = 0u;                      // left the ground
        player.UpdateAnimState(10.03f);
        player.UpdateAnimState(10.045f);

        var writes = SkeletonWrites(engine);

        Assert.True(writes.SequenceEqual(new[] { RunSequence, JumpSequence }),
            $"the run, then the jump, each written once ({string.Join(",", writes)})");
    }

    /// <summary>
    /// A model change puts the skeleton back on sequence 0 (stock's SetModel's
    /// SetSequence( 0 )); the gait the machine still plays is written again on
    /// the next drive, as stock's ComputeMainSequence compares GetSequence().
    /// A respawn re-wears the player's model.
    /// </summary>
    [Fact]
    public void AGaitTheSkeletonNoLongerPlaysAfterAModelChangeIsWrittenAgain()
    {
        var (player, engine) = Running();
        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        engine.Returns("EntityRender.SequenceState.sequence", 0);
        player.UpdateAnimState(10.03f);

        var writes = SkeletonWrites(engine);
        Assert.True(writes.SequenceEqual(new[] { RunSequence, RunSequence }),
            $"the run, then the run again once the skeleton plays 0 ({string.Join(",", writes)})");
    }

    //-------------------------------------------------------------------------
    // The pose dials (stock's CMultiPlayerAnimState runs on the server too, and
    // its SetPoseParameter writes the CBaseAnimating array SetupBones poses the
    // hitboxes from): the machine's writes reach the server's skeleton.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A player looking 40 degrees up has his torso dial at 40 on the server's
    /// skeleton (ComputePoseParam_AimPitch: body_pitch is the eye pitch
    /// negated), which the server's hitbox trace poses from: he is hit where
    /// he is drawn looking up.
    /// </summary>
    [Fact]
    public void TheEyesPitchReachesTheServersBodyPitch()
    {
        var (player, engine) = Running();
        engine.Returns("Entities.GetEyeAngles", new Vector(-40f, 0f, 0f));

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        var pitch = PoseWrites(engine, BodyPitch);
        Assert.True(pitch.Count > 0 && pitch[^1] == 40f,
            $"the server's body_pitch is the eyes' pitch negated ({string.Join(",", pitch)})");
    }

    /// <summary>
    /// A player running straight ahead has his leg blend on the server's
    /// skeleton: move_x forward, move_y level (ComputePoseParam_MoveYaw's
    /// nine-way arm), scaled by the run's authored speed as the server's
    /// skeleton measures it.
    /// </summary>
    [Fact]
    public void ARunReachesTheServersLegBlend()
    {
        var (player, engine) = Running();
        engine.Returns("EntityRender.SequenceGroundSpeed", 300f);

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        var x = PoseWrites(engine, MoveX);
        var y = PoseWrites(engine, MoveY);
        Assert.True(x.Count > 0 && MathF.Abs(x[^1] - 1f) < 1e-4f && y.Count > 0 && MathF.Abs(y[^1]) < 1e-4f,
            $"move_x 1, move_y 0 for a run at the run's own speed (x {string.Join(",", x)}; y {string.Join(",", y)})");
        Assert.True(engine.AllOf("EntityRender.SequenceGroundSpeed").Any(),
            "the scale asked the server's skeleton for the run's authored speed");
    }

    /// <summary>
    /// The server's torso dial is posed with the body's yaw, as every other
    /// client draws this player: eyes turned 40 degrees from a body that has
    /// not turned leave body_yaw at 0 on the server, where posing the eyes
    /// would twist the server's hitboxes 40 degrees off the drawn body.
    /// </summary>
    [Fact]
    public void TheServersTorsoDialIsPosedWithTheBodysYawAsClientsDrawIt()
    {
        var (player, engine) = Running();
        player.State.Velocity = Vector.Zero;
        engine.Returns("Entities.GetEyeAngles", new Vector(0f, 40f, 0f));
        engine.Returns("Entities.GetAngles", Vector.Zero);

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);
        player.UpdateAnimState(10.03f);

        var yaw = PoseWrites(engine, BodyYaw);
        Assert.True(yaw.Count > 0 && yaw.All(v => v == 0f),
            $"body_yaw stays 0 with the body unturned ({string.Join(",", yaw)})");
    }

    /// <summary>
    /// A class change puts on a model whose dials are in another order (TF's
    /// scout, heavy and sniper list move_x before move_y; the other six the
    /// other way round): the machine looks them up again (stock's
    /// OnNewModel), so a run straight ahead still writes move_x 1 to the new
    /// model's move_x, not to the dial that was move_x on the old one.
    /// </summary>
    [Fact]
    public void AModelChangeLooksTheLegDialsUpAgain()
    {
        var (player, engine) = Running();
        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        engine.Returns("EntityRender.GetModelName", "models/humans/group03/female_01.mdl");
        engine.ReturnsEachCall("EntityRender.LookupPoseParameter",
                               () => engine.AllOf("EntityRender.LookupPoseParameter").Last().ArgAs<string>(1) switch
                               {
                                   "move_x" => MoveY,
                                   "move_y" => MoveX,
                                   string name => PoseIndex(name),
                               });
        int before = engine.AllOf("EntityRender.SetPoseParameter").Count();
        player.UpdateAnimState(10.03f);
        player.UpdateAnimState(10.045f);

        var after = engine.AllOf("EntityRender.SetPoseParameter").Skip(before).ToList();
        float LastAt(int index) => after.Last(c => c.ArgAs<int>(1) == index).ArgAs<float>(2);
        Assert.True(MathF.Abs(LastAt(MoveY) - 1f) < 1e-4f && MathF.Abs(LastAt(MoveX)) < 1e-4f,
            $"the new model's move_x (index {MoveY}) reads 1 and its move_y (index {MoveX}) 0 "
            + $"({LastAt(MoveY)}, {LastAt(MoveX)})");
    }

    //-------------------------------------------------------------------------
    // The layer chain's composition: a mod's IAnimStateLayer is a container
    // service (the plain [SideService] mark), and the server's machine asks
    // it in LayerOrder around the core's HL2 layer (order 1000).
    //-------------------------------------------------------------------------

    /// <summary>A mod's layer: names <see cref="JumpSequence"/> as the main sequence when it claims, and counts its asks.</summary>
    private abstract class ModLayer(int order, bool claims) : IAnimStateLayer
    {
        public int MainAsks;
        public int LayerOrder => order;

        public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
        {
            MainAsks++;
            if (!claims)
                return false;

            main.Activity = "ACT_MOD_LAYER";
            main.Sequence = JumpSequence;
            return true;
        }

        public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture) => false;
        public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data) => false;
    }

    private sealed class BeforeHl2(bool claims) : ModLayer(Hl2PostureLayer.LayerOrderDefault - 500, claims);
    private sealed class AfterHl2(bool claims) : ModLayer(Hl2PostureLayer.LayerOrderDefault + 500, claims);

    /// <summary>The composition as ServerModule builds it: the container's layers, collected by type.</summary>
    private static AnimStateLayers Composition(params IAnimStateLayer[] layers)
    {
        var container = new ServiceContainer();
        foreach (var layer in layers)
            container.AddInstance(layer.GetType(), layer);
        return new AnimStateLayers(() => container.ResolveAll<IAnimStateLayer>());
    }

    [Fact]
    public void AContainerLayerOrderedBeforeTheHl2LayerDecidesTheServersGait()
    {
        var before = new BeforeHl2(claims: true);
        var (player, engine) = Running(Composition(before));

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        var writes = SkeletonWrites(engine);

        Assert.True(before.MainAsks > 0, "the container's layer was asked on the server's machine");
        Assert.True(writes.SequenceEqual(new[] { JumpSequence }),
            $"its answer, not the HL2 layer's run, reached the server's skeleton ({string.Join(",", writes)})");
    }

    [Fact]
    public void AContainerLayerOrderedAfterTheHl2LayerIsNotAskedWhatTheHl2LayerAnswered()
    {
        var before = new BeforeHl2(claims: false);
        var after = new AfterHl2(claims: true);
        var (player, engine) = Running(Composition(after, before));

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        var writes = SkeletonWrites(engine);

        Assert.True(before.MainAsks == 2, $"the declining layer under the HL2 layer is asked once per drive ({before.MainAsks})");
        Assert.True(after.MainAsks == 0, $"the layer above the HL2 layer is never reached: HL2 answered first ({after.MainAsks})");
        Assert.True(writes.SequenceEqual(new[] { RunSequence }),
            $"so the HL2 layer's run is the server's gait ({string.Join(",", writes)})");
    }

    /// <summary>A layer that asks nothing and claims nothing, only reading the target it is offered.</summary>
    private sealed class IndexProbe : IAnimStateLayer
    {
        public int SawEntityIndex = int.MinValue;

        public int LayerOrder => Hl2PostureLayer.LayerOrderDefault - 400;

        public bool TryOverrideMainSequence(AnimStateContext context, AnimMainSequence main)
        {
            SawEntityIndex = context.Target.EntityIndex;
            return false;
        }

        public bool TryOverrideGesture(AnimStateContext context, AnimGesture gesture) => false;
        public bool TryHandleAnimEvent(AnimStateContext context, PlayerAnimEvent animEvent, int data) => false;
    }

    [Fact]
    public void TheTargetsEntityIndexIsTheEnginesAnswerForItsHandle()
    {
        // The TF2 layer reaches the mod's per-player data through the target
        // it is offered (ITFPlayerData.Networked(int), the active-weapon seam's
        // ActiveWeapon(int)), so the target has to name its index: the engine's
        // GetIndex of the handle the target was built with.
        var probe = new IndexProbe();
        var (player, engine) = Running(Composition(probe));
        engine.Returns("Entities.GetIndex", 7);

        player.UpdateAnimState(10f);
        player.UpdateAnimState(10.015f);

        Assert.Equal(7, probe.SawEntityIndex);
    }

    [Fact]
    public void WithNoLayerRegisteredTheServersChainIsTheHl2LayerAlone()
    {
        var (player, engine) = Running(Composition());
        var (bare, bareEngine) = Running();

        for (int tick = 0; tick < 3; tick++)
        {
            player.UpdateAnimState(10f + tick * 0.015f);
            bare.UpdateAnimState(10f + tick * 0.015f);
        }

        var layers = player.AnimStateOrNull!.Layers;

        Assert.True(layers.Count == 1 && layers[0] is Hl2PostureLayer,
            $"an empty container composes the HL2 layer alone ({string.Join(",", layers.Select(l => l.GetType().Name))})");
        Assert.True(SkeletonWrites(engine).SequenceEqual(new[] { RunSequence }),
            $"which runs the gait the HL2 layer chose ({string.Join(",", SkeletonWrites(engine))})");
        Assert.True(SkeletonWrites(engine).SequenceEqual(SkeletonWrites(bareEngine)),
            "exactly what a player without the composition is handed");
    }
}
