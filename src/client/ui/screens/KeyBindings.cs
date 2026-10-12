using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// The key bindings (stock: GameUI's COptionsSubKeyboard over kb_act.lst):
/// each row a command, its field the key bound to it. Clicked, a field takes
/// the next key or mouse button; Esc gives up, Delete clears. The form that
/// proves the key capture kind (plan_client_ui §6).
/// </summary>
public sealed partial class KeyBindings(UiWindows windows, IUiKeyBindingStore keys)
    : UiForm<KeyBindings.Model>(windows, null, keys)
{
    [UiFormModel]
    public sealed partial class Model
    {
        [Field("#SS_UI_Keys_Forward", Section = "#SS_UI_Keys_Movement")] public KeyBinding Forward;
        [Field("#SS_UI_Keys_Back", Section = "#SS_UI_Keys_Movement")] public KeyBinding Back;
        [Field("#SS_UI_Keys_Left", Section = "#SS_UI_Keys_Movement")] public KeyBinding Left;
        [Field("#SS_UI_Keys_Right", Section = "#SS_UI_Keys_Movement")] public KeyBinding Right;
        [Field("#SS_UI_Keys_Jump", Section = "#SS_UI_Keys_Movement")] public KeyBinding Jump;
        [Field("#SS_UI_Keys_Duck", Section = "#SS_UI_Keys_Movement")] public KeyBinding Duck;
        [Field("#SS_UI_Keys_Sprint", Section = "#SS_UI_Keys_Movement")] public KeyBinding Sprint;

        [Field("#SS_UI_Keys_Attack", Section = "#SS_UI_Keys_Combat")] public KeyBinding Attack;
        [Field("#SS_UI_Keys_Attack2", Section = "#SS_UI_Keys_Combat")] public KeyBinding Attack2;
        [Field("#SS_UI_Keys_Reload", Section = "#SS_UI_Keys_Combat")] public KeyBinding Reload;
        [Field("#SS_UI_Keys_Use", Section = "#SS_UI_Keys_Combat")] public KeyBinding Use;
        [Field("#SS_UI_Keys_LastWeapon", Section = "#SS_UI_Keys_Combat")] public KeyBinding LastWeapon;

        [Field("#SS_UI_Keys_Chat", Section = "#SS_UI_Keys_Talk")] public KeyBinding Chat;
        [Field("#SS_UI_Keys_TeamChat", Section = "#SS_UI_Keys_Talk")] public KeyBinding TeamChat;
        [Field("#SS_UI_Keys_Voice", Section = "#SS_UI_Keys_Talk")] public KeyBinding Voice;
        [Field("#SS_UI_Keys_Scores", Section = "#SS_UI_Keys_Talk")] public KeyBinding Scores;
    }

    public override string Name => "keybindings";

    public override string WindowId => "keybindings";

    protected override string TitleToken => "#SS_UI_Keys_Title";

    protected override UiWindowSpec Spec(float screenW, float screenH)
        => new(MathF.Min(640, screenW), MathF.Min(720, screenH), 420, 320, screenW, screenH, 520);

    protected override void Bind(UiFormBindings<Model> b)
        => b.Key(Model.Fields.Forward, "+forward")
            .Key(Model.Fields.Back, "+back")
            .Key(Model.Fields.Left, "+moveleft")
            .Key(Model.Fields.Right, "+moveright")
            .Key(Model.Fields.Jump, "+jump")
            .Key(Model.Fields.Duck, "+duck")
            .Key(Model.Fields.Sprint, "+speed")
            .Key(Model.Fields.Attack, "+attack")
            .Key(Model.Fields.Attack2, "+attack2")
            .Key(Model.Fields.Reload, "+reload")
            .Key(Model.Fields.Use, "+use")
            .Key(Model.Fields.LastWeapon, "lastinv")
            .Key(Model.Fields.Chat, "messagemode")
            .Key(Model.Fields.TeamChat, "messagemode2")
            .Key(Model.Fields.Voice, "+voicerecord")
            .Key(Model.Fields.Scores, "+showscores");
}
