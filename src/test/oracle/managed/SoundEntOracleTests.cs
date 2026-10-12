using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// The numbers the managed sound list shares with stock's soundent.h, read out
/// of stock rather than restated. This was SoundEntTests.SoundEntNumbersMatchTheHeader,
/// which read the header out of the farm; no binary compiles soundent.h since
/// plan_server_novalve.md P5 and it left the farm in P6, so the comparison
/// is against the pristine clone, where an oracle belongs.
/// </summary>
public class SoundEntOracleTests
{
    [Fact]
    public void SoundEntNumbersMatchStocksHeader()
    {
        string header = ValveSdk.Read("src/game/server/soundent.h");

        Assert.Contains("MAX_WORLD_SOUNDS_SP\t= " + SoundEnt.MaxWorldSoundsSp, header);
        Assert.Contains("MAX_WORLD_SOUNDS_MP\t= " + SoundEnt.MaxWorldSoundsMp, header);
        Assert.Contains("SOUNDLIST_EMPTY = " + SoundEnt.ListEmpty, header);
        Assert.Contains("SOUND_DANGER\t\t\t= 0x00000008", header);
        Assert.Contains("SOUNDENT_CHANNEL_UNSPECIFIED = " + SoundChannel.Unspecified, header);
    }
}
