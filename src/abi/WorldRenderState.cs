namespace SourceSharp.Abi;

/// <summary>
/// Which piece of the client's world render state is being set. Mirrors
/// SourceSharpWorldRenderState.
///
/// <para>
/// Each value names a stock map entity whose entire purpose is to hold numbers
/// the renderer reads every frame. The entity itself — its keys, its inputs,
/// when the numbers change — is C#'s; this is the one place the numbers cross
/// back into native code.
/// </para>
/// </summary>
public enum WorldRenderState
{
    /// <summary>dirX, dirY, dirZ, r, g, b, maxDist, disabled.</summary>
    Shadow = 0,

    /// <summary>cheapStart, cheapEnd.</summary>
    WaterLod = 1,

    /// <summary>
    /// useExposureMin, useExposureMax, useBloomScale, exposureMin,
    /// exposureMax, bloomScale, bloomScaleMinimum.
    /// </summary>
    Tonemap = 2,
}
