using System.Runtime.InteropServices;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// THE FRAME'S SKELETONS, POSED AHEAD (plan_physics_bones_perf A2): the table
/// IManagedBonesClient::BoneBatch hands native once a frame, laid out as
/// src/shared/native/standalone_bone_batch.h declares it.
///
/// <para>
/// Native answers a SetupBones ask from an entry only when the ask is the one
/// the entry was posed for (frame, clock, a mask inside the posed one, the
/// block inputs the pose read); everything else still crosses. What goes in,
/// and when an entry stops being good, is decided here: <see cref="Drop"/>
/// the moment that entity's skeleton state changes, <see cref="DropAll"/> when
/// every skeleton's does. Native counts the asks each entry answered, which is
/// how the next frame knows the entity was drawn.
/// </para>
///
/// <para>
/// The memory is unmanaged and owned here; it moves only while a batch is
/// being written (the start of a frame), and native reads every pointer
/// through the header on each ask. The header itself never moves once its
/// address is handed to native (<see cref="Hand"/>): native may hold that
/// address until the next frame's first ask, past a hot reload. So a reload
/// hands the table on (<see cref="Adopt"/>, carried by ClientBoneBatchCarry)
/// instead of leaving it behind: the new build answers native the same
/// address, and the only table ever freed is the new build's own, which
/// native was never handed. No table is left behind, and none native may
/// hold is freed.
/// </para>
/// </summary>
public sealed unsafe class ClientBoneBatch
{
    /// <summary>standalone_bone_batch.h's SourceSharpBoneBatchEntry_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Entry
    {
        public uint Entity;
        public int BoneMask;
        public int Bones;
        public int FirstMatrix;
        public int Answered;
        public int ModelIndex;
        public int ViewModel;
        public Vector Origin;
        public QAngle Angles;
        public Vector RenderOrigin;
        public QAngle RenderAngles;
    }

    /// <summary>standalone_bone_batch.h's SourceSharpBoneBatch_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Header
    {
        public int FrameCount;
        public float CurrentTime;
        public int Entries;
        public int Pad;
        public short* SlotToEntry;
        public Entry* EntryTable;
        public float* Matrices;
    }

    /// <summary><c>SS_BONE_BATCH_SLOTS</c>: an entity's slot is its handle's entry index.</summary>
    public const int Slots = 4096;

    private const int MatrixFloats = 12;

    private Header* _header;
    private int _entryCapacity;
    private int _matrixCapacity;
    private int _matricesUsed;

    public ClientBoneBatch()
    {
        _header = (Header*)NativeMemory.AllocZeroed((nuint)sizeof(Header));
        _header->SlotToEntry = (short*)NativeMemory.Alloc(Slots * sizeof(short));
        new Span<short>(_header->SlotToEntry, Slots).Fill(-1);
        _header->FrameCount = -1;
        TablesAllocated = 1;
    }

    /// <summary>The table's address.</summary>
    public nint Address => (nint)_header;

    /// <summary>Whether <see cref="Address"/> has been handed to native: from then on it is never freed.</summary>
    public bool Handed { get; private set; }

    /// <summary>The tables this instance allocated (its constructor's).</summary>
    public int TablesAllocated { get; private set; }

    /// <summary>The tables this instance freed: its own, when it adopted a previous build's.</summary>
    public int TablesFreed { get; private set; }

    /// <summary>The address of the table this instance freed, or 0.</summary>
    public nint FreedTable { get; private set; }

    /// <summary>The entries and matrices the table has room for, which a reload carries with it.</summary>
    public (int Entries, int Matrices) Capacity => (_entryCapacity, _matrixCapacity);

    /// <summary>The table's address, as BoneBatch answers it to native: from now on it is never freed.</summary>
    public nint Hand()
    {
        Handed = true;
        return (nint)_header;
    }

    /// <summary>
    /// A previous build's table, taken over as it is (a hot reload): native
    /// may still hold its address, so it stays the address answered. This
    /// instance's own table is freed in its place, which native was never
    /// handed. Refused (false, nothing changes) once this instance's own
    /// table was handed, or for no table. The carried table answers nothing
    /// until the next batch.
    /// </summary>
    public bool Adopt(nint address, int entryCapacity, int matrixCapacity)
    {
        if (address == 0 || address == (nint)_header || Handed || entryCapacity < 0 || matrixCapacity < 0)
        {
            return false;
        }

        Header* own = _header;
        NativeMemory.Free(own->SlotToEntry);
        NativeMemory.Free(own->EntryTable);
        NativeMemory.Free(own->Matrices);
        NativeMemory.Free(own);
        FreedTable = (nint)own;
        TablesFreed++;

        _header = (Header*)address;
        _entryCapacity = entryCapacity;
        _matrixCapacity = matrixCapacity;
        _matricesUsed = 0;
        DropAll();
        return true;
    }

    /// <summary>The frame the table answers for.</summary>
    public int FrameCount => _header->FrameCount;

    /// <summary>How many entries the table holds now.</summary>
    public int Count => _header->Entries;

    private static int SlotOf(EntityHandle entity) => (int)(entity.Value & (Slots - 1));

    /// <summary>The entry posed for <paramref name="entity"/>, if the table holds one.</summary>
    public bool TryGet(EntityHandle entity, out Entry entry)
    {
        short at = _header->SlotToEntry[SlotOf(entity)];

        if (at < 0 || at >= _header->Entries || _header->EntryTable[at].Entity != entity.Value)
        {
            entry = default;
            return false;
        }

        entry = _header->EntryTable[at];
        return true;
    }

    /// <summary>The matrices of an entry, 12 floats a bone (the copy native makes).</summary>
    public ReadOnlySpan<float> Matrices(in Entry entry) =>
        new(_header->Matrices + entry.FirstMatrix * MatrixFloats, entry.Bones * MatrixFloats);

    /// <summary>A new frame's table: empty, answering for this frame and clock.</summary>
    public void Begin(int frameCount, float currentTime)
    {
        for (int i = 0; i < _header->Entries; i++)
        {
            _header->SlotToEntry[SlotOf(new EntityHandle(_header->EntryTable[i].Entity))] = -1;
        }

        _header->Entries = 0;
        _header->FrameCount = frameCount;
        _header->CurrentTime = currentTime;
        _matricesUsed = 0;
    }

    /// <summary>
    /// <paramref name="entity"/>'s skeleton (12 floats a bone), posed under
    /// <paramref name="boneMask"/> from <paramref name="block"/>'s inputs, as an entry.
    /// </summary>
    public void Add(EntityHandle entity, in EngineBlock block, int boneMask, ReadOnlySpan<float> boneFloats)
    {
        int slot = SlotOf(entity);
        int bones = boneFloats.Length / MatrixFloats;

        if (_header->SlotToEntry[slot] >= 0 || bones <= 0)
        {
            return;
        }

        Reserve(_header->Entries + 1, _matricesUsed + bones);

        int at = _header->Entries++;
        _header->EntryTable[at] = new Entry
        {
            Entity = entity.Value,
            BoneMask = boneMask,
            Bones = bones,
            FirstMatrix = _matricesUsed,
            ModelIndex = block.Head.ModelIndex,
            ViewModel = block.Client.ViewModel,
            Origin = block.Head.Origin,
            Angles = block.Head.Angles,
            RenderOrigin = block.Client.RenderOrigin,
            RenderAngles = block.Client.RenderAngles,
        };

        boneFloats[..(bones * MatrixFloats)].CopyTo(new Span<float>(_header->Matrices + _matricesUsed * MatrixFloats,
                                                                    bones * MatrixFloats));
        _matricesUsed += bones;
        _header->SlotToEntry[slot] = (short)at;
    }

    /// <summary>The entry posed for <paramref name="entity"/> no longer answers anything.</summary>
    public void Drop(EntityHandle entity)
    {
        int slot = SlotOf(entity);
        short at = _header->SlotToEntry[slot];

        if (at >= 0 && at < _header->Entries && _header->EntryTable[at].Entity == entity.Value)
        {
            _header->SlotToEntry[slot] = -1;
        }
    }

    /// <summary>
    /// No entry answers anything until the next frame's batch. The entries
    /// stay readable (<see cref="EntryAt"/>): what native answered before the
    /// drop still says the entity was drawn.
    /// </summary>
    public void DropAll()
    {
        for (int i = 0; i < _header->Entries; i++)
        {
            _header->SlotToEntry[SlotOf(new EntityHandle(_header->EntryTable[i].Entity))] = -1;
        }
    }

    /// <summary>Entry <paramref name="index"/> of this frame's table, dropped or not.</summary>
    public Entry EntryAt(int index) => _header->EntryTable[index];

    private void Reserve(int entries, int matrices)
    {
        if (entries > _entryCapacity)
        {
            int capacity = Math.Max(entries, Math.Max(32, _entryCapacity * 2));
            _header->EntryTable = (Entry*)NativeMemory.Realloc(_header->EntryTable, (nuint)(capacity * sizeof(Entry)));
            _entryCapacity = capacity;
        }

        if (matrices > _matrixCapacity)
        {
            int capacity = Math.Max(matrices, Math.Max(1024, _matrixCapacity * 2));
            _header->Matrices = (float*)NativeMemory.Realloc(_header->Matrices,
                                                             (nuint)(capacity * MatrixFloats * sizeof(float)));
            _matrixCapacity = capacity;
        }
    }
}
