using SourceSharp.Abi;
using SourceSharp.Client.Effects;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// ParticleBinding's arms the recording (ParticleManagerOracleTests) does not
/// reach, held to particlemgr.cpp as written. Every local-space binding recorded
/// set its box by hand, so TransformAABB is held here.
/// </summary>
public class ParticleBindingTests
{
    private static ParticleBinding WithParticles(params (int Id, Vector Pos)[] particles)
    {
        var b = new ParticleBinding();
        foreach ((int id, Vector pos) in particles)
            b.AddParticle(0, id, pos);
        return b;
    }

    [Fact]
    public void ANewBindingHasTheConstructorsFlagsAndBox()
    {
        var b = new ParticleBinding();
        Assert.Equal(3484, (int)b.Flags);
        Assert.Equal((-50f, 50f, 0f), (b.Min.X, b.Max.Z, b.CullRadius));
    }

    // TransformAABB: centre through the matrix, extents through its absolute rows.
    [Fact]
    public void AFullUpdateInALocalSpaceTakesTheBoxThroughTheTransform()
    {
        ParticleBinding b = WithParticles((1, new Vector(1, 0, 0)), (2, new Vector(3, 2, 0)));
        b.SetLocalSpaceTransform([0, -1, 0, 10, 1, 0, 0, 20, 0, 0, 1, 30]);
        Assert.False(b.LocalSpaceTransformIdentity);
        Assert.True(b.EndBBoxUpdate(Vector.Zero));
        Assert.Equal((8f, 21f, 30f), (b.Min.X, b.Min.Y, b.Min.Z));
        Assert.Equal((10f, 23f, 30f), (b.Max.X, b.Max.Y, b.Max.Z));
    }

    [Fact]
    public void TheIdentityMatrixIsTheIdentitySpace()
    {
        var b = new ParticleBinding();
        b.SetLocalSpaceTransform([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0]);
        Assert.True(b.LocalSpaceTransformIdentity);
    }

    [Fact]
    public void ABoxSetByHandIsNotRecomputed()
    {
        ParticleBinding b = WithParticles((1, new Vector(100, 100, 100)));
        b.SetBBox(new Vector(-1, -1, -1), new Vector(1, 1, 1), disableAutoUpdate: true);
        Assert.False(b.EndBBoxUpdate(Vector.Zero));
        Assert.Equal(1f, b.Max.X);
    }

    [Fact]
    public void DrawingBeforeTheViewModelTakesItOutOfTheLeafSystem()
    {
        var b = new ParticleBinding();
        b.SetDrawBeforeViewModel(true);
        Assert.False(b.Has(ParticleBindingFlags.DrawThruLeafSystem));
        Assert.True(b.Has(ParticleBindingFlags.DrawBeforeViewModel));
    }

    // Keys are view-space z (more negative is farther): the bucket sort leaves them far to near.
    [Fact]
    public void ABucketSortLeavesTheListFarToNear()
    {
        ParticleBinding b = WithParticles((1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero));
        // List order is 3, 2, 1 (each linked at the head).
        b.SortAfterDraw(0, [-10f, -50f, -30f], bucketSort: true);
        Assert.Equal([2, 1, 3], b.Materials[0].Select(p => p.Id));
    }

    // GetNext's one pass: a particle whose key is below the last kept key steps back one place.
    [Fact]
    public void ADrawWithoutTheBucketSortSwapsOnePlaceBack()
    {
        ParticleBinding b = WithParticles((1, Vector.Zero), (2, Vector.Zero), (3, Vector.Zero));
        b.SortAfterDraw(0, [1f, 3f, 2f], bucketSort: false);
        Assert.Equal([3, 1, 2], b.Materials[0].Select(p => p.Id));
    }

    [Fact]
    public void PostRenderCarriesTheDrawAndEndsTheFirstFrame()
    {
        var b = new ParticleBinding();
        b.Set(ParticleBindingFlags.Drawn, true);
        b.PostRender();
        Assert.True(b.Has(ParticleBindingFlags.DrawnPrevFrame));
        Assert.False(b.Has(ParticleBindingFlags.FirstFrame));
    }
}
