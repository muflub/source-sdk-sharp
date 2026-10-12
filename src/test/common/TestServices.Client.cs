extern alias client;

namespace SourceSharp.Tests;

/// <summary>The client half of <see cref="TestServices"/>.</summary>
public static partial class TestServices
{
    /// <summary>
    /// The client's game assembly, where a client registry's scan starts: its
    /// own declarations and, compiled into it, the shared source's
    /// (plan_side_isolation P7).
    /// </summary>
    public static System.Reflection.Assembly ClientGame => typeof(client::SourceSharp.Client.ClientModule).Assembly;
}
