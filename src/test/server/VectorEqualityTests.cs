using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>Vector compares by value with no reflection: a record struct holding one asks it per frame.</summary>
public class VectorEqualityTests
{
    [Fact]
    public void Equal_components_are_equal() => Assert.True(new Vector(1f, 2f, 3f).Equals(new Vector(1f, 2f, 3f)));

    [Theory]
    [InlineData(9f, 2f, 3f)]
    [InlineData(1f, 9f, 3f)]
    [InlineData(1f, 2f, 9f)]
    public void Any_different_component_is_not_equal(float x, float y, float z)
        => Assert.False(new Vector(1f, 2f, 3f).Equals(new Vector(x, y, z)));

    [Fact]
    public void Nan_equals_nan_and_zero_equals_negative_zero_as_before()
    {
        Assert.True(new Vector(float.NaN, 0f, 0f).Equals(new Vector(float.NaN, 0f, 0f)));
        Assert.True(new Vector(0f, 0f, 0f).Equals(new Vector(-0f, 0f, 0f)));
    }

    [Fact]
    public void Equal_vectors_hash_alike()
        => Assert.Equal(new Vector(1f, 2f, 3f).GetHashCode(), new Vector(1f, 2f, 3f).GetHashCode());

    [Fact]
    public void A_comparison_allocates_nothing()
    {
        var state = new FogState(true, true, false, false, new Vector(1f, 2f, 3f), 1, 2, 3f, 4f, 5f, 6f);
        var other = state with { Start = 9f };
        // The first comparison builds the runtime's comparers.
        _ = state.Equals(other);

        long before = GC.GetAllocatedBytesForCurrentThread();
        bool same = state.Equals(other);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.False(same);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Every_generated_payload_s_key_is_the_one_its_handler_registers_under()
    {
        var payloads = typeof(SourceSharp.Server.ServerModule).Assembly.GetTypes()
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessagePayload<>)))
            .ToList();
        Assert.True(payloads.Count > 5, $"{payloads.Count} payload(s) were read");

        foreach (Type payload in payloads)
        {
            var map = payload.GetInterfaceMap(typeof(IUserMessagePayload<>).MakeGenericType(payload));
            object? Read(string name) => map.TargetMethods[Array.FindIndex(map.InterfaceMethods, m => m.Name == "get_" + name)].Invoke(null, null);

            Assert.Equal(UserMessageKey.Of((Type)Read("Handler")!), Read("Key"));
        }
    }
}
