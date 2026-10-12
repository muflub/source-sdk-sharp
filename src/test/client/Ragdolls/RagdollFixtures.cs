using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Ragdolls;
using SourceSharp.Shared.Bones;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// A three-bone body (pelvis -> spine -> head, plus an unphysical "jaw") and
/// the .phy text a ragdoll of it would carry, for the ragdoll facts.
/// </summary>
internal static class RagdollFixtures
{
    public const int Model = 7;

    public sealed class Log : ILog
    {
        public readonly List<string> Lines = [];
        public void Msg(string message) => Lines.Add(message);
        public void Warning(string message) => Lines.Add(message);
    }

    public static StudioHdr Body(bool sequences = true)
    {
        StudioHdrBuilder b = new() { ModelName = "test/body.mdl" };
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "pelvis", Parent = -1, Flags = BoneLibrary.BoneUsedByAnything, PoseToBone = Translation(0f, 0f, -40f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "spine", Parent = 0, Flags = BoneLibrary.BoneUsedByAnything, Pos = new NVector(0f, 0f, 10f), PoseToBone = Translation(0f, 0f, -50f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "head", Parent = 1, Flags = BoneLibrary.BoneUsedByAnything, Pos = new NVector(0f, 0f, 10f), PoseToBone = Translation(0f, 0f, -60f) });
        b.Bones.Add(new StudioHdrBuilder.BoneDef { Name = "jaw", Parent = 2, Flags = 0, Pos = new NVector(2f, 0f, 0f), PoseToBone = Translation(-2f, 0f, -60f) });
        if (sequences)
        {
            b.Anims.Add(new StudioHdrBuilder.AnimDef { Name = "idle" });
            b.Sequences.Add(new StudioHdrBuilder.SeqDef { Label = "idle", AnimTable = [0], Weights = [1f, 1f, 1f, 1f] });
        }

        return StudioHdr.TryCreate(b.Build(), null, out StudioHdr? hdr) ? hdr! : throw new InvalidOperationException("fixture image rejected");
    }

    public static BoneMatrix3x4 Translation(float x, float y, float z)
    {
        BoneMatrix3x4 m = BoneMatrix3x4.Identity;
        m.Position = new NVector(x, y, z);
        return m;
    }

    /// <summary>The pose: each bone translated straight up, the jaw beside the head.</summary>
    public static BoneMatrix3x4[] Pose() =>
    [
        Translation(0f, 0f, 40f),
        Translation(0f, 0f, 50f),
        Translation(0f, 0f, 60f),
        Translation(2f, 0f, 60f),
    ];

    public static ConstraintRagdollParams Joint(int parent, int child, float torque = 10f)
    {
        ConstraintRagdollParams c = ConstraintRagdollParams.Defaults();
        c.ParentIndex = parent;
        c.ChildIndex = child;
        c.Axis0 = ConstraintAxisLimit.Friction(-10f, 10f, torque);
        c.Axis1 = ConstraintAxisLimit.Friction(-20f, 20f, torque);
        c.Axis2 = ConstraintAxisLimit.Friction(-30f, 30f, torque);
        return c;
    }

    /// <summary>pelvis, spine, head; spine and head jointed to their parents.</summary>
    public static List<FakeRagdollVPhysics.Block> ThreeSolids() =>
    [
        new FakeRagdollVPhysics.SolidBlock(0, "pelvis", "flesh"),
        new FakeRagdollVPhysics.SolidBlock(1, "spine", "flesh"),
        new FakeRagdollVPhysics.SolidBlock(2, "head", "flesh"),
        new FakeRagdollVPhysics.ConstraintBlock(Joint(0, 1)),
        new FakeRagdollVPhysics.ConstraintBlock(Joint(1, 2)),
    ];

    public static RagdollParams Params(StudioHdr hdr, FakeRagdollVPhysics fake) => new()
    {
        GameData = unchecked((nint)0x8000_0000_0000_1235UL),
        Collide = fake.Collide(Model),
        StudioHdr = hdr,
        Name = 0x3300,
        ModelIndex = Model,
        CurrentBones = Pose(),
        ForceBoneIndex = -1,
    };

    public static (RagdollSolver Solver, FakeRagdollVPhysics Fake, Log Log) Solver(int developer = 0)
    {
        var fake = new FakeRagdollVPhysics();
        fake.Surfaces["flesh"] = 5;
        var log = new Log();
        return (new RagdollSolver(fake, log, () => developer), fake, log);
    }

    /// <summary>A body built from <see cref="ThreeSolids"/>.</summary>
    public static (RagdollBody Body, RagdollSolver Solver, FakeRagdollVPhysics Fake) Built()
    {
        var (solver, fake, _) = Solver();
        fake.Blocks = ThreeSolids();
        var body = new RagdollBody();
        if (!solver.Create(body, Params(Body(), fake), fake.Env))
            throw new InvalidOperationException("the fixture body was not built");
        return (body, solver, fake);
    }

    public static Float3x4 At(float x, float y, float z)
    {
        Float3x4 m = Float3x4.Identity;
        m[0, 3] = x;
        m[1, 3] = y;
        m[2, 3] = z;
        return m;
    }
}
