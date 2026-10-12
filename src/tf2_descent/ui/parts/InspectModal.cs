using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace TF2.Descent.Client.Ui.Parts;

/// <summary>
/// The item inspect (docs/plans/descent_ui.md §7: the 3D rasterisation is
/// engine wiring, out of the UI's scope): a centred modal panel in the host
/// screen's <c>ui.Overlay("inspect", modal: true)</c> - the Dialog's title
/// bar in the item's rarity colour with its <see cref="RarityChip"/>, the
/// toolkit's tab language for the two views (the weapon alone, the weapon
/// mounted on the hero), and the two viewports as <c>ui.Texture</c>
/// surfaces: the pixels are the engine's, this part only declares which
/// surface sits where. A surface the engine has not sent leaves its
/// viewport an empty plate (the plate is the waiting state, never a fake
/// picture). Everything about the subject - the item, the view, the
/// character, even whether the modal is open - is the host's view-model
/// through <see cref="IDescentInspect"/>; the close button and Esc answer
/// <see cref="IDescentInspectActions"/>. Draws nothing while Showing is
/// false, so a screen can describe it unconditionally.
/// </summary>
public sealed class InspectModal : UiPanel
{
    private readonly IDescentInspect _inspect;
    private readonly IDescentInspectActions? _actions;
    private readonly RarityChip _chip = new();

    public InspectModal(IDescentInspect inspect, IDescentInspectActions? actions = null)
    {
        _inspect = inspect;
        _actions = actions;
    }

    public override void Describe(ref UiFrame ui)
    {
        if (!_inspect.Showing || _inspect.Item is not { } item)
            return;

        bool mounted = _inspect.ShowsMounted;
        var paint = DescentPaints.Of(item.Rarity);
        float w = MathF.Min(720, ui.DesignWidth - 2 * (float)Space.L);
        using (ui.Column(Space.M, Align.Stretch).At(UiAnchor.Centre).Width(Size.Du(w))
                   .Fill(Tone.Surface).Round(Radius.L).Raise(Elevation.Overlay).Clip())
        {
            // The title bar in the Dialog's language: the rarity chip, the
            // name in the rung's colour (game data: ui.Data, its case kept),
            // and the window close (the X, keyed, as the toolkit's windows
            // draw theirs).
            using (ui.Row(Space.S, Align.Centre).Width(Size.Fill))
            {
                ui.Sub("chip", _chip, item.Rarity);
                ui.Data(item.BaseName, Text.Heading, Tone.Text).Color(paint).Width(Size.Fill).Elide();
                var close = ui.Box("close").Size(Size.Du(32), Size.Du(32)).Round(Radius.M);
                var closeHit = ui.Interact(close);
                using (close.Fill(closeHit.Hovered ? ((UiPaint)Tone.Danger).WithOpacity(0.35f) : Tone.None))
                    ui.Shape(UiShape.Cross45, closeHit.Hovered ? Tone.Text : Tone.TextMuted, 12)
                      .At(UiAnchor.Centre);
                if (closeHit.Clicked)
                    _actions?.Close();
            }

            // The two views, in the toolkit's tab language (VendorTabRow's
            // cells: heading, accent underline when the view-model says it
            // is showing). The switch reports; the answer comes back through
            // IDescentInspect, so the part never keeps the state.
            using (ui.Row(Space.XL, Align.End).Width(Size.Fill))
            {
                for (int i = 0; i < 2; i++)
                {
                    bool on = (i == 1) == mounted;
                    var cell = ui.Column(Space.XS, Align.Stretch).Key(i);
                    var hit = ui.Interact(cell);
                    using (cell)
                    {
                        ui.Text(i == 0 ? "#Descent_Inspect_Tab_Weapon" : "#Descent_Inspect_Tab_Mounted",
                                Text.Heading, on ? Tone.Text : Tone.TextMuted);
                        ui.Rect(on ? Tone.Accent : Tone.None, Size.Fill, Size.Du(4));
                    }
                    if (hit.Clicked)
                        _actions?.ShowMounted(i == 1);
                }
            }

            // The two viewports: side by side, stacked when the host's
            // window is narrow. The selected view wears the accent frame.
            if (ui.Narrow)
            {
                using (ui.Column(Space.S, Align.Stretch))
                {
                    Viewport(ref ui, "view-weapon", "#Descent_Inspect_View_Weapon",
                             _inspect.HasWeaponSurface, _inspect.WeaponTexture, !mounted, null);
                    Viewport(ref ui, "view-mounted", "#Descent_Inspect_View_Mounted",
                             _inspect.HasMountedSurface, _inspect.MountedTexture, mounted,
                             mounted ? _inspect.CharacterName : "");
                }
            }
            else
            {
                using (ui.Row(Space.S, Align.Start))
                {
                    Viewport(ref ui, "view-weapon", "#Descent_Inspect_View_Weapon",
                             _inspect.HasWeaponSurface, _inspect.WeaponTexture, !mounted, null);
                    Viewport(ref ui, "view-mounted", "#Descent_Inspect_View_Mounted",
                             _inspect.HasMountedSurface, _inspect.MountedTexture, mounted,
                             mounted ? _inspect.CharacterName : "");
                }
            }
        }
    }

    /// <summary>One viewport plate: the caption, and the engine's surface
    /// when it has been sent; the empty plate is the waiting state.
    /// <paramref name="wearer"/> names the hero for the mounted viewport
    /// while that view shows (the caption says who wears it; the name is
    /// game data); null for the weapon's own plate.</summary>
    private static void Viewport(ref UiFrame ui, string key, string captionToken, bool hasSurface, int texture,
                                 bool selected, string? wearer)
    {
        using (ui.Column(Space.S, Align.Stretch).Size(Size.Fill, Size.Auto)
                   .Fill(Tone.SurfaceRaised).Border(selected ? Tone.Accent : Tone.Line, selected ? 2 : 1)
                   .Round(Radius.M).Pad(Space.S, Space.S).Key(key))
        {
            ui.Text(captionToken, Text.Caption, Tone.TextMuted).Elide();
            if (hasSurface)
                ui.Texture(texture, Size.Fill, Size.Du(220));
            if (wearer is { Length: > 0 })
                using (ui.Row(Space.XS, Align.Centre))
                {
                    ui.Text("#Descent_Inspect_Character", Text.Caption, Tone.TextMuted);
                    ui.Data(wearer, Text.Body, Tone.Text).Width(Size.Fill).Elide();
                }
        }
    }
}
