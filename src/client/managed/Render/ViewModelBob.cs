using System;

namespace SourceSharp.Client.Render;

/// <summary>
/// The viewmodel bob's state (stock's CalcViewModelBobHelper/
/// AddViewModelBobHelper pair): <see cref="Tick"/> advances it one frame and
/// writes the vertical (0) and lateral (1) magnitudes the native pin applies
/// to the viewmodel's origin and angles. The game owns the convars and the
/// speed; the plan only carries the answer to native.
/// </summary>
public interface IViewModelBob
{
    /// <summary>Advances the bob to <paramref name="curTime"/> and writes bob[0] vertical, bob[1] lateral.</summary>
    void Tick(float curTime, float frameTime, Span<float> bob);
}
