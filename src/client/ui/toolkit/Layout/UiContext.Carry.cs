using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SourceSharp.Client.Ui.Toolkit;

// CARRIED ACROSS A UI-ONLY RELOAD (plan_client_ui U5): the per-id state every
// widget keeps (scroll offsets, open dropdowns, armed captures, a form's
// working copy is not here: it is the form's) and the focus, as bytes, so a
// new build of the toolkit and screens in a new load context picks up where
// the old one was. Ids are hashes of keys within scopes, so they survive any
// edit that keeps the keys. A state type is matched by its full name and its
// size: one that changed shape in the edit starts fresh rather than being
// read as something else.
public sealed partial class UiContext
{
    // Each pool answers its own size and whether it holds references, through
    // its own generic code: asking by reflection (MakeGenericMethod over a
    // state type) would leave the old build's types in the runtime's own
    // caches, which are never collected, and the old context with them.
    private interface IStatePool
    {
        int Size { get; }

        bool Carried { get; }

        void Write(BinaryWriter w);

        void Read(BinaryReader r, int count);
    }

    private const int CarryVersion = 1;

    /// <summary>The focus and every unmanaged per-id state, as bytes.</summary>
    public byte[] Carry()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(CarryVersion);
            w.Write(Focused);
            int pools = 0;
            foreach (var (_, pool) in _statePools)
                if (((IStatePool)pool).Carried)
                    pools++;
            w.Write(pools);
            foreach (var (type, value) in _statePools)
            {
                var pool = (IStatePool)value;
                if (!pool.Carried)
                    continue;
                w.Write(type.FullName ?? type.Name);
                w.Write(pool.Size);
                pool.Write(w);
            }
        }
        return stream.ToArray();
    }

    /// <summary>
    /// What <see cref="Carry"/> wrote, back into this context. <paramref name="resolve"/>
    /// finds a state type by its full name in this build (the toolkit's own,
    /// the screens', the runtime's); a name it cannot find, or a size that
    /// changed, is skipped. Answers how many pools were restored and skipped.
    /// </summary>
    public (int Restored, int Skipped) Restore(byte[] carried, Func<string, Type?> resolve)
    {
        using var r = new BinaryReader(new MemoryStream(carried));
        if (r.ReadInt32() != CarryVersion)
            return (0, 0);
        Focused = r.ReadUInt32();
        int pools = r.ReadInt32(), restored = 0, skipped = 0;
        for (int i = 0; i < pools; i++)
        {
            string name = r.ReadString();
            int size = r.ReadInt32();
            int count = r.ReadInt32();
            var type = resolve(name);
            object? pool = null;
            if (type is { IsValueType: true } && !type.ContainsGenericParameters && !_statePools.TryGetValue(type, out pool))
                pool = Activator.CreateInstance(typeof(StatePool<>).MakeGenericType(type));
            if (pool is not IStatePool p || !p.Carried || p.Size != size)
            {
                r.ReadBytes(count * (4 + size));
                skipped++;
                continue;
            }
            _statePools[type!] = pool;
            p.Read(r, count);
            restored++;
        }
        return (restored, skipped);
    }

    private static void CarryPool<T>(StatePool<T> pool, BinaryWriter w) where T : struct
    {
        w.Write(pool.Values.Count);
        foreach (var (id, value) in pool.Values)
        {
            w.Write(id);
            T copy = value;
            w.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1)));
        }
    }

    private static void RestorePool<T>(StatePool<T> pool, BinaryReader r, int count) where T : struct
    {
        for (int i = 0; i < count; i++)
        {
            uint id = r.ReadUInt32();
            T value = default;
            r.Read(MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)));
            pool.Values[id] = value;
        }
    }
}
