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
    /// <c>TE_Bubbles( filter, delay, mins, maxs, height, modelindex, count,
    /// speed )</c>, with every number different so a transposition cannot pass.
    ///
    /// <para>
    /// IT WAS TE_Explosion UNTIL plan_server_native_v2 T4d, and TE_DynamicLight
    /// before plan_shim.md phase 1f: each time the id went with its last native
    /// sender, so the subject moved to one that survives. This one has the
    /// shapes the fact was written to catch: a Vector at the head of the floats,
    /// a second Vector straight after it (so a float index off by one is
    /// visible), and floats and ints that interleave in the signature but not
    /// in the arrays.
    /// </para>
    /// </summary>
    [Fact]
    public void BubblesArgumentsLandOnTheFieldsItsSignatureNames()
    {
        float[] f = [11f, 22f, 33f, 44f, 55f, 66f, /* height */ 77f, /* speed */ 88f];
        int[]   i = [/* modelindex */ 101, /* count */ 102];

        var state = TeForwardDispatch.Build(TeForward.Bubbles, f, i) as TeBubbles;

        Assert.True(state is not null,
            "TeForward.Bubbles builds a TeBubbles; a null here is a forward that "
          + "reached C# and produced nothing");

        Assert.True(state!.Mins.X == 11f && state.Mins.Y == 22f && state.Mins.Z == 33f,
            $"the first three floats are the mins, in order; they came out {state.Mins}");

        Assert.True(state.Maxs.X == 44f && state.Maxs.Y == 55f && state.Maxs.Z == 66f,
            $"then the maxs, which is the read that a float index off by one moves; it "
          + $"came out {state.Maxs}");

        Assert.True(state.Height == 77f && state.Speed == 88f,
            $"then height and speed, the two floats the ints sit between in the "
          + $"signature; they came out [{state.Height}, {state.Speed}]");

        Assert.True(state.ModelIndex == 101 && state.Count == 102,
            $"the ints are modelindex then count, not [{state.ModelIndex}, {state.Count}]");
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
        Assert.True(TeForwardDispatch.Build(TeForward.Bubbles, [1f, 2f], [1, 2]) is null,
            "two floats where eight are wanted builds nothing");

        Assert.True(TeForwardDispatch.Build(TeForward.Bubbles,
                                            [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f], [1]) is null,
            "and one int where two are wanted");
    }

    /// <summary>
    /// The class the dispatcher's state resolves to is the one the forward was
    /// forwarding — which is what the client looks its player up by and what the
    /// server sends the blob as.
    /// </summary>
    [Fact]
    public void TheStateResolvesToTheWireClassTheForwardMeant()
    {
        var state = TeForwardDispatch.Build(TeForward.Bubbles,
                                            [0f, 0f, 0f, 1f, 1f, 1f, 1f, 1f], [1, 1])!;

        Assert.True(TempEntityAttribute.ClassNameOf(state.GetType()) == "CTEBubbles",
            "TE_Bubbles forwards CTEBubbles, which is the name both sides pair on and "
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
}
