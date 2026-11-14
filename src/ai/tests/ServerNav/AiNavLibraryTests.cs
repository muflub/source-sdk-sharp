using System.Reflection;
using SourceSharp.Ai.Server.Nav;
using SourceSharp.Ai.TF2.Jump;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Ai.Tests.ServerNav;

/// <summary>
/// SourceSharp.Ai.Nav is the navigation an assembly can reference without the
/// AI's services: the module sweeps the reference closure of every assembly it
/// loads (LoadedAssemblies) and registers what it finds there, so a reference
/// to SourceSharp.Ai.Server or SourceSharp.Ai.TF2 put their services into every
/// Descent game. The library therefore carries nothing a sweep reads (no
/// attribute of the engine's assemblies, on a type or a member, and no
/// registrar) and references none of the bindings.
/// </summary>
public sealed class AiNavLibraryTests
{
    private static readonly Assembly Library = typeof(NavFollower).Assembly;

    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Every attribute of an engine assembly on a type of <paramref name="assembly"/> or one of its members, named.</summary>
    private static List<string> SweptMarks(Assembly assembly)
    {
        var engine = new HashSet<Assembly> { typeof(SideServiceAttribute).Assembly, typeof(Vector).Assembly };
        var found = new List<string>();
        foreach (Type type in assembly.GetTypes())
        {
            foreach (CustomAttributeData a in type.GetCustomAttributesData())
            {
                if (engine.Contains(a.AttributeType.Assembly))
                    found.Add($"{type.FullName}: [{a.AttributeType.Name}]");
            }

            foreach (MemberInfo m in type.GetMembers(Declared))
            {
                foreach (CustomAttributeData a in m.GetCustomAttributesData())
                {
                    if (engine.Contains(a.AttributeType.Assembly))
                        found.Add($"{type.FullName}.{m.Name}: [{a.AttributeType.Name}]");
                }
            }

            if (typeof(ISideServiceRegistrar).IsAssignableFrom(type))
                found.Add($"{type.FullName}: a registrar");
        }

        return found;
    }

    [Fact]
    public void TheLibraryHoldsTheServiceFreeNavigation()
    {
        Assert.Equal("SourceSharp.Ai.Nav", Library.GetName().Name);
        Assert.Same(Library, typeof(Nav3dNavSource).Assembly);
        Assert.Same(Library, typeof(NavGraphFromNav3d).Assembly);
        Assert.Same(Library, typeof(NavNumbers).Assembly);
        Assert.Same(Library, typeof(NavHullNavSource).Assembly);
        Assert.Same(Library, typeof(VoxelColumns).Assembly);
        Assert.Same(Library, typeof(VoxelMoveWorld).Assembly);
    }

    [Fact]
    public void NothingInTheLibraryIsReadByASweep()
    {
        // The control: the binding the library came out of carries services.
        Assert.Contains(SweptMarks(typeof(AiNav).Assembly), m => m.Contains("[SideServiceAttribute]"));

        Assert.Empty(SweptMarks(Library));
    }

    [Fact]
    public void TheLibraryReferencesNoBinding()
    {
        string[] names = [.. Library.GetReferencedAssemblies().Select(a => a.Name!)];
        Assert.Contains("SourceSharp.Ai", names);
        Assert.DoesNotContain(names, n => n.StartsWith("SourceSharp.Ai.", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.StartsWith("TF2.", StringComparison.Ordinal));
    }

    /// <summary>
    /// The library is reachable from an assembly that holds no game logic: it references the AI core,
    /// map-tools' reader, the ABI and the server's seams, never SourceSharp.Server (NavNumbers.Stock,
    /// which reads the movement code, is Ai.Server's StockNavNumbers).
    /// </summary>
    [Fact]
    public void TheLibraryReferencesNoGameLogic()
    {
        string[] ours = [.. Library.GetReferencedAssemblies().Select(a => a.Name!).Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard")];
        Assert.Equal(["SourceSharp.Abi", "SourceSharp.Ai", "SourceSharp.MapFormats", "SourceSharp.Server.Interface"], ours.Order(StringComparer.Ordinal));
    }
}
