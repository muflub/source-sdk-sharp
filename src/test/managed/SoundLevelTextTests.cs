using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="SoundLevelText"/>, stock's <c>TextToSoundLevel</c>
/// (public/SoundParametersInternal.cpp): the named table, the
/// <c>SNDLVL_&lt;n&gt;</c> fallback, and the default.
/// </summary>
public class SoundLevelTextTests
{
    [Fact]
    public void ANamedLevelIsTheTablesNotItsDigits()
    {
        Assert.True(SoundLevelText.Parse("SNDLVL_TALKING") == 80, "TALKING");
        Assert.True(SoundLevelText.Parse("SNDLVL_GUNFIRE") == 140, "GUNFIRE");
        Assert.True(SoundLevelText.Parse("SNDLVL_STATIC") == 66, "STATIC");
        Assert.True(SoundLevelText.Parse("SNDLVL_IDLE") == 60, "IDLE");
        Assert.True(SoundLevelText.Parse("SNDLVL_NONE") == 0, "NONE");
    }

    [Fact]
    public void TheNameMatchesWhateverItsCase()
    {
        // soundscapes_streetwar.txt writes "SNDLVL_100db" and "SNDLVL_100dB".
        Assert.True(SoundLevelText.Parse("SNDLVL_100db") == 100, "SNDLVL_100db");
        Assert.True(SoundLevelText.Parse("sndlvl_norm") == 75, "sndlvl_norm");
    }

    [Fact]
    public void AnUnlistedLevelIsReadFromItsDigitsWithinOneTo180()
    {
        Assert.True(SoundLevelText.Parse("SNDLVL_97dB") == 97, "97dB");
        Assert.True(SoundLevelText.Parse("SNDLVL_181dB") == SoundLevelText.Normal, "181 is out of range");
        Assert.True(SoundLevelText.Parse("SNDLVL_0") == SoundLevelText.Normal, "0 is out of range");
    }

    [Fact]
    public void AnythingElseIsNormalAndSaysSo()
    {
        string? said = null;

        int level = SoundLevelText.Parse("SNDLVL_LOUD", m => said = m);

        Assert.True(level == 75 && said == "CSoundEmitterSystem:  Unknown sound level SNDLVL_LOUD\n", $"{level} {said}");
    }
}
