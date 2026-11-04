extern alias client;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using SourceSharp.Abi;

using Xunit;

using MetaHandle = System.Reflection.Metadata.EntityHandle;

namespace SourceSharp.Tests;

/// <summary>
/// A command declares its line as the parameters of its <c>Execute</c>, and the binding reads
/// it: what to tokenise, convert, refuse and suggest. A [ConCommand] class that implements
/// <see cref="IConCommand"/> reads the raw line itself, and completes nothing, so one is
/// listed here with its reason or it fails. Read from the metadata of every assembly the game
/// loads, so an assembly that starts declaring commands is covered by being built.
/// </summary>
public class ConCommandCensusTests
{
    /// <summary>Free-text commands and why each must stay one. No command needs it today.</summary>
    private static readonly Dictionary<string, string> Waivers = new(StringComparer.Ordinal);

    /// <summary>The assemblies that declare commands: an empty scan of any of them is a broken census.</summary>
    private static readonly string[] Declaring =
    [
        "SourceSharp.Server", "SourceSharp.Client", "TF2.Server", "TF2.Client", "TF2.Descent.Server", "SourceSharp.Ai.Hl2mp",
    ];

    /// <summary>The fixture the census must be able to see.</summary>
    [ConCommand("ss_census_free_text")]
    public sealed class FreeTextFixture : IConCommand
    {
        public void Execute(string args) { }
    }

    /// <summary>Free text through a base class, as a family of commands would be.</summary>
    public abstract class FreeTextBase : IConCommand
    {
        public void Execute(string args) { }
    }

    [ConCommand("ss_census_free_text_derived")]
    public sealed class DerivedFreeTextFixture : FreeTextBase;

    /// <summary>A typed fixture, which it must not.</summary>
    [ConCommand("ss_census_typed")]
    public sealed class TypedFixture
    {
        public void Execute(string name) { }
    }

    private static string ManagedDir
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(ConCommandCensusTests).Assembly.Location)!, "..", "bin", "managed"));

    [Fact]
    public void NoProductCommandReadsItsOwnLine()
    {
        var rows = Scan(ProductAssemblies());
        string[] free = [.. rows.Where(r => r.FreeText && !Waivers.ContainsKey(r.Command)).Select(r => $"{r.Command} ({r.Assembly}: {r.Type})")];
        Assert.True(free.Length == 0, $"{free.Length} free-text command(s), neither typed nor waived:\n" + string.Join("\n", free));
    }

    [Fact]
    public void EveryWaiverIsStillAFreeTextCommand()
    {
        var free = Scan(ProductAssemblies()).Where(r => r.FreeText).Select(r => r.Command).ToHashSet();
        Assert.DoesNotContain(Waivers.Keys, k => !free.Contains(k));
    }

    /// <summary>The stimulus reaches the subject: every declaring assembly was read, and held commands.</summary>
    [Fact]
    public void TheScanReachesEveryDeclaringAssembly()
    {
        var rows = Scan(ProductAssemblies());
        Assert.All(Declaring, a => Assert.True(rows.Count(r => r.Assembly == a) > 0, $"no command found in {a}"));
        Assert.True(rows.Count > 250, $"only {rows.Count} commands found");
    }

    [Fact]
    public void TheCensusSeesFreeTextAndNotTyped()
    {
        var rows = Scan([typeof(ConCommandCensusTests).Assembly.Location]).ToDictionary(r => r.Command);
        Assert.True(rows["ss_census_free_text"].FreeText);
        Assert.True(rows["ss_census_free_text_derived"].FreeText);
        Assert.False(rows["ss_census_typed"].FreeText);
    }

    [Fact]
    public void EveryServerCommandBinds()
    {
        var (typed, unbound) = ConCommandLines.Census(typeof(SourceSharp.Server.ServerModule).Assembly);
        Assert.True(typed > 100, $"{typed}");
        Assert.Empty(unbound);
    }

    [Fact]
    public void EveryClientCommandBinds()
    {
        var (typed, unbound) = client::SourceSharp.Abi.ConCommandLines.Census(typeof(client::SourceSharp.Client.ClientModule).Assembly);
        Assert.True(typed > 100, $"{typed}");
        Assert.Empty(unbound);
    }

    private static string[] ProductAssemblies()
    {
        Assert.True(Directory.Exists(ManagedDir), $"{ManagedDir}: the game's managed directory, which the build fills");
        return Directory.GetFiles(ManagedDir, "*.dll");
    }

    private readonly record struct Row(string Command, string Assembly, string Type, bool FreeText);

    private static List<Row> Scan(string[] paths)
    {
        var modules = new List<(PEReader Pe, MetadataReader Md, string Assembly)>();
        try
        {
            foreach (string path in paths)
            {
                var pe = new PEReader(File.OpenRead(path));
                if (!pe.HasMetadata)
                {
                    pe.Dispose();
                    continue;
                }
                modules.Add((pe, pe.GetMetadataReader(), Path.GetFileNameWithoutExtension(path)));
            }

            var byName = new Dictionary<string, (MetadataReader Md, TypeDefinition Type)>(StringComparer.Ordinal);
            foreach (var (_, md, _) in modules)
            {
                foreach (TypeDefinitionHandle h in md.TypeDefinitions)
                    byName.TryAdd(NameOf(md, h), (md, md.GetTypeDefinition(h)));
            }

            var rows = new List<Row>();
            foreach (var (_, md, assembly) in modules)
            {
                foreach (TypeDefinitionHandle h in md.TypeDefinitions)
                {
                    TypeDefinition type = md.GetTypeDefinition(h);
                    if (DeclaredCommand(md, type) is { } command)
                        rows.Add(new Row(command, assembly, NameOf(md, h), ReadsItsLine(md, type, byName)));
                }
            }
            return rows;
        }
        finally
        {
            foreach (var (pe, _, _) in modules)
                pe.Dispose();
        }
    }

    /// <summary>The name in the type's [ConCommand("name")], or null.</summary>
    private static string? DeclaredCommand(MetadataReader md, TypeDefinition type)
    {
        foreach (CustomAttributeHandle h in type.GetCustomAttributes())
        {
            CustomAttribute attribute = md.GetCustomAttribute(h);
            MetaHandle owner = attribute.Constructor.Kind == HandleKind.MemberReference
                ? md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
                : md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
            if (NameOf(md, owner) != "SourceSharp.Abi.ConCommandAttribute")
                continue;

            BlobReader blob = md.GetBlobReader(attribute.Value);
            blob.ReadUInt16();   // the prolog
            return blob.ReadSerializedString();
        }
        return null;
    }

    /// <summary>Whether it implements IConCommand, itself or through a base class in any assembly scanned.</summary>
    private static bool ReadsItsLine(MetadataReader md, TypeDefinition type, Dictionary<string, (MetadataReader Md, TypeDefinition Type)> byName)
    {
        foreach (InterfaceImplementationHandle h in type.GetInterfaceImplementations())
        {
            if (NameOf(md, md.GetInterfaceImplementation(h).Interface) == "SourceSharp.Abi.IConCommand")
                return true;
        }

        return !type.BaseType.IsNil && byName.TryGetValue(NameOf(md, type.BaseType), out var parent)
               && ReadsItsLine(parent.Md, parent.Type, byName);
    }

    private static string NameOf(MetadataReader md, MetaHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                TypeDefinition def = md.GetTypeDefinition((TypeDefinitionHandle)handle);
                string name = md.GetString(def.Name);
                return def.GetDeclaringType().IsNil
                    ? Qualified(md.GetString(def.Namespace), name)
                    : $"{NameOf(md, def.GetDeclaringType())}+{name}";
            case HandleKind.TypeReference:
                TypeReference reference = md.GetTypeReference((TypeReferenceHandle)handle);
                return reference.ResolutionScope.Kind == HandleKind.TypeReference
                    ? $"{NameOf(md, (TypeReferenceHandle)reference.ResolutionScope)}+{md.GetString(reference.Name)}"
                    : Qualified(md.GetString(reference.Namespace), md.GetString(reference.Name));
            default:
                return string.Empty;
        }
    }

    private static string Qualified(string space, string name) => space.Length == 0 ? name : $"{space}.{name}";
}
