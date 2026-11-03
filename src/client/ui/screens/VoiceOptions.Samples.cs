using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class VoiceOptionsSamples
{
    private static UiScreen Build(UiSampleServices s, MemoryConVarStore store, MemoryMicrophone microphone)
    {
        s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IUiConVarStore>(store);
        s.Add<IMicrophoneSettings>(microphone);
        return s.Create<VoiceOptions>();
    }

    [UiSample("voice on, boosted")]
    private static UiScreen On(UiSampleServices s) => Build(s, OptionsSamples.Convars(), new MemoryMicrophone { Boost = true });

    [UiSample("voice off")]
    private static UiScreen Off(UiSampleServices s)
        => Build(s, OptionsSamples.Convars().With("voice_modenable", "0").With("voice_scale", "0.3"), new MemoryMicrophone { Volume = 0f });
}
