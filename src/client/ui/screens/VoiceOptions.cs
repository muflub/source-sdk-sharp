using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The voice options (plan_gameui_managed.md G2; stock: GameUI's voice page):
/// voice on or off (voice_modenable), how loud others are (voice_scale), and
/// the microphone's volume and boost through the engine's voice tweak
/// controls (written only when the engine has them). The microphone test is not offered (D4).
/// </summary>
public sealed partial class VoiceOptions(UiWindows windows, IUiConVarStore convars, IMicrophoneSettings microphone)
    : UiForm<VoiceOptions.Model>(windows, convars)
{
    [UiFormModel]
    public sealed partial class Model
    {
        [Field("#SS_UI_Voice_Enable", Section = "#SS_UI_Voice_Chat")]
        public bool Enabled = true;

        [Field("#SS_UI_Voice_Receive", Min = 0f, Max = 1f, Step = 0.05f, Section = "#SS_UI_Voice_Chat")]
        public float Receive = 1f;

        [Field("#SS_UI_Voice_Microphone", Min = 0f, Max = 1f, Step = 0.05f, Section = "#SS_UI_Voice_Mic")]
        public float Microphone = 1f;

        [Field("#SS_UI_Voice_Boost", Section = "#SS_UI_Voice_Mic")]
        public bool Boost;
    }

    public override string Name => "voiceoptions";

    public override string WindowId => "voiceoptions";

    protected override string TitleToken => "#SS_UI_Voice_Title";

    protected override void Bind(UiFormBindings<Model> b)
        => b.ConVar(Model.Fields.Enabled, "voice_modenable")
            .ConVar(Model.Fields.Receive, "voice_scale")
            .Custom(Model.Fields.Microphone, m => m.Microphone = microphone.Volume,
                    m => { if (microphone.Available) microphone.Volume = m.Microphone; })
            .Custom(Model.Fields.Boost, m => m.Boost = microphone.Boost,
                    m => { if (microphone.Available) microphone.Boost = m.Boost; });
}
