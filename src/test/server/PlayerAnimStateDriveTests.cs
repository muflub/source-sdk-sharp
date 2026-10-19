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

        var models = new ModelDescriptors(_ => 1, _ => Model, new RecordingLog());
        models.AttachSource(new PlayerModel());

        var player = new SsPlayer(engine, engine, Impacts(engine), Fluid(),
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
