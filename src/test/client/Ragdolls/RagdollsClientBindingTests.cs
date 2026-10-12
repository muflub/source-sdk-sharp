using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests.Ragdolls;

/// <summary>
/// IManagedRagdollsClient as RagdollsClientBinding answers it: each call counted
/// and handed to the ragdolls, 0 with none composed, and a throw logged rather
/// than carried back into the physics step.
/// </summary>
public class RagdollsClientBindingTests
{
    private sealed class Step : IClientRagdollStep
    {
        public int Stepped;
        public int Destroyed;
        public bool Throw;

        public int StepPoses()
        {
            if (Throw)
                throw new InvalidOperationException("boom");
            return ++Stepped;
        }

        public int DestroyAll() => ++Destroyed;
    }

    [Fact]
    public void TheStepIsCountedAndHandedOver()
    {
        var crossings = new Crossings();
        var step = new Step();
        var binding = new RagdollsClientBinding(crossings, new RagdollFixtures.Log(), () => step);

        Assert.Equal(1, binding.StepPoses());
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRagdollsStepPoses));
    }

    [Fact]
    public void TheTeardownIsCountedAndHandedOver()
    {
        var crossings = new Crossings();
        var step = new Step();
        var binding = new RagdollsClientBinding(crossings, new RagdollFixtures.Log(), () => step);

        Assert.Equal(1, binding.DestroyAll());
        Assert.Equal(1, crossings.Read(CrossingOp.ClientRagdollsDestroyAll));
    }

    [Fact]
    public void WithNoRagdollsComposedBothAnswerZero()
    {
        var binding = new RagdollsClientBinding(new Crossings(), new RagdollFixtures.Log(), () => null);

        Assert.Equal((0, 0), (binding.StepPoses(), binding.DestroyAll()));
    }

    [Fact]
    public void AThrowIsLoggedAndAnswersZero()
    {
        var log = new RagdollFixtures.Log();
        var binding = new RagdollsClientBinding(new Crossings(), log, () => new Step { Throw = true });

        Assert.Equal(0, binding.StepPoses());
        Assert.Contains(log.Lines, l => l.Contains("the ragdoll step threw: boom"));
    }
}
