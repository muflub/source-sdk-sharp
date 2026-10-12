using SourceSharp.Abi;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TeForwardDispatch"/> — the logic that used to be in
/// <c>shell_te_forwards*.cpp</c>.
///
/// <para>
/// A forward knew three things C++ had no business knowing: which temp entity
/// class it was, which argument became which field, and what the field was
/// called. All three are here now, and what crosses the boundary is an id and
/// the numbers the C++ signature already had.
/// </para>
///
/// <para>
/// THE ARGUMENTS ARRIVE POSITIONALLY, which is the one thing this gives up
/// against one typed ABI slot per forward: a value in the wrong index is a wrong
/// value rather than a compile error. So the tests below are about POSITION —
/// every field distinct, and each asserted against the slot it came from.
/// </para>
/// </summary>
public class TeForwardDispatchTests
{
    /// <summary>
    /// <c>TE_Explosion( filter, delay, pos, modelindex, scale, framerate, flags,
    /// radius, magnitude, normal, materialType )</c>, with every number
    /// different so a transposition cannot pass.
    ///
    /// <para>
    /// IT WAS TE_DynamicLight UNTIL plan_shim.md PHASE 1f. That id went with
    /// the client forwarder that was its only sender, so the subject moved to
    /// one that survives and has the same shapes this fact was written to
    /// catch: a Vector at the head of the floats, a second Vector further in
    /// (so a float index off by one is visible), a float read between two ints,
    /// and one int narrowed on the way into the field.
    /// </para>
    /// </summary>
    [Fact]
    public void AnExplosionsArgumentsLandOnTheFieldsItsSignatureNames()
    {
        float[] f = [11f, 22f, 33f, /* scale */ 44f, /* normal */ 55f, 66f, 77f];
        int[]   i = [/* modelindex */ 101, /* framerate */ 102, /* flags */ 103,
                     /* materialType */ 'C', /* radius */ 104, /* magnitude */ 105];

        var state = TeForwardDispatch.Build(TeForward.Explosion, f, i) as TeExplosion;

        Assert.True(state is not null,
            "TeForward.Explosion builds a TeExplosion; a null here is a forward that "
          + "reached C# and produced nothing");

        Assert.True(state!.Origin.X == 11f && state.Origin.Y == 22f && state.Origin.Z == 33f,
            $"the first three floats are the origin, in order; it came out {state.Origin}");

        Assert.True(state.Scale == 44f, $"then scale, not {state.Scale}");

        Assert.True(state.Normal.X == 55f && state.Normal.Y == 66f && state.Normal.Z == 77f,
            $"then the normal, which is the read that a float index off by one moves; it "
          + $"came out {state.Normal}");

        Assert.True(state.ModelIndex == 101 && state.FrameRate == 102 && state.Flags == 103,
            $"the ints open modelindex, framerate, flags; they came out "
          + $"[{state.ModelIndex}, {state.FrameRate}, {state.Flags}]");

        // NARROWED ON THE WAY IN, and that is the declaration's width rather
        // than anything this arm remembered: materialType is one byte on the
        // wire and 'C' is ITempEntsSystem::Explosion's own declared default.
        Assert.True(state.MaterialType == (byte)'C',
            $"materialType survives the narrowing to one byte: 'C' came back as "
          + $"{state.MaterialType}");

        Assert.True(state.Radius == 104 && state.Magnitude == 105,
            $"and radius then magnitude LAST, not [{state.Radius}, {state.Magnitude}]");
    }

    /// <summary>
    /// AN ID NOTHING IS WRITTEN FOR IS A NULL, NOT A THROW. Native sends nothing
    /// on a refusal; an exception here would cross back through an
    /// <c>[UnmanagedCallersOnly]</c> boundary in the middle of the engine's
    /// event loop, which is exactly when this path is busiest.
    /// </summary>
    [Fact]
    public void AnIdWithNoArmIsRefusedRatherThanThrowing()
    {
        var state = TeForwardDispatch.Build((TeForward)9999, [1f], [1]);

        Assert.True(state is null, "an unknown id builds nothing");
    }

    /// <summary>
    /// AND SO IS A SHORT ARGUMENT LIST. Native marshals from a signature that
    /// cannot be short — but the boundary is a pointer and a count, and a count
    /// that disagrees with the arm would otherwise read off the end of the span.
    /// </summary>
    [Fact]
    public void TooFewArgumentsAreRefusedRatherThanReadPastTheEnd()
    {
        Assert.True(TeForwardDispatch.Build(TeForward.Explosion, [1f, 2f], [1, 2, 3, 4, 5, 6]) is null,
            "two floats where seven are wanted builds nothing");

        Assert.True(TeForwardDispatch.Build(TeForward.Explosion,
                                            [1f, 2f, 3f, 4f, 5f, 6f, 7f], [1]) is null,
            "and one int where six are wanted");
    }

    /// <summary>
    /// The class the dispatcher's state resolves to is the one the forward was
    /// forwarding — which is what the client looks its player up by and what the
    /// server sends the blob as.
    /// </summary>
    [Fact]
    public void TheStateResolvesToTheWireClassTheForwardMeant()
    {
        var state = TeForwardDispatch.Build(TeForward.Explosion,
                                            [0f, 0f, 0f, 1f, 1f, 1f, 1f], [1, 1, 1, 1, 1, 1])!;

        Assert.True(TempEntityAttribute.ClassNameOf(state.GetType()) == "CTEExplosion",
            "TE_Explosion forwards CTEExplosion, which is the name both sides pair on and "
          + "the one ClientTempEnts.PlayLocally looks the player up by");
    }
}
