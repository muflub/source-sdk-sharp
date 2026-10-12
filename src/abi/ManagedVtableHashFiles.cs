using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SourceSharp.Abi;

/// <summary>
/// Reads a build's <see cref="ManagedVtableHashesAttribute"/>s from its files
/// on disk, through metadata only: nothing is loaded, so the running build is
/// untouched while a reload is checked (plan_managed_vtable_reload.md L5).
/// </summary>
public static class ManagedVtableHashFiles
{
    private const string AttributeNamespace = "SourceSharp.Abi";
    private const string AttributeName = nameof(ManagedVtableHashesAttribute);

    /// <summary>
    /// Every contract's hashes across <paramref name="roots"/> (simple
    /// assembly names) and the assemblies they reference that live in
    /// <paramref name="directory"/>.
    /// </summary>
    public static Dictionary<string, uint[]> Read(string directory, IEnumerable<string> roots)
    {
        var found = new Dictionary<string, uint[]>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(roots);

        while (pending.TryDequeue(out string? name))
        {
            if (!seen.Add(name))
                continue;

            string path = Path.Combine(directory, name + ".dll");
            if (!File.Exists(path))
                continue;   // a framework or default-context reference

            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
                continue;

            var md = pe.GetMetadataReader();
            foreach (var reference in md.AssemblyReferences)
                pending.Enqueue(md.GetString(md.GetAssemblyReference(reference).Name));

            foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = md.GetCustomAttribute(handle);
                if (!IsHashesAttribute(md, attribute))
                    continue;

                var value = attribute.DecodeValue(new NameProvider());
                string contract = (string)value.FixedArguments[0].Value!;
                var slots = (ImmutableArray<CustomAttributeTypedArgument<string>>)value.FixedArguments[1].Value!;
                found[contract] = slots.Select(s => (uint)s.Value!).ToArray();
            }
        }

        return found;
    }

    private static bool IsHashesAttribute(MetadataReader md, CustomAttribute attribute)
    {
        System.Reflection.Metadata.EntityHandle type = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };

        return type.Kind switch
        {
            HandleKind.TypeReference => md.GetTypeReference((TypeReferenceHandle)type) is var r
                                        && md.StringComparer.Equals(r.Name, AttributeName)
                                        && md.StringComparer.Equals(r.Namespace, AttributeNamespace),
            HandleKind.TypeDefinition => md.GetTypeDefinition((TypeDefinitionHandle)type) is var d
                                         && md.StringComparer.Equals(d.Name, AttributeName)
                                         && md.StringComparer.Equals(d.Namespace, AttributeNamespace),
            _ => false,
        };
    }

    /// <summary>Names types as strings: all the decoder needs for a (string, uint[]) attribute.</summary>
    private sealed class NameProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetSystemType() => "System.Type";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeReference(handle).Name);
        public string GetTypeFromSerializedName(string name) => name;
        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
            => throw new BadImageFormatException($"{AttributeName} carries no enum, but {type} was asked for");
        public bool IsSystemType(string type) => type == "System.Type";
    }
}
