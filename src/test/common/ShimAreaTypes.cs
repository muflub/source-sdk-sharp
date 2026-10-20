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
    /// <summary>
    /// The forwarder struct an area reads its slots through: the area's class,
    /// or the <c>I*Area</c> interface the registration hands it by (the class
    /// of the same name less the <c>I</c>).
    /// </summary>
    public static Type FwdOf(Type area)
    {
        if (area.IsInterface)
            area = area.Assembly.GetType($"{area.Namespace}.{area.Name[1..]}", throwOnError: true)!;

        // A derived area also takes its base's view of the same table, second.
        var ctor = Assert.Single(area.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));
        Type shimRef = ctor.GetParameters()[0].ParameterType;
        Assert.True(shimRef.IsGenericType, $"{area.Name} is not built from a ShimRef");
        return shimRef.GetGenericArguments()[0];
    }
}
