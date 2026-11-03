using SourceSharp.Abi;
using TF2.Shared;

namespace TF2.Server.Entities;

/// <summary>
/// The convars the control-point stack reads, as one seam: the capture area,
/// the points and the rules read these and nothing else, so a fact hands them
/// a record and the game hands them the convars.
/// </summary>
public interface IObjectiveSettings
{
    /// <summary><c>mp_capstyle</c>: 1 scales capture time with the cappers.</summary>
    int CapStyle { get; }

    /// <summary><c>mp_blockstyle</c>: 0 breaks a blocked capture, 1 pauses it.</summary>
    int BlockStyle { get; }

    /// <summary><c>mp_capdeteriorate_time</c>: seconds a scaled capture takes to fall back to nothing.</summary>
    float CapDeteriorateTime { get; }

    /// <summary><c>mp_simulatemultiplecappers</c>: each capper counts this many times.</summary>
    int SimulateMultipleCappers { get; }

    /// <summary><c>tf_caplinear</c>: teams must capture the points in order.</summary>
    bool CapLinear { get; }
}

/// <summary>The game's <see cref="IObjectiveSettings"/>: the convars, read as they stand.</summary>
[SideService(Side.Server, As = [typeof(IObjectiveSettings)])]
public sealed class ObjectiveConVarSettings(
    MpCapStyleConVar capStyle,
    MpBlockStyleConVar blockStyle,
    MpCapDeteriorateTimeConVar capDeteriorateTime,
    MpSimulateMultipleCappersConVar simulateMultipleCappers,
    TFCapLinearConVar capLinear) : IObjectiveSettings
{
    public int CapStyle => capStyle.Value;

    public int BlockStyle => blockStyle.Value;

    public float CapDeteriorateTime => capDeteriorateTime.Value;

    public int SimulateMultipleCappers => simulateMultipleCappers.Value;

    public bool CapLinear => capLinear.Value;
}
