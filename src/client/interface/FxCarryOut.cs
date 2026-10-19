using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

// (IFxCarryOut, the FX_* routine an IEffects call still ended in natively,
// left with lane c5-particles: its last routine, FX_Dust, is C#, DustPuff.)

/// <summary>
/// fx.cpp's <c>CImpactOverlay</c> (<c>FX_GunshipImpact</c>), carried out
/// natively: a warp overlay the glow system holds and updates each frame.
/// </summary>
public interface IFxImpactOverlay
{
    /// <summary>One overlay at <paramref name="position"/> (already off the surface) in <paramref name="color"/>.</summary>
    void ImpactOverlay(Vector position, Vector color);
}

/// <summary>
/// What stock's <c>SuppressTE</c> reads, answered natively: whether the
/// effects system may predict right now (<c>IPredictionSystem::CanPredict</c>,
/// false during a re-prediction), and how many recipients a client PVS filter
/// holds (the local viewer, or nobody before there is one).
/// </summary>
public interface IFxSuppression
{
    /// <summary><c>CanPredict()</c> on the client's IEffects.</summary>
    bool CanPredict();

    /// <summary><c>CPVSFilter( origin ).GetRecipientCount()</c> on the client.</summary>
    int RecipientCount(Vector origin);
}
