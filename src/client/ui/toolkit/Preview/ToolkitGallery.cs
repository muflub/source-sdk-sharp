using SourceSharp.Client.Ui;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// The toolkit's own parts in the gallery (plan_client_ui §5.8.6): each
/// composite in its states, so an agent picks parts by looking. Parts that need
/// game data (TeamTable, HudMeter) are in the screens assembly's gallery.
/// </summary>
internal static class ToolkitGallery
{
    [UiGallery("Button", "default, primary")]
    private static UiScreen Buttons(UiSampleServices s) => new UiPartPreview("Button", static (ref UiFrame ui) =>
    {
        using (ui.Row(Space.M))
        {
            ui.Button("cancel", "#SS_UI_Cancel");
            ui.Button("join", "#SS_UI_TeamMenu_Join", primary: true);
        }
    });

    [UiGallery("KeyHint", "use and reload")]
    private static UiScreen KeyHints(UiSampleServices s) => new UiPartPreview("KeyHint", static (ref UiFrame ui) =>
    {
        using (ui.Panel())
        {
            ui.KeyHintData("E", "Use the charger");
            ui.KeyHintData("R", "Reload");
        }
    }, UiLayerKind.Hud);

    [UiGallery("StatBlock", "score row")]
    private static UiScreen Stats(UiSampleServices s) => new UiPartPreview("StatBlock", static (ref UiFrame ui) =>
    {
        using (ui.Panel().Raise(Elevation.Raised))
        using (ui.Row(Space.XXL))
        {
            ui.StatBlock(27, "#SS_UI_Kills");
            ui.StatBlock(9, "#SS_UI_Deaths");
            ui.StatBlock(42, "#SS_UI_Ping", Tone.TextMuted);
        }
    });

    [UiGallery("Toast", "accent and danger")]
    private static UiScreen Toasts(UiSampleServices s) => new UiPartPreview("Toast", static (ref UiFrame ui) =>
    {
        using (ui.Column(Space.S, Align.Start))
        {
            ui.Toast("Picked up 45 SMG rounds");
            ui.Toast("Alyx killed you with a .357", Tone.Danger);
        }
    }, UiLayerKind.Hud);

    [UiGallery("Tabs", "second selected")]
    private static UiScreen TabRow(UiSampleServices s) => new UiPartPreview("Tabs", static (ref UiFrame ui) =>
    {
        using (ui.Panel())
            ui.Tabs(["#SS_UI_Players", "#SS_UI_Map", "#SS_UI_Score"], 1);
    });

    [UiGallery("Meter", "full, half, low")]
    private static UiScreen Meters(UiSampleServices s) => new UiPartPreview("Meter", static (ref UiFrame ui) =>
    {
        using (ui.Panel().Width(Size.Du(320)))
        {
            ui.Meter(1f, Tone.Accent, Size.Fill);
            ui.Meter(0.5f, Tone.Positive, Size.Fill);
            ui.Meter(0.15f, Tone.Danger, Size.Fill);
        }
    }, UiLayerKind.Hud);

    [UiGallery("EmptyState", "no players")]
    private static UiScreen Empty(UiSampleServices s) => new UiPartPreview("EmptyState", static (ref UiFrame ui) =>
    {
        using (ui.Panel().Width(Size.Du(480)))
            ui.EmptyState(UiShape.Person, "#SS_UI_Empty_Title", "#SS_UI_Empty_Body");
    });

    [UiGallery("Dialog", "title, body, buttons")]
    private static UiScreen DialogPart(UiSampleServices s) => new UiPartPreview("Dialog", static (ref UiFrame ui) =>
    {
        using (ui.Dialog("dialog", "#SS_UI_TeamMenu_Title", 560))
        {
            ui.Text("#SS_UI_TeamMenu_Subtitle", Text.Body, Tone.TextMuted);
            using (ui.DialogButtons())
            {
                ui.Button("cancel", "#SS_UI_Cancel");
                ui.Button("join", "#SS_UI_TeamMenu_Join", primary: true);
            }
        }
    });

    [UiGallery("Icons", "the shape set")]
    private static UiScreen Icons(UiSampleServices s) => new UiPartPreview("Icons", static (ref UiFrame ui) =>
    {
        using (ui.Panel())
        using (ui.Row(Space.XL))
        {
            ui.Icon(UiShape.Cross, Tone.Accent, 32);
            ui.Icon(UiShape.Shield, Tone.Accent, 32);
            ui.Icon(UiShape.Bullet, Tone.Accent, 32);
            ui.Icon(UiShape.Signal, Tone.Text, 32).Name("signal");
            ui.Icon(UiShape.Dot, Tone.Text, 32);
            ui.Icon(UiShape.Chevron, Tone.Text, 32);
            ui.Icon(UiShape.Cross45, Tone.Danger, 32);
            ui.Icon(UiShape.Crosshair, Tone.Text, 32);
            ui.Icon(UiShape.Person, Tone.Text, 32);
            ui.Icon(UiShape.Check, Tone.Positive, 32);
            ui.Icon(UiShape.ChevronDown, Tone.Text, 32);
            ui.Icon(UiShape.Lock, Tone.TextMuted, 32);
            ui.Icon(UiShape.Star, Tone.Accent, 32);
        }
    }, UiLayerKind.Hud);

    [UiGallery("Fields", "text, checkbox, dropdown, progress")]
    private static UiScreen Fields(UiSampleServices s) => new UiPartPreview("Fields", static (ref UiFrame ui) =>
    {
        using (ui.Panel().Width(Size.Du(520)))
        {
            ui.TextField("map", "", "#SS_UI_Browser_Map_Placeholder");
            ui.TextField("typed", "dm_lock", "#SS_UI_Browser_Map_Placeholder", caret: 7, selStart: 3, selLength: 4);
            using (ui.Row(Space.L, Align.End))
            {
                ui.Checkbox("full", "#SS_UI_Browser_NotFull", true);
                ui.Checkbox("pw", "#SS_UI_Browser_NoPassword", false);
                ui.Dropdown("ping", "#SS_UI_Browser_MaxPing", "150 ms");
            }
            ui.Progress(0.42f, "#SS_UI_Browser_Refreshing");
        }
    });

    [UiGallery("Tooltip", "shown under a button")]
    private static UiScreen Tooltips(UiSampleServices s) => new UiPartPreview("Tooltip", static (ref UiFrame ui) =>
    {
        using (ui.Panel().Width(Size.Du(520)).Height(Size.Du(160)))
        {
            var box = ui.Box("auto").Size(Size.Du(200), Size.Du(40)).Fill(Tone.SurfaceRaised).Round(Radius.M);
            using (box)
                ui.Text("#SS_UI_TeamMenu_Join", Text.Heading).At(UiAnchor.Centre).TextAlign(Align.Centre);
            ui.Tooltip("auto-tip", box, hovered: true, "#SS_UI_Tooltip_AutoAssign", delaySeconds: 0f);
        }
    });
}
