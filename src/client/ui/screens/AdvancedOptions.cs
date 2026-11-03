using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The advanced options (stock: GameUI's COptionsSubKeyboardAdvancedDlg, behind
/// the keyboard page's Advanced button; a tab here, where it can be found): the
/// developer console (con_enable, without which the console key does nothing)
/// and fast weapon switching (hud_fastswitch). On is any non-zero value, as
/// stock reads them.
/// </summary>
public sealed partial class AdvancedOptions(UiWindows windows, IUiConVarStore convars) : UiForm<AdvancedOptions.Model>(windows, convars)
{
    [UiFormModel]
    public sealed partial class Model
    {
        [Field("#SS_UI_Advanced_Console")]
        public bool DeveloperConsole;

        [Field("#SS_UI_Advanced_FastSwitch")]
        public bool FastSwitch;
    }

    public override string Name => "advancedoptions";

    public override string WindowId => "advancedoptions";

    protected override string TitleToken => "#SS_UI_Advanced_Title";

    public override string TabToken => "#SS_UI_Advanced_Tab";

    protected override void Bind(UiFormBindings<Model> b)
        => b.ConVar(Model.Fields.DeveloperConsole, "con_enable")
            .ConVar(Model.Fields.FastSwitch, "hud_fastswitch");
}
