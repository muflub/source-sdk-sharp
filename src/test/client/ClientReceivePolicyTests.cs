using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The render policy's projected-texture receive rule (Q2 of
/// docs/plans/c7_shadow_manager.md, user ruling 2026-10-19 "match stock"):
/// stock's <c>C_BaseEntity::ShouldReceiveProjectedTextures</c>
/// (c_baseentity.cpp, the fork's 1458-1481), each arm on its own.
/// </summary>
public class ClientReceivePolicyTests
{
    private const int Flashlight = 1;
    private const int Shadow = 2;
    private const int Both = Flashlight | Shadow;
    private const int Brush = 1;

    private static ClientRenderFacts Facts(int modelType = ClientRenderFacts.Studio, bool noDraw = false, bool noReceiveShadow = false,
                                           int renderMode = 0, int alpha = 255) =>
        new() { ModelType = modelType, NoDraw = noDraw, NoReceiveShadow = noReceiveShadow, RenderMode = renderMode, RenderAlpha = alpha };

    [Fact]
    public void NothingThatIsNotDrawnTakesEither()
    {
        // if ( IsEffectActive( EF_NODRAW ) ) return false;  -- ahead of both kinds
        Assert.False(ClientRenderPolicy.ReceivesProjectedTextures(Facts(noDraw: true), Flashlight));
        Assert.False(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: Brush, noDraw: true), Shadow));
    }

    [Fact]
    public void AFlashlightLightsAStudioModelAndABrushModel()
    {
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(), Flashlight));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: Brush), Flashlight));
    }

    [Fact]
    public void AFlashlightSkipsOnlyAnInvisibleBlendedEntity()
    {
        // GetRenderMode() > kRenderNormal && GetRenderColor().a == 0
        Assert.False(ClientRenderPolicy.ReceivesProjectedTextures(Facts(renderMode: 2, alpha: 0), Flashlight));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(renderMode: 0, alpha: 0), Flashlight));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(renderMode: 2, alpha: 1), Flashlight));
    }

    [Fact]
    public void AMaskOfBothKindsIsAskedAsAFlashlight()
    {
        // The flashlight bit is tested first: the shadow manager asks with both,
        // and a studio model then takes it although it takes no blob shadow.
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(), Both));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(noReceiveShadow: true), Both));
    }

    [Fact]
    public void ABlobShadowLandsOnAnythingButAStudioModel()
    {
        Assert.False(ClientRenderPolicy.ReceivesProjectedTextures(Facts(), Shadow));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: Brush), Shadow));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: ClientRenderFacts.NoModel), Shadow));
    }

    [Fact]
    public void NoReceiveShadowRefusesABlobAndNotAFlashlight()
    {
        Assert.False(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: Brush, noReceiveShadow: true), Shadow));
        Assert.True(ClientRenderPolicy.ReceivesProjectedTextures(Facts(modelType: Brush, noReceiveShadow: true), Flashlight));
    }
}
