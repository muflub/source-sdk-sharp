using System.Runtime.InteropServices;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two blobs of a managed server command's engine side
/// (plan_server_native_v2 T4g) lie where the COMPILE_TIME_ASSERTs beside
/// SourceSharp_PlayerCommandBegin / _Finish say they do.
/// </summary>
public sealed class PlayerCommandBlobsTests
{
    [Fact]
    public void TheFactsAreEightyBytesWithTheNativeOffsets()
        => Assert.True(Marshal.SizeOf<PlayerCommandFacts>() == PlayerCommandFacts.SizeInBytes
                       && PlayerCommandFacts.SizeInBytes == 80
                       && (int)Marshal.OffsetOf<PlayerCommandFacts>(nameof(PlayerCommandFacts.Origin)) == 8
                       && (int)Marshal.OffsetOf<PlayerCommandFacts>(nameof(PlayerCommandFacts.FixAngle)) == 44
                       && (int)Marshal.OffsetOf<PlayerCommandFacts>(nameof(PlayerCommandFacts.EntIndex)) == 60
                       && (int)Marshal.OffsetOf<PlayerCommandFacts>(nameof(PlayerCommandFacts.GroundVelocity)) == 68,
                       "SourceSharpPlayerCommandFacts: 80 bytes, origin 8, fixangle 44, index 60, ground velocity 68");

    [Fact]
    public void TheResultIsSeventySixBytesWithTheNativeOffsets()
        => Assert.True(Marshal.SizeOf<PlayerCommandResult>() == PlayerCommandResult.SizeInBytes
                       && PlayerCommandResult.SizeInBytes == 76
                       && (int)Marshal.OffsetOf<PlayerCommandResult>(nameof(PlayerCommandResult.Yaw)) == 20
                       && (int)Marshal.OffsetOf<PlayerCommandResult>(nameof(PlayerCommandResult.ViewOffset)) == 32
                       && (int)Marshal.OffsetOf<PlayerCommandResult>(nameof(PlayerCommandResult.Ground)) == 68,
                       "SourceSharpPlayerCommandResult: 76 bytes, yaw 20, view offset 32, ground 68");
}
