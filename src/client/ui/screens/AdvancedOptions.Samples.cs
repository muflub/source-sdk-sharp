using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class AdvancedOptionsSamples
{
    private static UiScreen Build(UiSampleServices s, MemoryConVarStore store)
    {
        if (!s.Has<IUiWindowStore>())
            s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IUiConVarStore>(store);
        return s.Create<AdvancedOptions>();
    }

    [UiSample("stock settings")]
    private static UiScreen Stock(UiSampleServices s)
        => Build(s, new MemoryConVarStore().With("con_enable", "0").With("hud_fastswitch", "0"));

    [UiSample("console enabled, fast switch")]
    private static UiScreen Enabled(UiSampleServices s)
        => Build(s, new MemoryConVarStore().With("con_enable", "1").With("hud_fastswitch", "1"));
}
