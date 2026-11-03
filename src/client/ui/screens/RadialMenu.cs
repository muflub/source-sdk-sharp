using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>
/// A popup ring of choices round the screen's centre, over play: the pointer's
/// direction lights the nearest and a click takes it, or its number does, and
/// Escape or the hub goes back. One screen, always open and drawn only while
/// its source has a menu up, for whatever owns one: voice commands, calling a
/// vote, the admin's choices.
/// </summary>
public sealed class RadialMenu(IRadialMenuSource menu, IRadialMenuActions actions) : UiScreen
{
    private const float Outer = 340, Hub = 112, Pop = 14, Gap = 0.022f, Rim = 5;

    public override string Name => "radial";

    public override bool OpenByDefault => true;

    public override bool WantsMouse => menu.Showing;

    /// <summary>Above what it pops over; quit's and the disconnect's dialogs stay above it.</summary>
    public override int Order => 90;

    public override void Describe(ref UiFrame ui)
    {
        if (!menu.Showing)
            return;
        int count = menu.ItemCount;
        float cx = ui.DesignWidth / 2f, cy = ui.DesignHeight / 2f;

        // The ring fits the shorter side, the hub never shrinks below what its words need.
        float outer = MathF.Min(Outer, MathF.Min(cx, cy) - Pop - (float)Space.S);
        float hub = MathF.Min(Hub, outer * 0.36f);

        var mouse = ui.MouseDu;
        // No pointer yet (a negative position) means no item, not the top-left's.
        int hover = mouse.X < 0f ? -1 : UiRadialLayout.ItemAt(mouse.X - cx, mouse.Y - cy, count, hub);
        actions.Hover(hover);

        for (int i = 0; i < count && i < 9; i++)
            if (ui.ConsumeKey((int)UiKey.Key0 + i + 1) || ui.ConsumeKey((int)UiKey.KeyPad0 + i + 1))
            {
                actions.Choose(i);
                return;
            }
        if (ui.ConsumeKey((int)UiKey.Escape))
        {
            actions.Back();
            return;
        }
        foreach (int code in ui.ButtonsPressed)
            if (actions.Pressed(code))
            {
                ui.ConsumeKey(code);
                return;
            }

        var root = ui.Box().Size(Size.Fill, Size.Fill).Fill(((UiPaint)Tone.Scrim).WithOpacity(0.55f)).Key("radial-root");
        var hit = ui.Interact(root);
        using (root)
        {
            for (int i = 0; i < count; i++)
                Wedge(ref ui, i, count, cx, cy, outer, hub, i == hover);
            HubDisc(ref ui, cx, cy, hub, hover, count);
        }

        if (hit.Clicked)
        {
            if (hover < 0)
                actions.Back();
            else
                actions.Choose(hover);
        }
    }

    private void Wedge(ref UiFrame ui, int i, int count, float cx, float cy, float outer, float hub, bool over)
    {
        var item = menu.Item(i);
        bool lit = over && item.Enabled;
        var (start, span) = UiRadialLayout.Wedge(i, count, count > 1 ? Gap : 0f);

        // Lit, it stands out from the ring, and a rim along its outer edge says which.
        float r = lit ? outer + Pop : outer;
        UiPaint body = lit ? Tone.SurfaceRaised : Tone.Window;
        Sector(ref ui, body, cx, cy, r, start, span, (hub + Rim * 2f) / r).Key(i);
        if (lit)
            Sector(ref ui, Tone.Accent, cx, cy, r, start, span, (r - Rim) / r).Key(1000 + i);

        // The words at the middle of the wedge, as wide as the wedge is there.
        var (x, y) = UiRadialLayout.Centre(i, count, 1f, 1f);
        float mid = (hub + outer) / 2f + 8f;
        float width = count > 1 ? MathF.Min(232f, 2f * mid * MathF.Sin(MathF.PI / count) * 0.92f) : 232f;
        float height = 92f;
        using (ui.Column(Space.XS, Align.Centre).At(UiAnchor.TopLeft)
                 .Inset(new UiLength(MathF.Round(cx + x * mid - width / 2f), false), new UiLength(MathF.Round(cy + y * mid - height / 2f), false))
                 .Size(Size.Du(width), Size.Du(height)).Layered().Key(2000 + i))
        {
            ui.Flex();
            if (i < 9)
                ui.Text($"{i + 1}", Text.Label, Tone.Accent);
            Say(ref ui, item.Label, Text.Body, lit ? Tone.Text : item.Enabled ? Tone.Text : Tone.TextMuted).Wrap(width);
            if (item.Detail is { } detail)
                Say(ref ui, detail, Text.Caption, Tone.TextMuted).Wrap(width);
            ui.Flex();
        }
    }

    // The middle: what this menu is, and what a click would take.
    private void HubDisc(ref UiFrame ui, float cx, float cy, float hub, int hover, int count)
    {
        Sector(ref ui, Tone.Window, cx, cy, hub, 0f, MathF.Tau, 0f).Key("hub");
        Sector(ref ui, Tone.Accent, cx, cy, hub + Rim, 0f, MathF.Tau, hub / (hub + Rim)).Key("hub-rim");
        float width = hub * 2f - 2f * (float)Space.M;
        using (ui.Column(Space.XS, Align.Centre).At(UiAnchor.TopLeft)
                 .Inset(new UiLength(MathF.Round(cx - width / 2f), false), new UiLength(MathF.Round(cy - hub), false))
                 .Size(Size.Du(width), Size.Du(hub * 2f)).Layered().Key("hub-words"))
        {
            ui.Flex();
            Say(ref ui, menu.Title, Text.Label, Tone.TextMuted).Wrap(width);
            if (hover >= 0 && hover < count && menu.Item(hover).Enabled)
                Say(ref ui, menu.Item(hover).Label, Text.Body, Tone.Accent).Wrap(width);
            ui.Flex();
        }
    }

    private static UiBox Sector(ref UiFrame ui, UiPaint paint, float cx, float cy, float radius, float start, float span, float inner)
        => ui.Sector(paint, radius * 2f, start, span, inner).At(UiAnchor.TopLeft)
             .Inset(new UiLength(MathF.Round(cx - radius), false), new UiLength(MathF.Round(cy - radius), false)).Layered();

    // A #Token is looked up; anything else is the owner's data, a name or a map's.
    private static UiBox Say(ref UiFrame ui, string text, Text role, Tone tone)
        => text.StartsWith('#') ? ui.Text(text, role, tone) : ui.Data(text, role, tone);
}
