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
public sealed partial class AdvancedOptions(UiWindows windows, IUiConVarStore convars, IAdminKeySource adminKey) : UiForm<AdvancedOptions.Model>(windows, convars)
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

    private string _keyInput = "";

    /// <summary>The admin key: its public half to give a server, a way to make one if there is none, and a way to set one.</summary>
    protected override void DescribeMore(ref UiFrame ui)
    {
        using (ui.Column(Space.S, Align.Start).Width(Size.Fill).Name("adminkey"))
        {
            ui.Text("#SS_UI_Advanced_AdminKey", Text.Label, Tone.Accent);
            if (adminKey.HasKey)
            {
                ui.Text("#SS_UI_Advanced_AdminPublic", Text.Body, Tone.Text).Wrap(KeyWrapDu);
                ui.Data(adminKey.PublicKey, Text.Caption, Tone.TextMuted).Wrap(KeyWrapDu);
                ui.Data(adminKey.PublicKeyFile, Text.Caption, Tone.TextMuted).Wrap(KeyWrapDu);
            }
            else
            {
                ui.Text("#SS_UI_Advanced_AdminNone", Text.Body, Tone.Text).Wrap(KeyWrapDu);
                if (ui.Button("adminkeygen", "#SS_UI_Advanced_AdminGenerate").Clicked)
                    adminKey.Generate();
            }
            using (ui.Row(Space.S).Width(Size.Fill))
            {
                _keyInput = ui.EditText("adminkeyset", _keyInput, "#SS_UI_Advanced_AdminSetHint");
                if (ui.Button("adminkeytake", "#SS_UI_Advanced_AdminSet", enabled: _keyInput.Length > 0).Clicked)
                {
                    adminKey.Set(_keyInput);
                    // It may be the secret itself: it does not stay on the screen.
                    _keyInput = "";
                }
            }
            if (adminKey.Problem is { } problem)
                ui.Text(problem, Text.Caption, Tone.Danger).Name("adminkeyproblem");
        }
    }

    private const float KeyWrapDu = 520;
}
