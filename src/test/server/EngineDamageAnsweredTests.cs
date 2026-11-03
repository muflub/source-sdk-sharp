using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The damage seam's answered ask, held to its table.
///
/// <para>
/// <see cref="IEngineDamage.TakeDamageAnswered"/> is the one damage call that
/// answers how much the victim took, because stock's blast push lives inside
/// <c>OnTakeDamage_Alive</c> (tf_player.cpp:10232) — damage the victim refuses
/// never reaches the push, so a caller that pushes on a hit can only gate on
/// what the hit answered. <see cref="IEngineDamage.TakeDamage"/> answers
/// nothing, which is what let a refused blast hit push a player before the row
/// it took the push off landed.
/// </para>
///
/// <para>
/// Each fact serves the damage area from a <see cref="FakeShimRoot"/> whose
/// answering slot records into the object it is handed (word 1), so the
/// marshalling — handles, the damage-type cast, the two direction/origin
/// arrays or their absence, physics-only as an int — is read where the engine
/// reads it, and the table's answer is what the caller must see back.
/// </para>
/// </summary>
public unsafe class EngineDamageAnsweredTests : IDisposable
{
    /// <summary>The area's version, as its own registration serves it.</summary>
    private const string DamageVersion = "SourceSharpDamage001";

    // Recorder words: calls, victim, attacker, damage bits, damage type,
    // direction-null, direction xyz, origin-null, origin xyz, physics-only,
    // and the answer the slot returns (the facts set it before asking).
    private const int RecorderWords = 15;
    private const int Calls = 0, Victim = 1, Attacker = 2, Damage = 3, DamageKind = 4,
                      DirNull = 5, DirX = 6, DirY = 7, DirZ = 8,
                      OriginNull = 9, OriginX = 10, OriginY = 11, OriginZ = 12,
                      PhysicsOnly = 13, Answer = 14;

    private readonly FakeShimRoot _root = new();
    private readonly List<nint> _blocks = new();
    private readonly uint* _recorder;

    public EngineDamageAnsweredTests()
    {
        _recorder = (uint*)Alloc(RecorderWords * sizeof(uint));

        // The area object: word 1 is the recorder, the way the collision area's
        // facts hand their state to their slots; the answer starts as zero, the
        // same thing an engine answers when it takes nothing.
        var self = (nint*)Alloc(2 * sizeof(nint));
        self[1] = (nint)_recorder;

        // The table: every slot answering its default, and this one recording
        // what the seam marshalled.
        var slots = (nint*)Alloc(Marshal.SizeOf<ShimDamageFwd>());
        DefaultSlots.Fill(typeof(ShimDamageFwd), slots);
        slots[(int)Marshal.OffsetOf<ShimDamageFwd>(nameof(ShimDamageFwd.EntityTakeDamageAnswered)) / sizeof(nint)]
            = (nint)(delegate* unmanaged<void*, uint, uint, float, int, float*, float*, int, int>)&RecordAnswered;

        _root.Area(DamageVersion, self, slots);
    }

    public void Dispose()
    {
        _root.Dispose();
        foreach (nint block in _blocks)
            NativeMemory.Free((void*)block);
    }

    [Fact]
    public void TheAnsweredAskCarriesItsArgumentsAndReturnsWhatTheTableAnswered()
    {
        _recorder[Answer] = 7;
        var damage = SideBindings.Of<IEngineDamage>(Attached());

        int taken = damage.TakeDamageAnswered(new EntityHandle(42), new EntityHandle(3), 33.5f, DamageType.Blast,
                                              direction: new Vector(1.5f, -2.5f, 4.25f),
                                              forceOrigin: new Vector(64f, 32f, 96f),
                                              physicsOnly: true);

        Assert.Equal(1, (int)_recorder[Calls]);
        Assert.Equal(42u, _recorder[Victim]);
        Assert.Equal(3u, _recorder[Attacker]);
        Assert.Equal(33.5f, BitConverter.Int32BitsToSingle((int)_recorder[Damage]));
        Assert.Equal((int)DamageType.Blast, (int)_recorder[DamageKind]);
        Assert.Equal(0u, _recorder[DirNull]);
        Assert.Equal(1.5f, BitConverter.Int32BitsToSingle((int)_recorder[DirX]));
        Assert.Equal(-2.5f, BitConverter.Int32BitsToSingle((int)_recorder[DirY]));
        Assert.Equal(4.25f, BitConverter.Int32BitsToSingle((int)_recorder[DirZ]));
        Assert.Equal(0u, _recorder[OriginNull]);
        Assert.Equal(64f, BitConverter.Int32BitsToSingle((int)_recorder[OriginX]));
        Assert.Equal(32f, BitConverter.Int32BitsToSingle((int)_recorder[OriginY]));
        Assert.Equal(96f, BitConverter.Int32BitsToSingle((int)_recorder[OriginZ]));
        Assert.Equal(1u, _recorder[PhysicsOnly]);
        Assert.True(taken == 7, $"the amount the table answered is the amount the caller is told ({taken})");
    }

    [Fact]
    public void TheNullDirectionArmsTheTableWithNoPointers()
    {
        var damage = SideBindings.Of<IEngineDamage>(Attached());

        int taken = damage.TakeDamageAnswered(new EntityHandle(42), EntityHandle.None, 10f, DamageType.Bullet);

        Assert.Equal(1, (int)_recorder[Calls]);
        Assert.True(_recorder[DirNull] == 1u, "no direction means a null pointer, not a zeroed array");
        Assert.True(_recorder[OriginNull] == 1u, "with it, no force origin either");
        Assert.Equal(0u, _recorder[PhysicsOnly]);
        Assert.True(taken == 0, $"an engine that answers nothing returns no landed damage ({taken})");
    }

    //-------------------------------------------------------------------------

    private EngineSide Attached()
    {
        var engine = TestSides.Create(Side.Server);
        engine.Attachment.Initialize();

        Assert.True(engine.Attachment.AttachShims(_root.Bind(), out string why), why);
        return engine;
    }

    private nint* Alloc(int bytes)
    {
        void* p = NativeMemory.AllocZeroed((nuint)bytes);
        _blocks.Add((nint)p);
        return (nint*)p;
    }

    [UnmanagedCallersOnly]
    private static int RecordAnswered(void* self, uint hEntity, uint hAttacker, float flDamage,
                                      int nDamageType, float* pDirXYZ, float* pForceOriginXYZ, int bPhysicsOnly)
    {
        var r = (uint*)((nint*)self)[1];
        r[Calls]++;
        r[Victim] = hEntity;
        r[Attacker] = hAttacker;
        r[Damage] = *(uint*)&flDamage;
        r[DamageKind] = (uint)nDamageType;
        r[DirNull] = pDirXYZ is null ? 1u : 0u;
        if (pDirXYZ is not null)
        {
            r[DirX] = *(uint*)&pDirXYZ[0];
            r[DirY] = *(uint*)&pDirXYZ[1];
            r[DirZ] = *(uint*)&pDirXYZ[2];
        }
        r[OriginNull] = pForceOriginXYZ is null ? 1u : 0u;
        if (pForceOriginXYZ is not null)
        {
            r[OriginX] = *(uint*)&pForceOriginXYZ[0];
            r[OriginY] = *(uint*)&pForceOriginXYZ[1];
            r[OriginZ] = *(uint*)&pForceOriginXYZ[2];
        }
        r[PhysicsOnly] = (uint)bPhysicsOnly;

        return (int)r[Answer];
    }
}
