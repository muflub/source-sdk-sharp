using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class AudioOptionsSamples
{
    private static UiScreen Build(UiSampleServices s, MemoryConVarStore store)
    {
        s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IUiConVarStore>(store);
        s.Add<IUiCommandLine>(new RecordingCommandLine());
        return s.Create<AudioOptions>();
    }

    [UiSample("stock settings")]
    private static UiScreen Stock(UiSampleServices s) => Build(s, OptionsSamples.Convars());

    [UiSample("captions only, low quality, muted")]
    private static UiScreen Quiet(UiSampleServices s) => Build(s, OptionsSamples.Convars()
        .With("volume", "0.1").With("cc_subtitles", "1").With("Snd_PitchQuality", "0").With("dsp_slow_cpu", "1"));
}
