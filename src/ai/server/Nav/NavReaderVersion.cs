using SourceSharp.MapFormats.Nav;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// The version of map-tools' <c>SourceSharp.MapFormats</c> the binding runs
/// (plan_hl2mp_ai §6.1): what the derived graph's cache header names, so a
/// graph derived under another reader is rebuilt.
/// </summary>
public static class NavReaderVersion
{
    /// <summary>major.minor.patch of the reader assembly.</summary>
    public static string Package
    {
        get
        {
            Version v = typeof(Nav3dReader).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }
}
