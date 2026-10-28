using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The kill feed's icons (user ruling 2026-10-05): every weapon the server can
/// name in player_death maps to art in the UI's icon set, not the generic
/// fallback; every name the mapping answers is art the set has.
/// </summary>
public class UiKillFeedIconTests
{
    private static readonly UiIconSet Set = UiIconSet.Embedded();

    /// <summary>
    /// What the server names: each weapon script's class with stock's
    /// weapon_ dropped (DeathNotice.WeaponName), the inflictors that are
    /// projectiles (this mod's own class names, and stock's renames), physics
    /// and the world.
    /// </summary>
    public static TheoryData<string> ServerNames()
    {
        var names = new TheoryData<string>();
        foreach (string f in Directory.GetFiles(RepoLayout.Repo("game", "mod_test", "scripts"), "weapon_*.txt").Order(StringComparer.Ordinal))
        {
            string weapon = Path.GetFileNameWithoutExtension(f)["weapon_".Length..];
            if (weapon != "cubemap")      // a mapping tool, not a weapon anyone holds
                names.Add(weapon);
        }
        foreach (string n in new[]
                 {
                     EntityNames.CrossbowBolt, EntityNames.RpgMissile, EntityNames.NpcGrenadeFrag, EntityNames.PropCombineBall, EntityNames.SsBarrel,
                     "crossbow_bolt", "rpg_missile", "grenade_frag", "smg1_grenade", "combine_ball", "physics", "world",
                 })
            names.Add(n);
        return names;
    }

    [Theory]
    [MemberData(nameof(ServerNames))]
    public void Every_weapon_the_server_names_has_its_own_icon(string weapon)
    {
        string icon = KillIcons.For(weapon);
        Assert.NotEqual(KillIcons.Generic, icon);
        Assert.True(Set.IndexOf(icon) >= 0, $"'{weapon}' maps to '{icon}', which the icon set has no art for");
    }

    [Fact]
    public void The_scan_found_the_weapon_scripts()
        => Assert.True(ServerNames().Count >= 12 + 12, $"only {ServerNames().Count} names: the weapon scripts were not found");

    [Fact]
    public void An_unknown_weapon_is_the_generic_icon_and_the_set_has_it()
    {
        Assert.Equal(KillIcons.Generic, KillIcons.For("trigger_hurt"));
        Assert.True(Set.IndexOf(KillIcons.Generic) >= 0);
        Assert.True(Set.IndexOf(KillIcons.Skull) >= 0);
    }

    [Fact]
    public void The_set_has_the_ruled_icons()
    {
        string[] ruled =
        [
            "crowbar", "stunstick", "pistol", "357", "smg1", "ar2", "shotgun", "crossbow", "rpg", "frag", "slam", "physcannon",
            "skull", "explosion", "combine_ball", "physics", "generic",
        ];
        Assert.All(ruled, n => Assert.True(Set.IndexOf(n) >= 0, $"no art for '{n}'"));
    }
}

/// <summary>
/// The icon set's atlas (tools/ui-icons): the build is deterministic, the
/// committed atlas is what the SVGs build today, and an icon reaches the render
/// list as a texture the backends draw: the SVG backend as its art.
/// </summary>
public class UiIconAtlasTests
{
    private static string IconDir => SourceSharp.UiPreview.IconAtlasBuilder.IconDir(RepoLayout.RepoRoot);

    [Fact]
    public void Two_builds_are_the_same_bytes()
    {
        var a = SourceSharp.UiPreview.IconAtlasBuilder.Build(IconDir);
        var b = SourceSharp.UiPreview.IconAtlasBuilder.Build(IconDir);
        Assert.Equal(a.Manifest, b.Manifest);
        Assert.Equal(a.Compressed, b.Compressed);
        Assert.Contains(a.Alpha, x => x == 255);       // something was drawn
    }

    [Fact]
    public void The_committed_atlas_is_what_the_svgs_build()
    {
        var built = SourceSharp.UiPreview.IconAtlasBuilder.Build(IconDir);
        Assert.True(File.ReadAllText(Path.Combine(IconDir, "atlas", "icons.tsv")) == built.Manifest,
                    "icons.tsv is not what the SVGs build: run `make ui-icons` and commit the atlas");
        // The pixels, not the compressed bytes: the deflate stream may differ
        // between zlib builds while saying the same thing.
        var committed = UiIconSet.Load(new Dictionary<string, string>(), "atlas\t" + built.Width + "\t" + built.Height + "\n",
                                       File.ReadAllBytes(Path.Combine(IconDir, "atlas", "icons.a8z")));
        int differ = 0;
        for (int i = 0; i < built.Alpha.Length; i++)
            if (committed.Alpha[i] != built.Alpha[i])
                differ++;
        Assert.True(differ == 0, $"icons.a8z differs from what the SVGs build in {differ} pixels: run `make ui-icons` and commit the atlas");
    }

    [Fact]
    public void Every_icon_is_in_the_atlas_at_every_height()
    {
        var set = UiIconSet.Embedded();
        Assert.Equal(Directory.GetFiles(IconDir, "*.svg").Length, set.Count);
        for (int i = 0; i < set.Count; i++)
            foreach (int h in UiIconSet.PixelHeights)
                Assert.True(set.Rects.ToArray().Any(r => r.Icon == i && r.PixelHeight == h), $"{set.Icon(i).Name} is not in the atlas at {h}px");
    }

    [Fact]
    public void An_icon_is_an_image_element_and_the_svg_backend_draws_its_art()
    {
        var ctx = UiTestKit.Context();
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => ui.Icon("smg1", Tone.Warning, 20).Name("icon"), 1.0);
        var e = UiTestKit.Named(ctx, "icon");
        Assert.Equal(SourceSharp.Client.Ui.UiElementKind.Image, e.Kind);
        Assert.Equal(40, e.Rect.W);       // smg1's art is 48 wide on the 24 grid: 20 du tall is 40 wide
        var svg = new SvgBackend(new SvgBackendTests.FixedMetrics());
        svg.Draw(ctx.List.AsDrawList());
        Assert.Contains("data-icon=\"smg1\"", svg.Svg);
    }

    [Fact]
    public void An_unknown_name_draws_the_generic_icon()
    {
        var ctx = UiTestKit.Context();
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => ui.Icon("no-such-icon", Tone.Text, 20), 1.0);
        var svg = new SvgBackend(new SvgBackendTests.FixedMetrics());
        svg.Draw(ctx.List.AsDrawList());
        Assert.Contains("data-icon=\"generic\"", svg.Svg);
    }
}
