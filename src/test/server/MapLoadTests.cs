using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="MapLoad"/>, and the lifetime rule it enforces by construction.
///
/// <para>
/// The failure being prevented is a quiet one: the entity lump is engine memory
/// valid for the duration of <c>LevelInit</c> and no longer, and read a frame
/// later — when the engine has reused the buffer — it does not fault. It parses.
/// A map with most of its entities loads and the symptom appears elsewhere.
/// </para>
/// </summary>
public class MapLoadTests
{
    private static readonly byte[] Lump = "worldspawn"u8.ToArray();

    [Fact]
    public void TheMapNameIsCopiedAndKept()
    {
        var load = new MapLoad("de_dust", Lump);

        Assert.True(load.MapName == "de_dust", "a load carries the map it is loading");
    }

    [Fact]
    public void TheEntityLumpIsBorrowedRatherThanCopied()
    {
        byte[] lump = "abc"u8.ToArray();
        var load = new MapLoad("de_dust", lump);

        // The engine reusing its buffer, in miniature. A MapLoad that had copied
        // the lump would still read 'a' here, which is precisely the answer that
        // would make the lifetime rule pointless.
        lump[0] = (byte)'z';

        Assert.True(load.Entities[0] == (byte)'z',
            "the entity lump is a view over the engine's bytes, not a copy of them");
    }

    [Fact]
    public void TheLumpLengthIsWhatTheCallerWasGiven()
    {
        var load = new MapLoad("de_dust", Lump);

        Assert.True(load.Entities.Length == Lump.Length, "and it is the whole lump");
    }

    [Fact]
    public void ALoadWithNoLandmarkIsNotATransition()
    {
        var load = new MapLoad("de_dust", Lump);

        Assert.True(!load.HasLandmark, "a fresh map load carries no landmark");
    }

    [Fact]
    public void ATransitionCarriesItsLandmarkAndTheLevelItCameFrom()
    {
        var load = new MapLoad("d1_town_02", Lump, oldLevel: "d1_town_01", landmark: "town_gate");

        Assert.True(load.HasLandmark && load.OldLevel == "d1_town_01" && load.Landmark == "town_gate",
            "a transition says where it came from and through what");
    }

    /// <summary>
    /// <b>The enforcement itself, and it is a type property rather than a
    /// convention.</b>
    ///
    /// <para>
    /// <c>IsByRefLike</c> is the metadata flag the C# compiler reads to refuse
    /// every way a value could outlive its call: it cannot be boxed, made a field
    /// of an ordinary class or struct, put in a collection, captured by a lambda,
    /// used as a generic argument or held across an <c>await</c>. So this one
    /// assertion stands for all of those refusals — and none of them can be
    /// written down as a test, because a test that tried would not compile.
    /// </para>
    /// </summary>
    [Fact]
    public void MapLoadCannotOutliveItsCallBecauseTheCompilerRefusesToLetIt()
    {
        Assert.True(typeof(MapLoad).IsByRefLike,
            "MapLoad is a ref struct, so storing one is a compile error rather than a review note");
    }

    /// <summary>
    /// The other half of the same rule: the ABI layout must NOT be byref-like,
    /// because it crosses the boundary as a pointer and the bootstrap holds one.
    /// The two types are named apart — <c>MapLoadRaw</c> and <c>MapLoad</c> —
    /// exactly as <c>CGlobalVarsRaw</c> is named apart from <c>Globals</c>.
    /// </summary>
    [Fact]
    public void TheAbiLayoutIsNotARefStructBecauseTheBoundaryHoldsOne()
    {
        Assert.True(!typeof(MapLoadRaw).IsByRefLike,
            "MapLoadRaw is the blittable layout the engine hands across and is not lifetime-restricted");
    }
}
