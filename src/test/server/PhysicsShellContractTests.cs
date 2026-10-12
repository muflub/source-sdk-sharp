using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE TWO THINGS <c>plan_entity.md</c> §7 DECIDES ABOUT PHYSICS, AND THE
/// PROPERTY THAT MAKES A DECISION DIFFERENT FROM AN OMISSION.
///
/// <para>
/// Step 3a's paragraph said the <c>IMotionEvent</c> refusal "belongs in
/// <see cref="ContractBaseline"/> with a reason, never as a silent no-op" (both
/// of §7's physics refusals are lifted since 2026-10-11). The remaining facts
/// are what stops an entry rotting into decoration: a refusal with no
/// reason, or a reason with no citation, is the state the entry exists to
/// prevent — somebody a year from now finding the behaviour missing, assuming it
/// is a bug, and rediscovering the argument the hard way.
/// </para>
///
/// <para>
/// They are deliberately NOT a test of the physics code. What the shell does is
/// pinned by <c>sourcesharp/test/physics_shell_test.cpp</c> on the native leg,
/// because it is arithmetic over PODs with no managed side to it. What is
/// checked here is that the DECISION is still written down.
/// </para>
/// </summary>
public class PhysicsShellContractTests
{
    /// <summary>
    /// The IMotionEvent refusal of plan_entity.md §7 ("a managed controller
    /// would have to cross the boundary from inside the step") is LIFTED: the
    /// user's ruling of 2026-10-11 put all physics logic in managed, and
    /// vphysics asks managed's own IMotionEvent table mid-step
    /// (plan_physics_bones_native_zero 2.2/2.4). The entry and its four facts
    /// retire; this one holds the lift, and MotionSimulationTests /
    /// PhysicsMotionControllersTests hold what answers now.
    /// </summary>
    [Fact]
    public void TheMotionControllerRefusalIsLifted()
    {
        Assert.NotEmpty(ContractBaseline.Refusals);
        Assert.DoesNotContain(ContractBaseline.Refusals, r => r.Subject.Contains("IMotionEvent"));
    }

    /// <summary>
    /// The ShouldCollide refusal of plan_entity.md §7 ("no path from it back
    /// out to managed code") is LIFTED: the user's ruling of 2026-10-11 put all
    /// physics logic in managed, and vphysics asks managed's own
    /// IPhysicsCollisionSolver table (plan_physics_bones_native_zero 2.1).
    /// The entry and its two facts retire; this one holds the lift, and
    /// PhysicsSolverTests holds what answers now.
    /// </summary>
    [Fact]
    public void TheShouldCollideRefusalIsLifted()
    {
        Assert.NotEmpty(ContractBaseline.Refusals);
        Assert.DoesNotContain(ContractBaseline.Refusals, r => r.Subject.Contains("ShouldCollide"));
    }

    [Fact]
    public void EveryRefusalCitesThePlanSectionThatDecidedIt()
    {
        Assert.NotEmpty(ContractBaseline.Refusals);
        foreach (var refusal in ContractBaseline.Refusals)
        {
            Assert.Contains("plan_entity.md", refusal.Citation);
        }
    }

    [Fact]
    public void EveryRefusalCarriesASubjectAndAReason()
    {
        foreach (var refusal in ContractBaseline.Refusals)
        {
            // A blank reason is the silent no-op wearing a record's clothes.
            Assert.False(string.IsNullOrWhiteSpace(refusal.Subject));
            Assert.False(string.IsNullOrWhiteSpace(refusal.Reason));
            Assert.True(refusal.Reason.Length > 80,
                $"the reason for '{refusal.Subject}' is too short to be an argument");
        }
    }
}
