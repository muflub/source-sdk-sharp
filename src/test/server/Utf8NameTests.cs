using System.Text;
using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Guards the game-event name cache.
///
/// The point of it is that a repeated name costs no allocation, so that is
/// tested directly rather than inferred from the code shape — an implementation
/// that quietly decoded every time would pass every correctness check here and
/// still be the thing this class exists to avoid.
///
/// <para>
/// Each fact gets its own cache (xUnit builds the class per fact), so the count
/// a fact asserts is the count it built. The bulk fact this replaces shared one
/// process-wide cache and relied on running its assertions in written order.
/// </para>
/// </summary>
public unsafe class Utf8NameTests
{
    private static readonly string[] Three = { "player_death", "player_hurt", "round_start" };

    private readonly Utf8NameCache _names = new();

    /// <summary>Pushes <paramref name="names"/> through this fact's cache.</summary>
    private void Warm(params string[] names)
    {
        foreach (string name in names)
        {
            var bytes = Encoding.UTF8.GetBytes(name + "\0");
            fixed (byte* p = bytes)
                _names.Get(p);
        }
    }

    private string Decode(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes)
            return _names.Get(p);
    }

    //-------------------------------------------------------------------------
    // Decoding agrees with the path it replaced.
    //-------------------------------------------------------------------------

    [Theory]
    [InlineData("player_death")]
    [InlineData("player_hurt")]
    [InlineData("round_start")]
    public void ANameDecodesToItself(string name)
    {
        Assert.True(Decode(name) == name, $"'{name}' decodes to itself");
    }

    [Fact]
    public void ThreeDistinctNamesCacheThreeEntries()
    {
        Warm(Three);

        Assert.True(_names.Count == 3, "three distinct names cached three entries");
    }

    //-------------------------------------------------------------------------
    // A repeated name is the SAME instance, which is what proves no allocation
    // happened on the second call.
    //-------------------------------------------------------------------------

    [Fact]
    public void ARepeatedNameReturnsTheSameInstance()
    {
        Warm(Three);

        var bytes = Encoding.UTF8.GetBytes("player_death\0");
        string first, second;
        fixed (byte* p = bytes) first = _names.Get(p);
        fixed (byte* p = bytes) second = _names.Get(p);

        Assert.True(ReferenceEquals(first, second), "a repeated name returns the same instance");
    }

    [Fact]
    public void ARepeatedNameAddsNoCacheEntry()
    {
        Warm(Three);

        _ = Decode("player_death");

        Assert.True(_names.Count == 3, "a repeated name adds no cache entry");
    }

    [Fact]
    public void AThousandRepeatLookupsAllocateNothing()
    {
        // Directly: the allocation counter must not move.
        Warm(Three);

        var bytes = Encoding.UTF8.GetBytes("player_hurt\0");

        fixed (byte* p = bytes)
        {
            _names.Get(p);   // warm, so the decode is already paid

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
                _names.Get(p);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.True(after == before, $"1000 repeat lookups allocate nothing ({after - before} bytes)");
        }
    }

    //-------------------------------------------------------------------------
    // Past the cap the class is allowed to allocate; it is not allowed to be
    // wrong.
    //-------------------------------------------------------------------------

    [Fact]
    public void NamesPastTheCacheCapStillDecodeCorrectly()
    {
        // THE ASSERTION USED TO BE `Assert.True(true, ...)` after a loop that
        // called Assert.Fail and returned. That works, but it reads as a check
        // that cannot fail and it was one of the sites
        // tools/vacuous-assert-audit ratchets. The count is the same test
        // written so the thing being asserted is visible.
        int wrong = 0;

        for (int i = 0; i < 200; i++)
        {
            if (Decode($"filler_event_{i}") != $"filler_event_{i}")
                wrong++;
        }

        Assert.True(wrong == 0, $"names past the cache cap still decode correctly ({wrong} of 200 wrong)");
    }

    [Fact]
    public void TheCacheStopsGrowingAtItsCap()
    {
        for (int i = 0; i < 200; i++)
            _ = Decode($"filler_event_{i}");

        Assert.True(_names.Count <= 64, "the cache stops growing at its cap");
    }

    //-------------------------------------------------------------------------
    // Degenerate input matches Engine.FromUtf8's contract.
    //-------------------------------------------------------------------------

    [Fact]
    public void ANullNameIsEmpty()
    {
        Assert.True(_names.Get((byte*)null).Length == 0, "a null name is empty");
    }

    [Fact]
    public void AnEmptyNameIsEmpty()
    {
        var empty = new byte[] { 0 };
        fixed (byte* p = empty)
            Assert.True(_names.Get(p).Length == 0, "an empty name is empty");
    }

    [Fact]
    public void AMultiByteNameDecodesCorrectly()
    {
        // Multi-byte UTF-8 survives the byte-wise comparison.
        Assert.True(Decode("café_événement") == "café_événement", "a multi-byte name decodes correctly");
    }

    [Fact]
    public void APrefixAndItsExtensionStayDistinct()
    {
        // Names that share a prefix must not collide.
        string a = Decode("player");
        string b = Decode("player_death");

        Assert.True(a == "player" && b == "player_death", "a prefix and its extension stay distinct");
    }

    //-------------------------------------------------------------------------
    // An instance is its owner's: a binding's cache shares nothing with another.
    //-------------------------------------------------------------------------

    [Fact]
    public void TwoCachesShareNoEntries()
    {
        Warm(Three);
        var other = new Utf8NameCache();

        var bytes = Encoding.UTF8.GetBytes("player_death\0");
        fixed (byte* p = bytes) other.Get(p);

        Assert.True(_names.Count == 3 && other.Count == 1, $"each cache counts its own names ({_names.Count}, {other.Count})");
    }
}
