using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>Stock's m_customaccel modes, 0..3 in order.</summary>
public enum MouseAcceleration
{
    Off,
    Custom,
    PowerOnly,
    CustomAndPower,
}

/// <summary>
/// The mouse options (stock: GameUI's COptionsSubMouse), the form that proves
/// the form layer (plan_client_ui §8 U4): sensitivity a slider, reverse,
/// raw input and filter toggles, acceleration a choice; each a convar but
/// reverse, which is m_pitch's sign.
/// </summary>
public sealed partial class MouseOptions(UiWindows windows, IUiConVarStore convars) : UiForm<MouseOptions.Model>(windows, convars)
{
    [UiFormModel]
    public sealed partial class Model
    {
        [Field("#SS_UI_Mouse_Sensitivity", Min = 0.1f, Max = 20f, Step = 0.1f, Section = "#SS_UI_Mouse_Look")]
        public float Sensitivity = 3f;

        [Field("#SS_UI_Mouse_Invert", Section = "#SS_UI_Mouse_Look")]
        public bool Reverse;

        [Field("#SS_UI_Mouse_RawInput", Section = "#SS_UI_Mouse_Feel")]
        public bool RawInput = true;

        [Field("#SS_UI_Mouse_Filter", Section = "#SS_UI_Mouse_Feel")]
        public bool Filter;

        [Field("#SS_UI_Mouse_Accel", Section = "#SS_UI_Mouse_Feel")]
        public MouseAcceleration Acceleration;
    }

    private readonly IUiConVarStore _convars = convars;

    public override string Name => "mouseoptions";

    public override string WindowId => "mouseoptions";

    protected override string TitleToken => "#SS_UI_Mouse_Title";

    protected override void Bind(UiFormBindings<Model> b)
        => b.ConVar(Model.Fields.Sensitivity, "sensitivity")
            .ConVar(Model.Fields.RawInput, "m_rawinput")
            .ConVar(Model.Fields.Filter, "m_filter")
            .ConVar(Model.Fields.Acceleration, "m_customaccel")
            .Custom(Model.Fields.Reverse, LoadReverse, SaveReverse);

    // Reverse is m_pitch's sign: stock's options flip it and keep its size.
    private void LoadReverse(Model m)
        => m.Reverse = float.TryParse(_convars.Get("m_pitch"), System.Globalization.NumberStyles.Float,
                                      System.Globalization.CultureInfo.InvariantCulture, out float p) && p < 0f;

    private void SaveReverse(Model m)
    {
        float pitch = float.TryParse(_convars.Get("m_pitch"), System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out float p) ? MathF.Abs(p) : 0.022f;
        _convars.Set("m_pitch", (m.Reverse ? -pitch : pitch).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Below 0.5 the view barely turns: allowed by the engine, refused here.</summary>
    protected override string? Validate(int field, Model m)
        => field == Model.Fields.Sensitivity && m.Sensitivity < 0.5f ? "#SS_UI_Mouse_TooSlow" : null;
}
