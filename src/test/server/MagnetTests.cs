using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a magnet holds on to.
///
/// The rule is worth testing because it is the one place the mod reads two
/// engine answers that can disagree and has to pick. Source gives every surface
/// a material character: one comes from the face that was actually struck, the
/// other from the model's own default. A metal crate with a painted texture
/// reports paint for the first and metal for the second, and accepting either
/// is what makes a magnet feel like it grabs the OBJECT rather than the texture.
/// </summary>
public class MagnetTests
{
    [Fact]
    public void MetalSticks()
        => Assert.True(PhysMagnet.Sticks(SurfaceMaterial.Metal, SurfaceMaterial.Metal), "metal sticks");

    [Fact]
    public void AComputerCasingCountsAsMetal()
        => Assert.True(PhysMagnet.Sticks(SurfaceMaterial.Computer, SurfaceMaterial.Unknown), "a computer casing counts as metal");

    // The disagreement case, both ways round.

    [Fact]
    public void APaintedMetalModelStillSticks()
        => Assert.True(PhysMagnet.Sticks(SurfaceMaterial.Wood, SurfaceMaterial.Metal), "a painted metal model still sticks");

    [Fact]
    public void AMetalFaceOnAWoodenModelStillSticks()
        => Assert.True(PhysMagnet.Sticks(SurfaceMaterial.Metal, SurfaceMaterial.Wood), "a metal face on a wooden model still sticks");

    [Fact]
    public void WoodDoesNotStick()
        => Assert.True(!PhysMagnet.Sticks(SurfaceMaterial.Wood, SurfaceMaterial.Wood), "wood does not stick");

    [Fact]
    public void GlassDoesNotStick()
        => Assert.True(!PhysMagnet.Sticks(SurfaceMaterial.Glass, SurfaceMaterial.Wood), "glass does not stick");

    [Fact]
    public void AnUnknownSurfaceDoesNotStick()
    {
        // Unknown is what arrives when the engine could not answer at all —
        // a brush with no surfaceprops, a model with no default. It must not
        // read as metal, or a magnet would pick up anything it failed to
        // identify.
        Assert.True(!PhysMagnet.Sticks(SurfaceMaterial.Unknown, SurfaceMaterial.Unknown), "an unknown surface does not stick");
    }
}
