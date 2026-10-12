using System.Globalization;
using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// Where the local view is, this frame, for the HUD's debug line.
///
/// <para>
/// A screenshot is only evidence when it says WHEN and WHERE it was taken: with
/// the engine clock and the position on it, a picture can be matched to a log
/// line, a per-frame trace or a devapi reading. The prediction writes this each
/// frame from the view it publishes; the HUD reads it. One instance, from the
/// container.
/// </para>
/// </summary>
public sealed class ViewReadout
{
    /// <summary>Whether anything has been published since load.</summary>
    public bool Known { get; private set; }

    /// <summary>The eye: the published origin plus the view offset.</summary>
    public Vector Eye { get; private set; }

    /// <summary>The view angles of the last replayed command.</summary>
    public float Pitch { get; private set; }

    public float Yaw { get; private set; }

    public void PublishEye(Vector eye)
    {
        Eye = eye;
        Known = true;
    }

    public void PublishAngles(float pitch, float yaw)
    {
        Pitch = pitch;
        Yaw = yaw;
    }

    /// <summary>
    /// The HUD line: engine time, eye position, pitch and yaw. Invariant culture
    /// so a screenshot from any locale reads the same.
    /// </summary>
    public static string Line(float time, Vector eye, float pitch, float yaw)
        => string.Create(CultureInfo.InvariantCulture,
               $"t {time:F3}  pos {eye.X:F1} {eye.Y:F1} {eye.Z:F1}  ang {pitch:F1} {yaw:F1}");
}
