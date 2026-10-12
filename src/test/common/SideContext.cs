using System.Reflection;
using System.Runtime.Loader;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A side's load context, arranged the way GameLoadContext arranges the real
/// one: assemblies come from the mod directory, and the ABI resolves to the copy
/// the default context already holds so contract types stay identical.
/// </summary>
internal sealed class SideContext : AssemblyLoadContext
{
    public SideContext(bool collectible = false)
        : base("ScanTestSide", collectible) { }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "SourceSharp.Abi")
            return typeof(ILog).Assembly;

        return FromBin(assemblyName.Name);
    }

    public Assembly? FromBin(string? simpleName)
    {
        if (simpleName is null)
            return null;

        var path = Path.Combine(AppContext.BaseDirectory, simpleName + ".dll");

        // Null falls through to the default context, which is what a framework
        // assembly wants.
        return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
    }
}
