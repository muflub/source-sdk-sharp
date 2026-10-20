extern alias client;
using System.Reflection;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 11: the shim areas are handed to the code that
/// calls them, and nothing holds them as a set. The containers (<c>ShimAreas</c>,
/// <c>ClientShimAreas</c>) were a class whose members were the areas, which
/// every binding reached through its side; these facts keep them, and anything
/// shaped like them, from coming back.
///
/// <para>
/// Read from the built assemblies by reflection, both sides' interop and the
/// Abi, so a new area, binding or adapter is covered the day it is written.
/// An area is a class of the Abi built over a <c>ShimRef&lt;TFwd&gt;</c>,
/// which is how <c>tools/shim-areas</c> writes every one.
/// </para>
/// </summary>
public class ShimAreaInjectionCensusTests
{
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static Assembly Abi => typeof(ShimRoot).Assembly;

    private static Assembly[] Interops =>
        [typeof(EngineSide).Assembly, typeof(client::SourceSharp.Abi.EngineSide).Assembly];

    /// <summary>
    /// Whether <paramref name="type"/> is a generated area: its class, or the
    /// <c>I*Area</c> interface consumers hold it by (phase 11 review).
    /// </summary>
    private static bool IsArea(Type type)
        => type.Assembly == Abi
        && (IsAreaClass(type)
            || type.IsInterface && type.Name.StartsWith('I')
               && Abi.GetType($"{type.Namespace}.{type.Name[1..]}") is { } cls && IsAreaClass(cls));

    private static bool IsAreaClass(Type type)
        => type.IsClass
        && type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
               .Any(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition().Name == "ShimRef`1");

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    /// <summary>
    /// No type exposes an area: every field or property whose type is an area
    /// is private. A container is exactly a type that hands its areas out, so
    /// this is the rule that keeps one from being written again, by any name.
    /// The positive arm is the bindings' own: they hold more than a hundred
    /// areas between them, each private, each given by constructor.
    /// </summary>
    [Fact]
    public void NoTypeHandsOutAnArea()
    {
        var exposed = new List<string>();
        int held = 0;

        // A closure's captured locals are the compiler's public fields on a
        // private class nothing can name: not a way to hand an area out.
        foreach (Type type in Interops.Prepend(Abi).SelectMany(Types)
                                      .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute))))
        {
            foreach (FieldInfo field in type.GetFields(Declared).Where(f => IsArea(f.FieldType)))
            {
                if (field.IsPrivate)
                    held++;
                else
                    exposed.Add($"{type.Assembly.GetName().Name}: {type.FullName}.{field.Name}");
            }

            foreach (PropertyInfo property in type.GetProperties(Declared).Where(p => IsArea(p.PropertyType)))
            {
                bool hidden = property.GetAccessors(nonPublic: true).All(a => a.IsPrivate);
                if (!hidden)
                    exposed.Add($"{type.Assembly.GetName().Name}: {type.FullName}.{property.Name}");
            }
        }

        Assert.True(held > 100, $"{held} private area fields: the walk reaches the bindings");
        Assert.True(exposed.Count == 0, "areas handed out:\n" + string.Join("\n", exposed));
        Assert.Null(Abi.GetType("SourceSharp.Abi.ShimAreas"));
        Assert.Null(Abi.GetType("SourceSharp.Abi.ClientShimAreas"));
    }

    /// <summary>
    /// The side keeps the areas its own services read, and those alone: the
    /// ones made with the side, before any composition exists (its log, its
    /// globals and clock, its model descriptors and break models, its entity
    /// liveness, the shell's velocity host, and the player slots the movement
    /// loop's pass entity is found in). A binding's area is the
    /// binding's. A field added here is a decision to make in review, so the
    /// set is spelled out per side.
    /// </summary>
    [Fact]
    public void TheSideHoldsOnlyTheAreasItsOwnServicesRead()
    {
        string[] shared = ["_consoleArea", "_engineArea", "_entitiesArea", "_modelsArea"];
        var expected = new Dictionary<Assembly, string[]>
        {
            [typeof(EngineSide).Assembly] = [.. shared, "_physicsArea", "_playersArea", "_precacheArea", "_transformArea"],
            [typeof(client::SourceSharp.Abi.EngineSide).Assembly] = shared,
        };

        foreach (var (assembly, names) in expected)
        {
            Type side = assembly.GetType("SourceSharp.Abi.EngineSide", throwOnError: true)!;
            var held = side.GetFields(Declared).Where(f => IsArea(f.FieldType)).Select(f => f.Name).Order().ToList();
            Assert.Equal(names.Order().ToList(), held);
        }
    }

    /// <summary>
    /// The side's registry of areas is read where a consumer is put together
    /// and nowhere else: by the side itself and its <c>ModuleEngine</c>, by
    /// the client's adapter registration (<c>ClientEntityFetches.AddTo</c>,
    /// which the module calls once while it composes, and which is an
    /// instance method only so a fact can hand in another), and otherwise only
    /// in a static factory (<c>For</c>, <c>ForEngine</c>, <c>Acquire</c>), which
    /// takes the areas once and hands them to a constructor. A constructor
    /// takes its areas; it never looks them up (review of phase 11), and an
    /// instance method that read the registry would be looking an area up per
    /// call, which is the container again behind a different name. A lambda
    /// is its closure's method, so one written in a factory that read the
    /// registry per call is caught too.
    /// </summary>
    [Fact]
    public void OnlyCompositionReadsTheSidesAreas()
    {
        var readers = new List<string>();
        var lookups = new List<string>();

        foreach (Assembly assembly in Interops)
        {
            Type side = assembly.GetType("SourceSharp.Abi.EngineSide", throwOnError: true)!;
            MethodInfo getter = side.GetProperty("Areas", BindingFlags.Instance | BindingFlags.NonPublic)!.GetMethod!;
            byte[] call = [0x28, .. BitConverter.GetBytes(getter.MetadataToken)];
            byte[] callvirt = [0x6F, .. BitConverter.GetBytes(getter.MetadataToken)];

            foreach (Type type in Types(assembly))
            {
                Type outer = type;
                while (outer.DeclaringType is { } declaring)
                    outer = declaring;

                foreach (MethodBase method in type.GetMethods(Declared).Cast<MethodBase>().Concat(type.GetConstructors(Declared)))
                {
                    byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
                    if (il is null || !(Contains(il, call) || Contains(il, callvirt)))
                        continue;

                    string where = $"{assembly.GetName().Name}: {type.FullName}.{method.Name}";
                    readers.Add(where);

                    bool composition = outer.Name is "EngineSide" or "ModuleEngine"
                                    || (outer.Name == "ClientEntityFetches" && method.Name == "AddTo")
                                    || (type == outer && method.IsStatic && !method.IsConstructor);
                    if (!composition)
                        lookups.Add(where);
                }
            }
        }

        // The positive arm: the walk finds the composition that does read it.
        Assert.Contains(readers, r => r.EndsWith("EngineServices.Build", StringComparison.Ordinal));
        Assert.Contains(readers, r => r.EndsWith("ClientEntityFetches.AddTo", StringComparison.Ordinal));
        Assert.True(lookups.Count == 0, "the side's areas looked up outside composition:\n" + string.Join("\n", lookups));
    }

    private static bool Contains(byte[] il, byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= il.Length; i++)
            if (il.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return true;
        return false;
    }
}
