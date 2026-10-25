extern alias client;
using System.Reflection;
using SourceSharp.Abi;
using Xunit;
using client::SourceSharp.Client;
using SourceSharp.Server;
using C = client::SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// One module engine class per side (plan_engineside_removal Phase 1, user
/// ruling 2026-10-25): each side's load makes its own, and each composition
/// root takes its own by constructor, so nothing has to ask at run time which
/// side it was handed.
/// </summary>
public class ModuleEngineSidesTests
{
    private const BindingFlags AnyConstructor = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>
    /// The shared surface is abstract in both interops, and each has exactly
    /// one engine of its own: the server's in the server's assembly, the
    /// client's in the client's.
    /// </summary>
    [Fact]
    public void EachSidesInteropHasOneModuleEngineOfItsOwn()
    {
        var expected = new Dictionary<Assembly, string>
        {
            [typeof(ModuleEngine).Assembly] = "ServerModuleEngine",
            [typeof(C.ModuleEngine).Assembly] = "ClientModuleEngine",
        };

        foreach (var (assembly, name) in expected)
        {
            Type shared = assembly.GetType("SourceSharp.Abi.ModuleEngine", throwOnError: true)!;
            Assert.True(shared.IsAbstract, $"{assembly.GetName().Name}: ModuleEngine is abstract");

            var engines = assembly.GetTypes().Where(t => t.IsSubclassOf(shared)).Select(t => t.Name).ToList();
            Assert.Equal([name], engines);
            Assert.True(assembly.GetType($"SourceSharp.Abi.{name}", throwOnError: true)!.IsSealed);
        }
    }

    /// <summary>
    /// Every constructor of a composition root that takes an engine takes its
    /// own side's class, never the shared base: a root cannot be composed over
    /// the other side's engine, or over one whose side it would have to ask.
    /// </summary>
    [Fact]
    public void EachRootTakesItsOwnSidesEngine()
    {
        AssertTakes(typeof(ServerModule), typeof(ServerModuleEngine), typeof(ModuleEngine));
        AssertTakes(typeof(ClientModule), typeof(C.ClientModuleEngine), typeof(C.ModuleEngine));
    }

    private static void AssertTakes(Type root, Type engine, Type shared)
    {
        var taken = root.GetConstructors(AnyConstructor)
                        .SelectMany(c => c.GetParameters())
                        .Select(p => p.ParameterType)
                        .Where(shared.IsAssignableFrom)
                        .ToList();

        // The positive arm: the walk sees the root's engine parameter at all.
        Assert.NotEmpty(taken);
        Assert.All(taken, t => Assert.Equal(engine, t));
    }

    /// <summary>The server's load makes the server's engine, over the side it loaded.</summary>
    [Fact]
    public void TheServersLoadMakesTheServersEngine()
    {
        EngineSide side = TestSides.Create(Side.Server);

        ModuleEngine engine = SideAttachment.ModuleEngineOver(side);

        Assert.IsType<ServerModuleEngine>(engine);
        Assert.Same(side, engine.Api);
    }

    /// <summary>The client's load makes the client's engine, over the side it loaded.</summary>
    [Fact]
    public void TheClientsLoadMakesTheClientsEngine()
    {
        C.EngineSide side = C.TestSides.Create(Side.Client);

        C.ModuleEngine engine = C.SideAttachment.ModuleEngineOver(side);

        Assert.IsType<C.ClientModuleEngine>(engine);
        Assert.Same(side, engine.Api);
    }

    /// <summary>
    /// No route is a settable property on the side (plan_engineside_removal
    /// Phase 3): what the composition builds late reaches the side's members
    /// through a <c>SideRoute</c> its container registered, so nothing holding
    /// the side can reassign where they go. The positive arm: the walk sees the
    /// side's properties, and the server's routes among them.
    /// </summary>
    [Fact]
    public void TheSideHasNoSettableProperty()
    {
        foreach (Type side in new[] { typeof(EngineSide), typeof(C.EngineSide) })
        {
            PropertyInfo[] properties = side.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                                           | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotEmpty(properties);
            if (side == typeof(EngineSide))   // only the server has late routes
                Assert.Contains(properties, p => p.PropertyType.IsGenericType
                                              && p.PropertyType.GetGenericTypeDefinition().Name == "SideRoute`1");

            var settable = properties.Where(p => p.SetMethod is { IsPrivate: false }).Select(p => p.Name).ToList();
            Assert.True(settable.Count == 0, $"{side.Assembly.GetName().Name}: settable on the side: {string.Join(", ", settable)}");
        }
    }
}
