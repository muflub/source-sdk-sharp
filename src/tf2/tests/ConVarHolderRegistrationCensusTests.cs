extern alias client;

using System.Reflection;

using Xunit;

namespace TF2.Tests;

/// <summary>
/// A class that only groups convars is built by the container, so it has to be
/// registered. An unregistered one throws where it is first asked for: pyro
/// vision's threw every frame in the render plan's post-process pass, and a
/// test that built the holder by hand never saw it.
/// </summary>
public class ConVarHolderRegistrationCensusTests
{
    // Each side loads its own SourceSharp.Abi, so the client's ConVarBase is not
    // the test's: these read by name, or the client's holders go unseen.
    private static bool IsConVar(Type? type)
    {
        for (; type is not null; type = type.BaseType)
        {
            if (type.FullName == "SourceSharp.Abi.ConVarBase")
                return true;
        }

        return false;
    }

    private static bool IsConVarHolder(Type type)
    {
        if (!type.IsClass || type.IsAbstract || IsConVar(type))
            return false;

        ConstructorInfo[] constructors = type.GetConstructors();
        if (constructors.Length != 1)
            return false;

        ParameterInfo[] parameters = constructors[0].GetParameters();
        return parameters.Length > 0 && parameters.All(p => IsConVar(p.ParameterType));
    }

    private static bool IsRegistered(Type type) =>
        type.CustomAttributes.Any(a => a.AttributeType.Name == "SideServiceAttribute");

    [Fact]
    public void EveryConVarHolderIsRegistered()
    {
        Assembly[] assemblies = [typeof(TF2.Server.TFStickybombConVars).Assembly,
                                 typeof(client::TF2.Client.PyroVisionConVars).Assembly,
                                 typeof(TF2.Shared.TFCritConVars).Assembly];

        List<Type> holders = [.. assemblies.Distinct().SelectMany(a => a.GetTypes()).Where(IsConVarHolder)];

        // The stimulus reaches both sides' holders.
        Assert.Contains(holders, t => t.FullName == "TF2.Client.PyroVisionConVars");
        Assert.Contains(holders, t => t.FullName == "TF2.Server.TFStickybombConVars");
        Assert.Empty(holders.Where(t => !IsRegistered(t)).Select(t => t.FullName));
    }
}
