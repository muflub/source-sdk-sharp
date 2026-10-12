using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using TF2.Descent.Client.Ui;
using TF2.Descent.Client.Ui.Parts;
using TF2.Descent.Client.Ui.Screens;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The item inspect (the corner button, the modal, the two surfaces) and the
/// vendor's 3D portrait — all data-only: the plan keeps the rasterisation
/// engine-side (docs/plans/descent_ui.md 7), so the facts read the declared
/// surfaces through the svg's data-texture marker and the element tree,
/// exactly as DescentAutomapTests reads the canvas.
/// </summary>
public class DescentInspectTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static (string Svg, UiElementInfo[] Elements, UiContext Ctx) DrawScreen(UiScreen screen)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, screen, UiSamples.Screen(1920, 1080), 100.0);
        return (UiTestKit.Svg(ctx), UiTestKit.Elements(ctx), ctx);
    }

    private static (string Svg, UiElementInfo[] Elements, UiContext Ctx) DrawPanel(UiPanel panel)
        => DrawScreen(new UiPanelPreview(panel.GetType().Name, panel, UiAnchor.Centre, UiLayerKind.Menu));

    private static int TextureCount(string svg)
        => svg.Split("data-texture=\"", StringSplitOptions.None).Length - 1;

    private static int TextureFor(string svg, int handle)
        => svg.Split($"data-texture=\"{handle}\"", StringSplitOptions.None).Length - 1;

    [Fact]
    public void The_modal_draws_nothing_while_hidden_and_the_item_when_shown()
    {
        var hidden = DrawPanel(new InspectModal(new FakeInspect { Show = false }));
        Assert.Equal(0, TextureCount(hidden.Svg));
        Assert.DoesNotContain("The Back Scatter", hidden.Svg);

        var shown = DrawPanel(new InspectModal(new FakeInspect()));
        // The item's name (ui.Data: the subject's text, rarity ink aside) and
        // one plate per view — while Showing is false the part is mute.
        Assert.Contains("The Back Scatter", shown.Svg);
        Assert.Equal(2, TextureCount(shown.Svg));
    }

    [Fact]
    public void The_two_views_use_their_own_surfaces_and_mounted_names_the_hero()
    {
        // Weapon-alone: both plates declared with their own handles, and the
        // hero caption rides the mounted view only.
        var (weaponSvg, _, _) = DrawPanel(new InspectModal(new FakeInspect()));
        Assert.Equal(1, TextureFor(weaponSvg, 21));
        Assert.Equal(1, TextureFor(weaponSvg, 22));
        Assert.DoesNotContain("Sentry", weaponSvg);

        // Mounted: the same two distinct surfaces (21 != 22 — the fakes name
        // the weapon and the hero renders apart), and now the hero is named.
        var (mountedSvg, _, _) = DrawPanel(new InspectModal(new FakeInspect { Mounted = true }));
        Assert.Equal(1, TextureFor(mountedSvg, 21));
        Assert.Equal(1, TextureFor(mountedSvg, 22));
        Assert.Contains("Sentry", mountedSvg);
    }

    [Fact]
    public void The_slot_button_appears_only_for_inspectable_items()
    {
        var item = new DescentItemView("Force-A-Bonk", DescentRarity.Vintage, 21, true, 40, 60, 2,
                                        false, false, false, false);
        // Inspectable: the keyed button exists, exactly one.
        var with = DrawScreen(new ItemSlotPartProbe(
            new ItemSlotProps(item, false, false, false, false, true, 3, new FakeInspectActions())));
        Assert.Single(with.Elements, e => UiTestKit.NameOf(with.Ctx, e) == "inspect");

        // Not inspectable though the host wired the actions: the flag is the
        // gate, not the wiring — no element carries the button's key.
        var plain = DrawScreen(new ItemSlotPartProbe(
            new ItemSlotProps(item, false, false, false, false, false, 3, new FakeInspectActions())));
        Assert.Equal(0, plain.Elements.Count(e => UiTestKit.NameOf(plain.Ctx, e) == "inspect"));
    }
    [Fact]
    public void The_withdraw_lock_leaves_the_item_name_its_full_label_width()
    {
        var item = new DescentItemView("Force-A-Bonk", DescentRarity.Vintage, 21, true, 40, 60, 2,
                                        false, false, false, false);
        float LabelWidth(bool withdrawOnly)
        {
            var (_, els, ctx) = DrawScreen(new ItemSlotPartProbe(
                new ItemSlotProps(item, false, withdrawOnly, false, false)));
            var text = Assert.Single(els, e => e.Kind == UiElementKind.Text);
            return text.Rect.W;
        }
        // The 2d overflow tail's lock must not eat the label: the plan's
        // Elide() budget is the whole 48-du cell (docs/plans/descent_ui.md 2.4);
        // a chip taken out of the row collapsed "Force-A-Bonk" to "M...".
        Assert.Equal(LabelWidth(false), LabelWidth(true));
        // The lock still reads: one shape element, and it is the warning chip.
        var (_, locked, _) = DrawScreen(new ItemSlotPartProbe(
            new ItemSlotProps(item, false, true, false, false)));
        Assert.Single(locked, e => e.Kind == UiElementKind.Shape);
    }

    [Fact]
    public void The_slot_chips_clear_the_name_and_each_other_inside_the_cell()
    {
        // All three chips at once (a broken Unusual in the overflow tail): the
        // fullest bottom row a 48-du cell must hold. A centred broken X ran
        // 6 du into the name's band and crossed "Crack..." (RevC, vendor).
        var item = new DescentItemView("Cracked Guard", DescentRarity.Unusual, 30, true, 0, 60, 5,
                                        true, true, false, false);
        var (_, els, ctx) = DrawScreen(new ItemSlotPartProbe(new ItemSlotProps(item, false, true, false, false)));
        var label = Assert.Single(els, e => e.Kind == UiElementKind.Text).Rect;
        var chips = els.Where(e => e.Kind == UiElementKind.Shape).Select(e => e.Rect).ToArray();
        Assert.Equal(3, chips.Length);
        // The cell: the 48-du well (scale 1 at 1920x1080), the innermost 48x48 box.
        float s = ctx.Screen.Scale;
        var cell = els.Last(e => e.Kind == UiElementKind.Box && MathF.Abs(e.Rect.W - ItemSlot.SizeDu * s) < 0.5f
                                 && MathF.Abs(e.Rect.H - ItemSlot.SizeDu * s) < 0.5f).Rect;
        static bool Hit(UiRect a, UiRect b)
            => a.X < b.X + b.W - 0.5f && b.X < a.X + a.W - 0.5f && a.Y < b.Y + b.H - 0.5f && b.Y < a.Y + a.H - 0.5f;
        for (int i = 0; i < chips.Length; i++)
        {
            var c = chips[i];
            Assert.False(Hit(c, label), $"chip {c} over the name {label}");
            Assert.True(c.X >= cell.X - 0.5f && c.X + c.W <= cell.X + cell.W + 0.5f
                        && c.Y >= cell.Y - 0.5f && c.Y + c.H <= cell.Y + cell.H + 0.5f, $"chip {c} outside the cell {cell}");
            for (int j = i + 1; j < chips.Length; j++)
                Assert.False(Hit(c, chips[j]), $"chips {c} and {chips[j]} overlap");
        }
    }

    [Fact]
    public void The_vendor_portrait_texture_exists_only_while_active()
    {
        // Injected and active: the window's right carries the declared surface.
        Assert.Equal(1, TextureFor(DrawVendor(new FakePortrait { On = true }), 41));

        // Inactive (the plain stall): no textured quad anywhere in the frame.
        Assert.Equal(0, TextureCount(DrawVendor(new FakePortrait { On = false })));

        // Not injected at all (the peers' samples): the same plain picture.
        Assert.Equal(0, TextureCount(DrawVendor(null)));
    }

    private static string DrawVendor(FakePortrait? portrait)
    {
        var s = new UiSampleServices();
        s.Add<IUiWindowStore>(new MemoryWindowStore());
        s.Add<IDescentVendor>(new FakeWareVendor { Kind = DescentVendorKind.Weapons, BuyWares = [], SellWares = [] });
        s.Add<IDescentItemGridSource>(new FakeWareGrid { Capacity = 4, Items = [] });
        s.Add<IDescentVendorActions>(new FakeWareActions());
        // The hover subjects, as the vendor samples wire them: inactive
        // fakes, which draw nothing (the pointer-less state).
        var hover = new FakeWareHover();
        s.Add<IDescentVendorHover>(hover);
        s.Add<IDescentHintTip>(hover);
        s.Add<IDescentItemHover>(new FakeVendorItemHover());
        if (portrait is not null)
            s.Add<IDescentVendorPortrait>(portrait);
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, s.Create<Vendor>(), UiSamples.Screen(1920, 1080), 100.0);
        return UiTestKit.Svg(ctx);
    }

    /// <summary>Draws one ItemSlot with fixed props, the way a host hands them
    /// to the part (the gallery's own machinery, props pinned).</summary>
    private sealed class ItemSlotPartProbe(ItemSlotProps props) : UiScreen
    {
        private readonly ItemSlot _slot = new();
        public override string Name => "ItemSlotProbe";
        public override UiLayerKind Layer => UiLayerKind.Menu;
        public override void Describe(ref UiFrame ui)
        {
            using (ui.Panel().Fill(Tone.Surface).Pad(Space.S, Space.S))
                ui.Sub("slot", _slot, props);
        }
    }

    private sealed class FakeInspect : IDescentInspect
    {
        public bool Show { get; init; } = true;
        public bool Mounted { get; init; }
        public bool Showing => Show;
        // The item stays offered whether or not Showing: the Showing gate is
        // the subject, not a null item.
        public DescentItemView? Item => new DescentItemView("The Back Scatter", DescentRarity.Strange, 21, true, 40, 82, 3,
                                                            false, false, false, true);
        public bool ShowsMounted => Mounted;
        public bool HasWeaponSurface => true;
        public int WeaponTexture => 21;
        public bool HasMountedSurface => true;
        public int MountedTexture => 22;
        public string CharacterName => "Sentry";
    }

    private sealed class FakeInspectActions : IDescentInspectActions
    {
        public void OpenInspect(int slotOrCell) { }
        public void Close() { }
        public void ShowMounted(bool mounted) { }
    }

    private sealed class FakePortrait : IDescentVendorPortrait
    {
        public bool On { get; init; }
        public bool Active => On;
        public int Texture => 41;
    }
}
