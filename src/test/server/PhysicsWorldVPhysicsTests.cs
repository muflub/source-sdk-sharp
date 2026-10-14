using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The world build's engine half (plan_physics_bones_native_zero 2.6): stock's
/// CSolidSetDefaults as SolidKeyDefaults, the solid_t writes the adapter makes
/// without vphysics, and the fluid_t / surfacedata_t offsets it reads and writes
/// (vtable_index_agreement_test's WorldParamsLayout pins the same numbers
/// against the headers).
/// </summary>
public unsafe class PhysicsWorldVPhysicsTests
{
    private sealed class Keys(nint defaultName) : ISolidKeyTable
    {
        public nint Table => 0;
        public SolidKeyDefaults Solids { get; } = new(defaultName);
        public bool SolidInFlight { get; set; }
    }

    private static void Key(SolidKeyDefaults solids, string key, string value)
    {
        byte[] k = System.Text.Encoding.UTF8.GetBytes(key + "\0"), v = System.Text.Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* pk = k)
        fixed (byte* pv = v)
            solids.ParseKeyValue(pk, pv);
    }

    [Fact]
    public void ASolidsDefaultsAreStocksObjectParamsAndContentsSolid()
    {
        var solids = new SolidKeyDefaults(0x5150);
        byte* solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);

        try
        {
            Key(solids, "contents", "33");
            solids.SetDefaults(solid);
            ObjectParams p = *(ObjectParams*)(solid + SolidLayout.Params);

            Assert.True(p.Mass == 1f && p.Inertia == 1f && p.Damping == 0.1f && p.RotDamping == 0.1f);
            Assert.True(p.RotInertiaLimit == 0.05f && p.DragCoefficient == 1f && p.Volume == 0f);
            Assert.Equal((nint)0x5150, p.Name);
            Assert.Equal(1, p.EnableCollisions);
            Assert.Equal(SolidKeyDefaults.ContentsSolid, solids.ContentsMask);
        }
        finally
        {
            NativeMemory.Free(solid);
        }
    }

    [Fact]
    public void TheContentsKeyIsReadAnyCaseAndOtherKeysAreIgnored()
    {
        var solids = new SolidKeyDefaults(0);
        solids.SetDefaults(null);

        Key(solids, "mass", "40");
        Assert.Equal(1u, solids.ContentsMask);

        Key(solids, "CONTENTS", "16385");
        Assert.Equal(16385u, solids.ContentsMask);
    }

    [Fact]
    public void ResetSolidZeroesItAndGivesItTheDefaults()
    {
        using var adapter = new PhysicsWorldVPhysics(() => 0, null, null, null, null, null, null, null, null, new Keys(0x77), TestVPhysics.None());
        byte* solid = (byte*)NativeMemory.Alloc(SolidLayout.Size);

        try
        {
            new Span<byte>(solid, SolidLayout.Size).Fill(0xCD);
            adapter.ResetSolid((nint)solid);

            Assert.Equal(0, *(int*)(solid + SolidLayout.Index));
            Assert.Equal(0, solid[SolidLayout.SurfaceProp]);
            ObjectParams p = *(ObjectParams*)(solid + SolidLayout.Params);
            Assert.Equal((nint)0x77, p.Name);
            Assert.Equal(1f, p.Mass);

            adapter.SetSolidEnableCollisions((nint)solid, false);
            Assert.Equal(0, ((ObjectParams*)(solid + SolidLayout.Params))->EnableCollisions);
            adapter.SetSolidEnableCollisions((nint)solid, true);
            Assert.Equal(1, ((ObjectParams*)(solid + SolidLayout.Params))->EnableCollisions);
        }
        finally
        {
            NativeMemory.Free(solid);
        }
    }

    [Fact]
    public void NativeTextIsOneCopyPerName()
    {
        using var adapter = new PhysicsWorldVPhysics(() => 0, null, null, null, null, null, null, null, null, new Keys(0), TestVPhysics.None());

        nint world = adapter.NativeText("world");

        Assert.Equal(world, adapter.NativeText("world"));
        Assert.NotEqual(world, adapter.NativeText("fluid"));
        Assert.Equal("world", Marshal.PtrToStringUTF8(world));
    }

    [Fact]
    public void TheFluidAndSurfaceOffsetsAreTheHeaders()
    {
        Assert.Equal((0, 4, 512, 520, 40, 576),
                     (FluidLayout.Index, FluidLayout.SurfaceProp, FluidLayout.SurfacePropCapacity, FluidLayout.Params,
                      FluidLayout.ParamsGameData, FluidLayout.Size));
        Assert.Equal((44, 76), (SurfaceDataLayout.SoundNames, SurfaceDataLayout.SoundHandles));
        Assert.Equal(0x0200, PhysicsWorldVPhysics.CallbackNeverDeleted);
    }
}
