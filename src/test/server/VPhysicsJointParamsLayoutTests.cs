using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed views of vphysics' constraint and spring parameter blocks
/// (plan_physics_bones_native_zero 2.4), one row per field the managed tables
/// write. The same numbers are pinned against vphysics/constraints.h and
/// vphysics_interface.h by vtable_index_agreement_test's ConstraintParamsLayout.
/// </summary>
public class VPhysicsJointParamsLayoutTests
{
    [Theory]
    [InlineData(typeof(ConstraintBreakableParams), 24)]
    [InlineData(typeof(ConstraintAxisLimit), 16)]
    [InlineData(typeof(Float3x4), 48)]
    [InlineData(typeof(ConstraintFixedParams), 72)]
    [InlineData(typeof(ConstraintHingeParams), 64)]
    [InlineData(typeof(ConstraintBallsocketParams), 48)]
    [InlineData(typeof(ConstraintLengthParams), 56)]
    [InlineData(typeof(ConstraintSlidingParams), 100)]
    [InlineData(typeof(ConstraintPulleyParams), 84)]
    [InlineData(typeof(ConstraintRagdollParams), 180)]
    [InlineData(typeof(SpringParams), 44)]
    [InlineData(typeof(ConstraintGroupParams), 12)]
    [InlineData(typeof(ObjectParams), 64)]
    public void SizeIsTheHeaders(Type block, int size) => Assert.Equal(size, Marshal.SizeOf(block));

    [Theory]
    [InlineData(typeof(ConstraintBreakableParams), nameof(ConstraintBreakableParams.ForceLimit), 4)]
    [InlineData(typeof(ConstraintBreakableParams), nameof(ConstraintBreakableParams.TorqueLimit), 8)]
    [InlineData(typeof(ConstraintBreakableParams), nameof(ConstraintBreakableParams.BodyMassScale0), 12)]
    [InlineData(typeof(ConstraintBreakableParams), nameof(ConstraintBreakableParams.IsActive), 20)]
    [InlineData(typeof(ConstraintAxisLimit), nameof(ConstraintAxisLimit.Torque), 12)]
    [InlineData(typeof(ConstraintFixedParams), nameof(ConstraintFixedParams.Constraint), 48)]
    [InlineData(typeof(ConstraintHingeParams), nameof(ConstraintHingeParams.WorldAxisDirection), 12)]
    [InlineData(typeof(ConstraintHingeParams), nameof(ConstraintHingeParams.HingeAxis), 24)]
    [InlineData(typeof(ConstraintHingeParams), nameof(ConstraintHingeParams.Constraint), 40)]
    [InlineData(typeof(ConstraintBallsocketParams), nameof(ConstraintBallsocketParams.Constraint), 24)]
    [InlineData(typeof(ConstraintLengthParams), nameof(ConstraintLengthParams.ObjectPosition0), 24)]
    [InlineData(typeof(ConstraintLengthParams), nameof(ConstraintLengthParams.TotalLength), 48)]
    [InlineData(typeof(ConstraintLengthParams), nameof(ConstraintLengthParams.MinLength), 52)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.SlideAxisRef), 48)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.Constraint), 60)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.LimitMin), 84)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.LimitMax), 88)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.Friction), 92)]
    [InlineData(typeof(ConstraintSlidingParams), nameof(ConstraintSlidingParams.Velocity), 96)]
    [InlineData(typeof(ConstraintPulleyParams), nameof(ConstraintPulleyParams.PulleyPosition0), 24)]
    [InlineData(typeof(ConstraintPulleyParams), nameof(ConstraintPulleyParams.ObjectPosition0), 48)]
    [InlineData(typeof(ConstraintPulleyParams), nameof(ConstraintPulleyParams.TotalLength), 72)]
    [InlineData(typeof(ConstraintPulleyParams), nameof(ConstraintPulleyParams.GearRatio), 76)]
    [InlineData(typeof(ConstraintPulleyParams), nameof(ConstraintPulleyParams.IsRigid), 80)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.ConstraintToReference), 24)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.ConstraintToAttached), 72)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.ParentIndex), 120)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.ChildIndex), 124)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.Axis0), 128)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.OnlyAngularLimits), 176)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.IsActive), 177)]
    [InlineData(typeof(ConstraintRagdollParams), nameof(ConstraintRagdollParams.UseClockwiseRotations), 178)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.NaturalLength), 4)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.Damping), 8)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.RelativeDamping), 12)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.StartPosition), 16)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.EndPosition), 28)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.UseLocalPositions), 40)]
    [InlineData(typeof(SpringParams), nameof(SpringParams.OnlyStretch), 41)]
    [InlineData(typeof(ConstraintGroupParams), nameof(ConstraintGroupParams.MinErrorTicks), 4)]
    [InlineData(typeof(ConstraintGroupParams), nameof(ConstraintGroupParams.ErrorTolerance), 8)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.Mass), 8)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.RotInertiaLimit), 24)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.Name), 32)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.GameData), 40)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.Volume), 48)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.DragCoefficient), 52)]
    [InlineData(typeof(ObjectParams), nameof(ObjectParams.EnableCollisions), 56)]
    public void FieldIsAtTheHeadersOffset(Type block, string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf(block, field));

    [Fact]
    public void GroupDefaultsAreStocks()
    {
        var p = ConstraintGroupParams.Defaults();
        Assert.True(p.AdditionalIterations == 0 && p.MinErrorTicks == 15 && p.ErrorTolerance == 3.0f,
                    "constraint_groupparams_t::Defaults: no extra iterations, 15 error ticks, 3 units");
    }

    [Fact]
    public void ObjectDefaultsAreStocks()
    {
        var p = ObjectParams.Defaults(0x10);
        Assert.True(p.MassCenterOverride == 0 && p.Mass == 1f && p.Inertia == 1f && p.Damping == 0.1f
                    && p.RotDamping == 0.1f && p.RotInertiaLimit == 0.05f && p.Name == 0x10 && p.GameData == 0
                    && p.Volume == 0f && p.DragCoefficient == 1f && p.EnableCollisions == 1,
                    "g_PhysDefaultObjectParams: 1, 1, 0.1, 0.1, 0.05, \"DEFAULT\", no game data, 0, 1, collisions on");
    }

    [Fact]
    public void BreakableDefaultsAreStocks()
    {
        var p = ConstraintBreakableParams.Defaults();
        Assert.True(p.Strength == 1f && p.ForceLimit == 0f && p.TorqueLimit == 0f
                    && p.BodyMassScale0 == 1f && p.BodyMassScale1 == 1f && p.IsActive == 1,
                    "constraint_breakableparams_t::Defaults: strength 1, no limits, mass scales 1, active");
    }

    [Fact]
    public void RagdollDefaultsAreStocks()
    {
        var p = ConstraintRagdollParams.Defaults();
        Assert.True(p.ParentIndex == -1 && p.ChildIndex == -1 && p.IsActive == 1
                    && p.ConstraintToReference[0, 0] == 1f && p.ConstraintToAttached[2, 2] == 1f
                    && p.Constraint.Strength == 1f,
                    "constraint_ragdollparams_t::Defaults: indices -1, active, identity transforms");
    }

    [Fact]
    public void PulleyDefaultsAreStocks()
    {
        var p = ConstraintPulleyParams.Defaults();
        Assert.True(p.TotalLength == 1f && p.GearRatio == 1f && p.IsRigid == 0,
                    "constraint_pulleyparams_t::Defaults: length 1, gear 1, not rigid");
    }

    [Fact]
    public void LengthDefaultsAreStocks()
    {
        var p = ConstraintLengthParams.Defaults();
        Assert.True(p.TotalLength == 1f && p.MinLength == 0f, "constraint_lengthparams_t::Defaults: length 1, min 0");
    }
}
