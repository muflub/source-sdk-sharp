using System.Reflection;
using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// A slot of a generated layout (VtableLayouts.g.cs), found by name whichever
/// form tools/vtable-layout gave it.
///
/// <para>
/// A layout MSVC orders the Itanium way is a struct of FIELDS, and a field's
/// offset is its slot. One it orders differently (a virtual destructor in one
/// slot, overloads grouped and reversed) is a span of pointers read through
/// PROPERTIES, each carrying <see cref="VtableSlotAttribute"/> with its index
/// under both ABIs. A test that builds a fake table writes the index the
/// property will read in this process, so it asks here rather than taking a
/// field offset.
/// </para>
/// </summary>
internal static class VtableSlots
{
    /// <summary>The slot <paramref name="name"/> is read from in this process.</summary>
    public static int Of<TVtbl>(string name) where TVtbl : unmanaged => Of(typeof(TVtbl), name);

    /// <inheritdoc cref="Of{TVtbl}(string)"/>
    public static int Of(Type layout, string name)
    {
        var (itanium, microsoft) = BothOf(layout, name);
        return OperatingSystem.IsWindows() ? microsoft : itanium;
    }

    /// <summary>
    /// The slot under each ABI, as the generator wrote it: the attribute's two
    /// numbers, or the field's offset twice for a layout both ABIs order alike.
    /// </summary>
    public static (int Itanium, int Microsoft) BothOf(Type layout, string name)
    {
        MemberInfo member = (MemberInfo?)layout.GetField(name, BindingFlags.Public | BindingFlags.Instance)
            ?? layout.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"{layout.Name} has no slot named {name}", nameof(name));

        if (member.GetCustomAttribute<VtableSlotAttribute>() is { } slot)
            return (slot.Itanium, slot.Microsoft);

        if (member is not FieldInfo)
            throw new ArgumentException($"{layout.Name}.{name} is a property without [VtableSlot]", nameof(name));

        int offset = (int)Marshal.OffsetOf(layout, name) / IntPtr.Size;
        return (offset, offset);
    }

    /// <summary>
    /// Every slot of a layout: its name and its function-pointer type with the
    /// calling-convention modifiers (<c>[SuppressGCTransition]</c>,
    /// <c>[MemberFunction]</c>) kept, in declaration order.
    /// </summary>
    public static IEnumerable<(string Name, Type Type)> All(Type layout)
    {
        const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        return layout.GetFields(Public).Select(f => (f.Name, f.GetModifiedFieldType()))
            .Concat(layout.GetProperties(Public).Select(p => (p.Name, p.GetModifiedPropertyType())));
    }
}
