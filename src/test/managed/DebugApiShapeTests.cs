using System.Reflection;

using Xunit;

using ClientSide = Debug.Api.Client.Interfaces;
using ServerSide = Debug.Api.Server.Interfaces;

namespace SourceSharp.Tests;

/// <summary>
/// The user's Q2 ruling (plan_devapi_interfaces §5.8): the small records both
/// Interfaces need are duplicated, and this holds each pair's shapes equal,
/// field by field, so the copies cannot drift.
/// </summary>
public class DebugApiShapeTests
{
    public static TheoryData<Type, Type> Pairs => new()
    {
        { typeof(ServerSide.DebugBuildHalf), typeof(ClientSide.DebugBuildHalf) },
        { typeof(ServerSide.DebugBuildInfo), typeof(ClientSide.DebugBuildInfo) },
        { typeof(ServerSide.DebugCount), typeof(ClientSide.DebugCount) },
        { typeof(ServerSide.DebugCrossings), typeof(ClientSide.DebugCrossings) },
    };

    /// <summary>
    /// A type's shape: its kind and each public property's name and type, in
    /// declaration order, with the two Interfaces' namespaces read as one.
    /// </summary>
    internal static string[] Shape(Type t)
    {
        static string Name(Type x) => x.IsGenericType
            ? $"{x.Name}<{string.Join(",", x.GetGenericArguments().Select(Name))}>"
            : x.Name;

        var kind = (t.IsValueType ? "struct" : "class") + (t.IsSealed ? " sealed" : "");
        return [kind, .. t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.Name != "EqualityContract")
                         .OrderBy(p => p.MetadataToken)
                         .Select(p => $"{p.Name}:{Name(p.PropertyType)}")];
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void The_two_copies_have_one_shape(Type server, Type client)
    {
        Assert.NotEqual(server.Assembly, client.Assembly);
        Assert.Equal(Shape(server), Shape(client));
    }

    [Fact]
    public void The_shape_sees_a_renamed_field()
    {
        // The stimulus reaches the subject: two records differing in one name.
        Assert.NotEqual(Shape(typeof(A)), Shape(typeof(B)));
        Assert.True(Shape(typeof(ServerSide.DebugBuildHalf)).Length > 5);
    }

    private sealed record A(int Hash, string Path);

    private sealed record B(int Hash, string Paths);
}
