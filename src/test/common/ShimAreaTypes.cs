using System.Reflection;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a generated shim area class is over. Each area class
/// (plan_shim_areas_split phase 2) takes its acquired <c>ShimRef&lt;TFwd&gt;</c>
/// as its one constructor argument, so the forwarder struct a container field
/// is served through is read from that, and a test cannot drift from the
/// generator by naming the struct itself.
/// </summary>
internal static class ShimAreaTypes
{
    /// <summary>The forwarder struct an area class (a container field's type) reads its slots through.</summary>
    public static Type FwdOf(Type area)
    {
        var ctor = Assert.Single(area.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Type shimRef = Assert.Single(ctor.GetParameters()).ParameterType;
        Assert.True(shimRef.IsGenericType, $"{area.Name} is not built from a ShimRef");
        return shimRef.GetGenericArguments()[0];
    }
}
