using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The ammo panel's secondary ammunition, as (current, max): pips up to five,
/// numbers past that, the danger tone at none, and nothing at all (no element,
/// no room) for a weapon without one.
/// </summary>
public class UiHudAmmoTests
{
    private static readonly UiFontLibrary Fonts = new();

    private static (UiContext Ctx, UiElementInfo[] Elements) Draw(FakeLocalPlayer player)
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), Fonts, UiSamplesTests.Language());
        UiSamples.Render(ctx, HudSamples.Build(new UiSampleServices(), player), UiSamples.Screen(1920, 1080), 100.0);
        return (ctx, UiTestKit.Elements(ctx));
    }

    private static UiElementInfo[] Named(UiContext ctx, UiElementInfo[] all, string name)
        => all.Where(e => UiTestKit.NameOf(ctx, e) == name).ToArray();

    private static UiElementInfo Panel(UiContext ctx, UiElementInfo[] all)
    {
        // The ammo panel: the raised surface under AmmoPanel[ammo].
        int ammo = Array.FindIndex(all, e => UiTestKit.NameOf(ctx, e) == "AmmoPanel[ammo]");
        Assert.True(ammo >= 0, "no ammo panel");
        return all.Where((e, i) => i > ammo && e.Kind == UiElementKind.Stack && e.Rect.W >= 232 * 1f && IsUnder(all, i, ammo)).First();
    }

    private static bool IsUnder(UiElementInfo[] all, int i, int ancestor)
    {
        for (int k = all[i].Parent; k >= 0; k = all[k].Parent)
            if (k == ancestor) return true;
        return false;
    }

    [Fact]
    public void An_smg_with_two_of_three_grenades_shows_three_pips_two_full()
    {
        var (ctx, all) = Draw(new FakeLocalPlayer { Secondary = 2, SecondaryMax = 3 });
        Assert.Single(Named(ctx, all, "secondary"));
        Assert.Equal(2, Named(ctx, all, "pip full").Length);
        Assert.Single(Named(ctx, all, "pip empty"));
        int full = Array.FindIndex(all, e => UiTestKit.NameOf(ctx, e) == "pip full");
        Assert.Equal(Tone.Accent, ctx.Report.FillToneOf(full));
    }

    [Fact]
    public void An_ar2_with_no_balls_shows_three_empty_pips_in_the_danger_tone()
    {
        var (ctx, all) = Draw(new FakeLocalPlayer { WeaponName = "#HL2_Pulse_Rifle_Menu", Clip = 30, Reserve = 60, Secondary = 0, SecondaryMax = 3 });
        Assert.Empty(Named(ctx, all, "pip full"));
        Assert.Equal(3, Named(ctx, all, "pip empty").Length);
        int alt = Array.FindIndex(all, e => UiTestKit.NameOf(ctx, e) == "\"ALT\"");
        Assert.True(alt >= 0, "no ALT label");
        Assert.Equal(Tone.Danger, ctx.Report.TextToneOf(alt));

        // Each empty pip is outlined in the danger tone.
        var danger = SourceSharpTheme.Create().Color(Tone.Danger);
        var pips = all.Select((e, i) => (e, i)).Where(p => UiTestKit.NameOf(ctx, p.e) == "pip empty").Select(p => p.i).ToHashSet();
        var ops = ctx.List.AsDrawList().Ops.ToArray();
        var quads = ctx.List.AsDrawList().Quads.ToArray();
        var borders = ops.Where(o => o.Kind == UiPrimitiveKind.Quad && pips.Contains(o.Element))
                         .Select(o => quads[o.Index].Border).ToArray();
        Assert.Equal(3, borders.Length);
        Assert.All(borders, b => Assert.Equal(danger, b));
    }

    [Fact]
    public void A_pistol_emits_no_secondary_elements_and_reserves_no_room()
    {
        var pistol = new FakeLocalPlayer { WeaponName = "#HL2_Pistol_Menu", Clip = 18, Reserve = 150, Secondary = -1, SecondaryMax = -1 };
        var (ctx, all) = Draw(pistol);
        Assert.DoesNotContain(all, e => UiTestKit.NameOf(ctx, e).StartsWith("secondary", StringComparison.Ordinal)
                                       || UiTestKit.NameOf(ctx, e).StartsWith("pip", StringComparison.Ordinal));
        Assert.DoesNotContain(all, e => UiTestKit.NameOf(ctx, e) == "\"ALT\"");

        // The same weapon with a secondary is wider by the secondary block: without one, no gap is kept.
        var (ctx2, all2) = Draw(new FakeLocalPlayer { WeaponName = "#HL2_Pistol_Menu", Clip = 18, Reserve = 150, Secondary = 1, SecondaryMax = 3 });
        float without = Panel(ctx, all).Rect.W, with = Panel(ctx2, all2).Rect.W;
        float block = Named(ctx2, all2, "secondary")[0].Rect.W + Named(ctx2, all2, "secondary divider")[0].Rect.W + 2 * 12;
        Assert.Equal(with - block, without, 1f);
    }

    [Fact]
    public void A_weapon_with_a_secondary_but_no_clip_shows_its_reserve_and_the_secondary()
    {
        var (ctx, all) = Draw(new FakeLocalPlayer { WeaponName = "#HL2_Pulse_Rifle_Menu", Clip = -1, Reserve = 60, Secondary = 1, SecondaryMax = 3 });
        Assert.Contains(all, e => UiTestKit.NameOf(ctx, e) == "\"60\"");
        Assert.DoesNotContain(all, e => UiTestKit.NameOf(ctx, e).StartsWith("\"/ ", StringComparison.Ordinal));
        Assert.Single(Named(ctx, all, "pip full"));
        Assert.Equal(2, Named(ctx, all, "pip empty").Length);
    }

    [Fact]
    public void More_than_five_secondary_rounds_are_numbers_not_pips()
    {
        var (ctx, all) = Draw(new FakeLocalPlayer { Secondary = 4, SecondaryMax = 8 });
        Assert.Empty(Named(ctx, all, "pip full"));
        int count = Array.FindIndex(all, e => UiTestKit.NameOf(ctx, e) == "secondary count");
        Assert.Equal("4 / 8", UiTestKit.RunText(ctx, count));
    }

    /// <summary>
    /// A weapon with no ammunition (the crowbar) gets a panel the size of its
    /// name, not the ammo panel's width with the name pushed to its far side
    /// (plan_client_ui U3: the live strip next to its SVG showed it).
    /// </summary>
    [Fact]
    public void A_weapon_with_no_ammunition_gets_a_panel_its_name_s_size()
    {
        var (ctx, all) = Draw(new FakeLocalPlayer { WeaponName = "#HL2_Crowbar_Menu", Clip = -1, Reserve = -1, Secondary = -1, SecondaryMax = -1 });
        int ammo = Array.FindIndex(all, e => UiTestKit.NameOf(ctx, e) == "AmmoPanel[ammo]");
        Assert.True(ammo >= 0, "no ammo panel");
        var stacks = all.Where((e, i) => i > ammo && e.Kind == UiElementKind.Stack && IsUnder(all, i, ammo)).ToArray();
        var panel = stacks.MaxBy(e => e.Rect.W);
        var name = all.Where((e, i) => i > ammo && e.Kind == UiElementKind.Text && IsUnder(all, i, ammo)).Single();

        Assert.True(panel.Rect.W < 200, $"the panel is {panel.Rect.W} px wide for a name of {name.Rect.W}");
        Assert.True(name.Rect.X - panel.Rect.X <= 24, $"the name starts {name.Rect.X - panel.Rect.X} px into its panel");
    }
}
