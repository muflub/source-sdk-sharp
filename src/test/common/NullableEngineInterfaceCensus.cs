using System.Reflection;
using System.Runtime.CompilerServices;

namespace SourceSharp.Tests;

/// <summary>
/// The 2026-10-20 ruling over the built assemblies: managed logic never
/// checks for a null engine interface or expects one. Every member of a
/// product assembly is read through <see cref="NullabilityInfoContext"/>,
/// and a field, property, parameter or return that is a NULLABLE engine
/// interface (an <c>IFwd*</c> interface, or one of the engine's slices the
/// same objects answer), at any depth of its type (<c>Func&lt;IFwdX?&gt;</c>
/// counts), is reported.
///
/// <para>
/// <b>The acquisition layer is the one exemption, and it is named by
/// shape</b>: an interop type that implements an engine interface (the
/// wrapper the side acquires, whose internal <c>Acquire</c> answers null with
/// the engine's reason), and the side's bound slots that hold what was acquired
/// (<c>EngineBound</c>, <c>EngineBoundSet</c>, <c>BoundInterface&lt;T&gt;</c>,
/// and <c>EngineServerBinding</c>, which installs the server slot's acquirer).
/// Everything else is handed the registered interface, never a null.
/// </para>
/// </summary>
internal static class NullableEngineInterfaceCensus
{
    // The engine's slices that are not named IFwd*: the same objects answer
    // them (EngineServer, EngineClient, PhysicsSurfaceProps, the random stream).
    private static readonly string[] Slices =
    [
        "IUniformRandom", "ISaveMemory", "IAreaPortalEngine", "IVPhysicsSurfaceSounds",
        "IClientInputEngine", "ISteamLaunchCommands", "IPlaybackState",
    ];

    // The slots, and the binding that installs the server slot's acquirer.
    private static readonly string[] AcquisitionLayer = ["EngineBound", "EngineBoundSet", "BoundInterface`1", "EngineServerBinding"];

    /// <summary>Whether <paramref name="type"/> is an engine interface the ruling covers.</summary>
    public static bool IsEngineInterface(Type type)
        => type.IsInterface && (type.Name.StartsWith("IFwd", StringComparison.Ordinal) || Slices.Contains(type.Name));

    /// <summary>Every type of <paramref name="assemblies"/>.</summary>
    public static IEnumerable<Type> TypesOf(params Assembly[] assemblies)
        => assemblies.SelectMany(a => a.GetTypes());

    /// <summary>
    /// The members of <paramref name="types"/> that hold, take or answer a
    /// nullable engine interface, outside the acquisition layer, and how many
    /// members mention an engine interface at all (so a scan that saw none,
    /// and so could not fail, is visible).
    /// </summary>
    public static (List<string> Nullable, int Mentions) Scan(IEnumerable<Type> types)
    {
        var context = new NullabilityInfoContext();
        var found = new List<string>();
        int mentions = 0;
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                               | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (Type type in types)
        {
            if (IsAcquisitionLayer(type))
                continue;

            string owner = type.FullName ?? type.Name;

            foreach (FieldInfo field in type.GetFields(all))
                Check(context.Create(field), $"{owner}.{field.Name} (field)");

            foreach (PropertyInfo property in type.GetProperties(all))
                Check(context.Create(property), $"{owner}.{property.Name} (property)");

            foreach (MethodBase method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                    Check(context.Create(parameter), $"{owner}.{method.Name}({parameter.Name})");

                if (method is MethodInfo { ReturnType: var returns } info && returns != typeof(void))
                    Check(context.Create(info.ReturnParameter), $"{owner}.{method.Name} (return)");
            }
        }

        return (found, mentions);

        void Check(NullabilityInfo info, string where)
        {
            bool mentioned = false, nullable = false;
            Walk(info, ref mentioned, ref nullable);
            if (mentioned)
                mentions++;
            if (nullable)
                found.Add(where);
        }
    }

    private static void Walk(NullabilityInfo info, ref bool mentioned, ref bool nullable)
    {
        Type type = info.Type.IsByRef ? info.Type.GetElementType()! : info.Type;
        if (IsEngineInterface(type))
        {
            mentioned = true;
            if (info.ReadState == NullabilityState.Nullable || info.WriteState == NullabilityState.Nullable)
                nullable = true;
        }

        if (info.ElementType is { } element)
            Walk(element, ref mentioned, ref nullable);
        foreach (NullabilityInfo argument in info.GenericTypeArguments)
            Walk(argument, ref mentioned, ref nullable);
    }

    // The type itself, or the outermost type a compiler-generated one (a
    // lambda's closure) was written in. A wrapper the side acquires lives in
    // an interop assembly and implements the engine interface itself; logic
    // that merely implements one is not exempt.
    private static bool IsAcquisitionLayer(Type type)
    {
        Type outer = type;
        while (outer.DeclaringType is { } declaring && IsGenerated(outer))
            outer = declaring;

        return AcquisitionLayer.Contains(outer.Name)
            || (!outer.IsInterface && outer.Assembly.GetName().Name!.EndsWith(".Interop", StringComparison.Ordinal)
                && outer.GetInterfaces().Any(IsEngineInterface));
    }

    private static bool IsGenerated(Type type)
        => type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) || type.Name.Contains('<');
}
