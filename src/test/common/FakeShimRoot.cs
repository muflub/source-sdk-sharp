using System.Reflection;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A shim root in unmanaged memory serving every generated area as an object
/// and a forwarder table under the table's own name
/// (plan_forwarders_everywhere.md) whose every slot answers its default
/// (<see cref="DefaultSlots"/>), except the slots a test fills with
/// <see cref="Live"/>. The root object's second word holds a handle to this
/// instance, so tests in parallel classes each answer from their own table.
/// </summary>
internal sealed unsafe class FakeShimRoot : IDisposable
{
    private readonly List<nint> _blocks = new();
    private readonly Dictionary<string, nint> _areas = new();
    private readonly Dictionary<string, nint> _interfaces = new();

    /// <summary>
    /// The reason an interface acquire this root does not serve gives, as the
    /// engine's factory writes one into its error buffer; null writes none.
    /// </summary>
    public string? Refusal { get; set; }

    /// <summary>
    /// Every interface acquire the root was asked, in order: the factory kind
    /// and the version string, whether or not it was served.
    /// </summary>
    public List<(SourceSharpFactoryType Type, string Version)> Asked { get; } = new();
    private readonly Dictionary<string, (Type Fwd, nint Table)> _tables = new();
    private readonly Dictionary<string, nint> _headers = new();
    private GCHandle _self;

    public FakeShimRoot()
    {
        // Every area either side registers, each served under its version
        // string: the area's class name less "Area" (IShimEngine's class is
        // IEngineArea, served as SourceSharpEngine001), so the same root binds
        // a server side and a client side (AttachOwnShims). A base area the
        // server registers again as its derived area's base is served once.
        var types = new List<Type>();
        ServerAreaRegistration.AddDetached((type, _) => types.Add(type));
        ClientAreaRegistration.AddDetached((type, _) => types.Add(type));
        foreach (Type area in types)
        {
            // Handed by interface (IEngineArea): the area's class name is the
            // interface's less its I.
            string name = area.IsInterface ? area.Name[1..] : area.Name;
            string version = $"SourceSharp{name[..^"Area".Length]}001";
            if (_areas.ContainsKey(version))
                continue;

            Serve(version, ShimAreaTypes.FwdOf(area));
        }
    }

    /// <summary>
    /// One area: an object nothing reads, under its version string, and its
    /// forwarder table's header, under the table's name, as the game DLL's
    /// internal factory serves both through the root's Acquire.
    /// </summary>
    private void Serve(string version, Type fwd)
    {
        int count = Marshal.SizeOf(fwd) / sizeof(nint);
        var header = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        header->Slots = (void**)Alloc(count);
        header->SlotCount = count;
        DefaultSlots.Fill(fwd, (nint*)header->Slots);

        string tableName = (string)fwd.GetProperty("TableName", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        _areas[version] = (nint)Alloc(1);
        _areas[tableName] = (nint)header;
        _headers[version] = (nint)header;
        _tables[version] = (fwd, (nint)header->Slots);
    }

    /// <summary>Fills one slot of one area, by the area's version and the slot's name.</summary>
    public FakeShimRoot Live(string version, string slot, nint fn)
    {
        var (fwd, table) = _tables[version];
        ((nint*)table)[(int)Marshal.OffsetOf(fwd, slot) / sizeof(nint)] = fn;
        return this;
    }

    /// <summary>
    /// Serves one interface through the root's AcquireInterface slot (an area
    /// that is not a shim-slot area, such as SourceSharpLevel001).
    /// </summary>
    public FakeShimRoot Interface(string version, void* self)
    {
        _interfaces[version] = (nint)self;
        return this;
    }

    /// <summary>
    /// Serves a forwarder table of <typeparamref name="TFwd"/>'s length under
    /// its own name (plan_engine_forwarders.md), the way a game DLL's internal
    /// factory serves the forwarder libraries' tables: the table header, its
    /// first word the slot array. The slots are null; a fact that calls one
    /// fills it.
    /// </summary>
    public FakeShimRoot Forwarders<TFwd>() where TFwd : unmanaged, IForwarderTable
        => Forwarders<TFwd>(out _);

    /// <summary>The same, handing back the table for a fact to fill.</summary>
    public FakeShimRoot Forwarders<TFwd>(out TFwd* table) where TFwd : unmanaged, IForwarderTable
        => Forwarders(out table, SourceSharp.Abi.Forwarders.SlotCount<TFwd>());

    /// <summary>
    /// The same, saying it holds <paramref name="served"/> slots: fewer than
    /// <typeparamref name="TFwd"/> reads is an older library. The slot array
    /// still spans the struct, so nothing reads past it.
    /// </summary>
    public FakeShimRoot Forwarders<TFwd>(out TFwd* table, int served) where TFwd : unmanaged, IForwarderTable
    {
        int count = SourceSharp.Abi.Forwarders.SlotCount<TFwd>();
        var header = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        header->Slots = (void**)Alloc(Math.Max(count, served));
        header->SlotCount = served;
        _interfaces[TFwd.TableName] = (nint)header;
        table = (TFwd*)header->Slots;
        return this;
    }

    /// <summary>Serves <paramref name="self"/> as the area <paramref name="version"/> through Acquire.</summary>
    public FakeShimRoot Area(string version, void* self)
    {
        _areas[version] = (nint)self;
        return this;
    }

    /// <summary>
    /// Serves <paramref name="self"/> as the generated area
    /// <paramref name="version"/> and <paramref name="slots"/>, a slot array
    /// of its forwarder struct's length, as that area's table: a fact whose
    /// slots read state from the object they are handed.
    /// </summary>
    public FakeShimRoot Area(string version, void* self, void* slots)
    {
        _areas[version] = (nint)self;
        ((ForwarderTableHeader*)_headers[version])->Slots = (void**)slots;
        _tables[version] = (_tables[version].Fwd, (nint)slots);
        return this;
    }

    /// <summary>
    /// Stops serving one area, as a root built without it would: its version
    /// string acquires nothing from now on.
    /// </summary>
    public FakeShimRoot Withhold(string version)
    {
        _areas.Remove(version);
        return this;
    }

    /// <summary>The root, bound the way LoadSideV2 binds the real one.</summary>
    public ShimRoot Bind()
    {
        Assert.True(ShimRoot.TryBind(Raw(), RawTable(), out var bound, out string why), why);
        return bound!;
    }

    /// <summary>
    /// The root as native hands it over, unbound: an object whose second word
    /// is this instance's handle (its first is unused, as nothing reads a
    /// root's vtable).
    /// </summary>
    public void* Raw()
    {
        if (!_self.IsAllocated)
            _self = GCHandle.Alloc(this);
        var root = Alloc(2);
        root[1] = GCHandle.ToIntPtr(_self);
        return root;
    }

    /// <summary>
    /// The root's forwarder table as native hands it over beside the root
    /// (SSFwd_ShimRoot, an SSForwarderTable): Acquire, AcquireInterface,
    /// Release, each handed the root object.
    /// </summary>
    public void* RawTable()
    {
        var slots = Alloc(3);
        slots[0] = (nint)(delegate* unmanaged<void*, byte*, void*>)&RootAcquire;
        slots[1] = (nint)(delegate* unmanaged<void*, int, byte*, byte*, int, void*>)&RootAcquireInterface;
        slots[2] = (nint)(delegate* unmanaged<void*, void>)&RootRelease;

        var header = (ForwarderTableHeader*)Alloc(sizeof(ForwarderTableHeader) / sizeof(nint));
        header->Slots = (void**)slots;
        header->SlotCount = 3;
        return header;
    }

    private nint* Alloc(int words)
    {
        var p = (nint*)NativeMemory.AllocZeroed((nuint)(words * sizeof(nint)));
        _blocks.Add((nint)p);
        return p;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquire(void* self, byte* name)
    {
        var owner = (FakeShimRoot)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        return owner._areas.TryGetValue(v, out nint area) ? (void*)area : null;
    }

    [UnmanagedCallersOnly]
    private static void* RootAcquireInterface(void* self, int type, byte* name, byte* error, int errorBytes)
    {
        var owner = (FakeShimRoot)GCHandle.FromIntPtr(((nint*)self)[1]).Target!;
        string v = Marshal.PtrToStringUTF8((nint)name) ?? string.Empty;
        owner.Asked.Add(((SourceSharpFactoryType)type, v));
        if (owner._interfaces.TryGetValue(v, out nint found))
            return (void*)found;

        // What the engine's factory says when it does not know a name, if the
        // fact set one: the reason a refused acquire carries to its message.
        if (owner.Refusal is { } reason && error != null && errorBytes > 0)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(reason);
            int n = Math.Min(bytes.Length, errorBytes - 1);
            bytes.AsSpan(0, n).CopyTo(new Span<byte>(error, n));
            error[n] = 0;
        }

        return null;
    }

    [UnmanagedCallersOnly]
    private static void RootRelease(void* self)
    {
    }

    public void Dispose()
    {
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
        _blocks.Clear();
        if (_self.IsAllocated)
            _self.Free();
    }
}
