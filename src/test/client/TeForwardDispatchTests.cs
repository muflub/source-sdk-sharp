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
    /// <c>TE_BloodStream( filter, delay, org, dir, r, g, b, a, amount )</c>,
    /// with every number different so a transposition cannot pass.
    ///
    /// <para>
    /// IT WAS TE_Bubbles UNTIL lane M1-C, TE_Explosion until plan_server_native_v2
    /// T4d, and TE_DynamicLight before plan_shim.md phase 1f: each time the id
    /// went with its last native sender, so the subject moved to one that
    /// survives (the server's shell_te_forwards.cpp sends this one). It has the
    /// shapes the fact was written to catch: a Vector at the head of the floats
    /// and a second straight after it (so a float index off by one is visible),
    /// and five ints whose order is the colour's.
    /// </para>
    /// </summary>
    [Fact]
    public void BloodStreamArgumentsLandOnTheFieldsItsSignatureNames()
    {
        float[] f = [11f, 22f, 33f, 44f, 55f, 66f];
        int[]   i = [/* r */ 101, /* g */ 102, /* b */ 103, /* a */ 104, /* amount */ 105];

        var state = TeForwardDispatch.Build(TeForward.BloodStream, f, i) as TeBloodStream;

        Assert.True(state is not null,
            "TeForward.BloodStream builds a TeBloodStream; a null here is a forward that "
          + "reached C# and produced nothing");

        Assert.True(state!.Origin.X == 11f && state.Origin.Y == 22f && state.Origin.Z == 33f,
            $"the first three floats are the origin, in order; they came out {state.Origin}");

        Assert.True(state.Direction.X == 44f && state.Direction.Y == 55f && state.Direction.Z == 66f,
            $"then the direction, which is the read that a float index off by one moves; it "
          + $"came out {state.Direction}");

        Assert.True(state.Color == new Color32(101, 102, 103, 104),
            $"the first four ints are r, g, b and a, not {state.Color}");

        Assert.True(state.Amount == 105, $"the fifth int is the amount, not {state.Amount}");
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
        Assert.True(TeForwardDispatch.Build(TeForward.BloodStream, [1f, 2f], [1, 2, 3, 4, 5]) is null,
            "two floats where six are wanted builds nothing");

        Assert.True(TeForwardDispatch.Build(TeForward.BloodStream,
                                            [1f, 2f, 3f, 4f, 5f, 6f], [1, 2, 3, 4]) is null,
            "and four ints where five are wanted");
    }

    /// <summary>
    /// The class the dispatcher's state resolves to is the one the forward was
    /// forwarding — which is what the client looks its player up by and what the
    /// server sends the blob as.
    /// </summary>
    [Fact]
    public void TheStateResolvesToTheWireClassTheForwardMeant()
    {
        var state = TeForwardDispatch.Build(TeForward.BloodStream,
                                            [0f, 0f, 0f, 1f, 1f, 1f], [1, 1, 1, 1, 1])!;

        Assert.True(TempEntityAttribute.ClassNameOf(state.GetType()) == "CTEBloodStream",
            "TE_BloodStream forwards CTEBloodStream, which is the name both sides pair on and "
          + "the one ClientTempEnts.PlayLocally looks the player up by");
    }

    /// <summary>
    /// A RESERVED id builds nothing: TE_BSPDecal and TE_ProjectDecal lost their
    /// last native sender at plan_server_native_v2 T4f (the managed
    /// EffectsOverTempEnts sends those temp entities itself), so their arms went.
    /// </summary>
    [Fact]
    public void TheIdsT4fReservedBuildNothing()
    {
        float[] f = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
        int[]   i = [1, 2, 3];

        Assert.Null(TeForwardDispatch.Build(TeForward.BSPDecal, f, i));
        Assert.Null(TeForwardDispatch.Build(TeForward.ProjectDecal, f, i));
    }

    /// <summary>
    /// TE_Bubbles is RESERVED since lane M1-C: the client's forward was its last
    /// native sender and nothing on the client link line called it, so the arm
    /// went with it and the id builds nothing.
    /// </summary>
    [Fact]
    public void TheBubblesIdBuildsNothing()
    {
        Assert.Null(TeForwardDispatch.Build(TeForward.Bubbles, [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f], [1, 2]));
    }
}
