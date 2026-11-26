using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using SourceSharp.Shared.Bones;
using SourceSharp.Tests;

using Xunit;

using NVector = System.Numerics.Vector3;

namespace TF2.Tests.Combat;

/// <summary>
/// The nine classes' shipped player models (<c>models/player/*.mdl</c> and the
/// animation models they include), posed by the server's skeletons as a still
/// player is posed (his stand sequence for the weapon he holds, the model's
/// default dials), and traced as a hitscan round is: a level line stops on the
/// head box where the head is.
///
/// <para>
/// The Heavy's head stands beside his origin, not over it: the minigun stance
/// puts <c>bip_head</c> some eight units to his left, so a line through the
/// column over his feet passes the head and stops on his chest or nothing (a
/// probe aimed at that column read no Head at any height; aimed at the head it
/// reads Head, live as here). The client draws the head there too
/// (<c>cs_cl_hitbands</c> against <c>cs_sv_hitbands</c>).
/// </para>
/// </summary>
public sealed class TFPlayerHitboxesTests
{
    private const int ModelIndex = 1;
    private static readonly EntityHandle Player = new(0x00010002u);

    /// <summary>Stock's <c>MASK_SHOT</c>.</summary>
    private const int MaskShot = 0x46004003;

    /// <summary>Each class's model and the stand activity of the weapon a fresh spawn holds.</summary>
    private static readonly (string Class, string Model, string Activity)[] Classes =
    [
        ("scout", "models/player/scout.mdl", "ACT_MP_STAND_PRIMARY"),
        ("soldier", "models/player/soldier.mdl", "ACT_MP_STAND_PRIMARY"),
        ("pyro", "models/player/pyro.mdl", "ACT_MP_STAND_PRIMARY"),
        ("demoman", "models/player/demo.mdl", "ACT_MP_STAND_PRIMARY"),
        ("heavyweapons", "models/player/heavy.mdl", "ACT_MP_STAND_PRIMARY"),
        ("engineer", "models/player/engineer.mdl", "ACT_MP_STAND_PRIMARY"),
        ("medic", "models/player/medic.mdl", "ACT_MP_STAND_PRIMARY"),
        ("sniper", "models/player/sniper.mdl", "ACT_MP_STAND_PRIMARY"),
        ("spy", "models/player/spy.mdl", "ACT_MP_STAND_SECONDARY"),
    ];

    private sealed class Blocks : IStandaloneBlocks
    {
        public byte[] Held = [];
        public Span<byte> Block(EntityHandle entity) => entity == Player ? Held : Span<byte>.Empty;
    }

    /// <summary>The shipped model and its includes, by name, as the engine's filesystem hands them.</summary>
    private sealed class Shipped(string model) : IStudioMemory
    {
        public byte[]? ModelImage(int modelIndex) => modelIndex == ModelIndex ? Read(model) : null;
        public byte[]? ModelAnimBlock(int modelIndex, int block) => null;
        public byte[]? IncludeImage(int modelIndex, string includeName) =>
            Read(includeName.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase) ? includeName : includeName + ".mdl");
        public byte[]? IncludeAnimBlock(int modelIndex, string includeName, int block) => null;

        private static byte[] Read(string path) =>
            ShippedContent.ReadModel(ShippedContent.Dir, path)
            ?? throw new InvalidOperationException($"no {path} under {ShippedContent.Dir}");
    }

    private sealed class NoSurfaces : IFwdPhysicsSurfaceProps
    {
        public int ParseSurfaceData(string fileName, string text) => 0;
        public int SurfacePropCount() => 0;
        public string? PropName(int index) => null;
        public int SurfaceIndex(string name) => 0;
        public int GameMaterial(int surfaceProps) => 0;
        public MoveSurface Surface(int surfaceProps) => default;
        public string? ScrapeSound(int surfaceProps, bool smooth) => null;
        public string? BulletImpactSound(int surfaceProps) => null;
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; }
    }

    /// <summary>A still player of the model at the origin facing +x, on the first sequence of the activity.</summary>
    private sealed class Rig
    {
        public Blocks Blocks { get; } = new();
        public ServerSkeletons Skeletons { get; }
        public StudioHdr Hdr { get; }
        public int Sequence { get; }

        public Rig(string model, string activity)
        {
            var models = new StudioModels(new Shipped(model));
            Hdr = models.OpenStudioHdr(ModelIndex) ?? throw new InvalidOperationException($"{model}: StudioHdr refused it");
            Sequence = Enumerable.Range(0, Hdr.NumSequences).FirstOrDefault(i => Hdr.SeqDesc(i).ActivityName == activity, -1);
            Assert.True(Sequence >= 0, $"{model} has a sequence for {activity}");

            EngineBlock block = default;
            block.Head.ModelIndex = ModelIndex;
            block.Client.Sequence = Sequence;
            block.Client.PlaybackRate = 1f;
            Blocks.Held = new byte[EngineBlock.SizeBytes];
            MemoryMarshal.Write(Blocks.Held, in block);

            Skeletons = new ServerSkeletons(Blocks, models, new Clock(), new RecordingLog(), new NoSurfaces());
        }

        /// <summary>Where the posed <c>bip_head</c> stands.</summary>
        public NVector Head()
        {
            int bone = Skeletons.LookupBone(Player, "bip_head");
            Vector at = default;
            Assert.True(bone >= 0 && Skeletons.GetBonePosition(Player, bone, out at), "the model has a posed bip_head");
            return new NVector(at.X, at.Y, at.Z);
        }

        /// <summary>The hit group a level line from in front of him (+x) stops on at a point, or null for none.</summary>
        public HitGroup? Group(NVector through)
        {
            var tr = new BoneTrace();
            var ray = BoneRay.FromPoints(through + new NVector(64f, 0f, 0f), through - new NVector(64f, 0f, 0f));
            bool tested = Skeletons.TestHitboxes(Player, Blocks.Held, ray, MaskShot, tr);
            return tested && tr.Fraction < 1f ? (HitGroup)tr.HitGroup : null;
        }

        public void Pose(string name, float value) =>
            Skeletons.SetPoseParameter(Player, Skeletons.LookupPoseParameter(Player, name), value);
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void A_line_at_the_heavys_drawn_head_reads_the_head_group()
    {
        var rig = new Rig("models/player/heavy.mdl", "ACT_MP_STAND_PRIMARY");
        NVector head = rig.Head();

        Assert.Equal(HitGroup.Head, rig.Group(head));
    }

    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void Every_classs_head_answers_at_its_head()
    {
        var wrong = new List<string>();
        foreach ((string name, string model, string activity) in Classes)
        {
            var rig = new Rig(model, activity);
            NVector head = rig.Head();
            if (rig.Group(head) is not HitGroup.Head)
                wrong.Add($"{name} ({head.X:0.0} {head.Y:0.0} {head.Z:0.0}): {rig.Group(head)?.ToString() ?? "nothing"}");
        }

        Assert.True(wrong.Count == 0, "a line at each class's head bone reads Head: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// The Heavy's head is beside the column over his feet (the r2g probe's
    /// line): at his head's own height that column reads no Head, while the
    /// head is drawn there, eight units to his left.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void The_heavys_head_stands_beside_the_column_over_his_feet()
    {
        var rig = new Rig("models/player/heavy.mdl", "ACT_MP_STAND_PRIMARY");
        NVector head = rig.Head();

        Assert.True(head.Y > 5f, $"the head is to his left ({head.Y:0.0})");
        Assert.NotEqual(HitGroup.Head, rig.Group(new NVector(0f, 0f, head.Z)));
    }

    /// <summary>
    /// A Heavy looking 60 degrees up (body_pitch 60: stock's
    /// ComputePoseParam_AimPitch negates the eye pitch) is posed so by the
    /// server's skeleton: his head moves, and a line at the moved head reads
    /// Head where a line at the level look's head does not.
    /// </summary>
    [NotOnCiFact("reads the shipped TF2 content: the Steam install under bin/<config>/content")]
    public void A_heavy_looking_up_carries_his_head_box_with_the_look()
    {
        var rig = new Rig("models/player/heavy.mdl", "ACT_MP_STAND_PRIMARY");
        NVector level = rig.Head();

        rig.Pose("body_pitch", 60f);
        NVector up = rig.Head();

        Assert.True(NVector.Distance(level, up) > 2f, $"the head moved with the look ({level} -> {up})");
        Assert.Equal(HitGroup.Head, rig.Group(up));
    }
}
