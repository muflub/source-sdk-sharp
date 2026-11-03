using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The keyboard page over the running mod's key actions.</summary>
public class KeyBindingsPageTests
{
    private sealed class RowList(UiKeyAction[] rows, (string, string)[] defaults) : IUiKeyActions
    {
        public IReadOnlyList<UiKeyAction> Actions() => rows;

        public IReadOnlyList<(string Key, string Command)> DefaultBinds => defaults;
    }

    private static (KeyBindings Form, MemoryKeyBindingStore Store, UiContext Ctx) Page(params UiKeyAction[] rows)
        => Page(rows, []);

    private static (KeyBindings Form, MemoryKeyBindingStore Store, UiContext Ctx) Page(UiKeyAction[] rows, (string, string)[] defaults)
    {
        var store = new MemoryKeyBindingStore().With("F", "impulse 100");
        var form = new KeyBindings(new UiWindows(new MemoryWindowStore()), store, new RowList(rows, defaults));
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        for (int i = 0; i < 2; i++)
            UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => form.Describe(ref ui), 1.0 + i / 60.0);
        return (form, store, ctx);
    }

    private static readonly UiKeyAction Light = new("impulse 100", "Flashlight", null), Slot = new("slot1", "Weapon 1", null), Jump = new("+jump", "Jump", null);

    private static string? KeyOf(KeyBindings form, string command) => form.Working.GetKey(form.Working.Row(command)).Key;

    private static void Frame(UiContext ctx, KeyBindings form, ref double now)
    {
        double at = now += 1.0 / 60.0;
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => form.Describe(ref ui), at);
    }

    private static void Click(UiContext ctx, KeyBindings form, string name, ref double now)
    {
        var r = UiTestKit.Named(ctx, name).Rect;
        float x = r.X + r.W / 2f, y = r.Y + r.H / 2f;
        ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
        Frame(ctx, form, ref now);
        ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, form, ref now);
        ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
        Frame(ctx, form, ref now);
        // What the click changed is drawn the frame after.
        Frame(ctx, form, ref now);
    }

    private static bool Has(UiContext ctx, string name) => UiTestKit.Elements(ctx).Any(e => UiTestKit.NameOf(ctx, e) == name);

    [Fact]
    public void Use_defaults_puts_every_row_on_its_default_and_clears_the_rest()
    {
        var (form, _, _) = Page([Light, Slot, Jump], [("space", "+jump"), ("1", "slot1")]);
        Assert.Equal("F", KeyOf(form, "impulse 100"));

        form.UseDefaults();
        Assert.Null(KeyOf(form, "impulse 100"));
        Assert.Equal("1", KeyOf(form, "slot1"));
        Assert.Equal("SPACE", KeyOf(form, "+jump"));
    }

    [Fact]
    public void Use_defaults_writes_nothing_until_apply_and_cancel_drops_it()
    {
        var (form, store, _) = Page([Light, Slot], [("1", "slot1")]);
        form.UseDefaults();
        Assert.Empty(store.Writes);
        Assert.True(form.AnyDirty);

        form.Cancel();
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void Use_defaults_then_apply_moves_the_binds()
    {
        var (form, store, _) = Page([Light, Slot], [("1", "slot1"), ("G", "impulse 100")]);
        form.UseDefaults();
        Assert.True(form.Apply());
        Assert.Equal("G", store.KeyFor("impulse 100"));
        Assert.Equal("1", store.KeyFor("slot1"));
        Assert.Null(store.CommandOn("F"));
    }

    [Fact]
    public void A_command_with_several_default_keys_shows_the_first()
    {
        var (form, _, _) = Page([Slot], [("1", "slot1"), ("KP_END", "slot1")]);
        form.UseDefaults();
        Assert.Equal("1", KeyOf(form, "slot1"));
    }

    [Fact]
    public void A_key_defaulted_on_two_rows_stays_with_the_last()
    {
        var (form, _, _) = Page([Light, Slot], [("Q", "impulse 100"), ("Q", "slot1")]);
        form.UseDefaults();
        Assert.Null(KeyOf(form, "impulse 100"));
        Assert.Equal("Q", KeyOf(form, "slot1"));
    }

    [Fact]
    public void A_default_for_a_command_with_no_row_is_left_out()
    {
        var (form, store, _) = Page([Slot], [("F6", "save quick"), ("1", "slot1")]);
        form.UseDefaults();
        Assert.True(form.Apply());
        Assert.Null(store.CommandOn("F6"));
    }

    /// <summary>As stock: a config with no way back to the console or a menu is not a default.</summary>
    [Fact]
    public void The_console_and_escape_always_get_their_keys()
    {
        var (form, _, _) = Page([new UiKeyAction("toggleconsole", "Console", null), new UiKeyAction("cancelselect", "Escape", null)], []);
        form.UseDefaults();
        Assert.Equal("`", KeyOf(form, "toggleconsole"));
        Assert.Equal("ESCAPE", KeyOf(form, "cancelselect"));
    }

    [Fact]
    public void A_key_already_on_its_default_in_another_case_is_no_change()
    {
        var (form, store, _) = Page([Light], [("f", "impulse 100")]);
        form.UseDefaults();
        Assert.False(form.AnyDirty);
        Assert.True(form.Apply());
        Assert.Empty(store.Writes);
    }

    [Fact]
    public void The_button_asks_first_and_keep_mine_changes_nothing()
    {
        var (form, _, ctx) = Page([Light, Slot], [("1", "slot1")]);
        double now = 2.0;
        Assert.True(Has(ctx, "keysdefaults"));
        Assert.False(Has(ctx, "keysdefaultsyes"));

        Click(ctx, form, "keysdefaults", ref now);
        Assert.True(Has(ctx, "keysdefaultsyes"));
        Assert.False(form.AnyDirty);

        Click(ctx, form, "keysdefaultsno", ref now);
        Assert.True(Has(ctx, "keysdefaults"));
        Assert.False(form.AnyDirty);
    }

    [Fact]
    public void Confirming_the_button_fills_the_form_and_writes_nothing_yet()
    {
        var (form, store, ctx) = Page([Light, Slot], [("1", "slot1")]);
        double now = 2.0;
        Click(ctx, form, "keysdefaults", ref now);
        Click(ctx, form, "keysdefaultsyes", ref now);
        Assert.Equal("1", KeyOf(form, "slot1"));
        Assert.Null(KeyOf(form, "impulse 100"));
        Assert.Empty(store.Writes);
        Assert.True(Has(ctx, "keysdefaults"));
    }

    [Fact]
    public void A_page_opened_again_does_not_still_ask()
    {
        var (form, _, ctx) = Page([Slot], [("1", "slot1")]);
        double now = 2.0;
        Click(ctx, form, "keysdefaults", ref now);
        Assert.True(Has(ctx, "keysdefaultsyes"));

        // A frame or more undescribed is the window having been shut.
        UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => { }, now += 1.0 / 60.0);
        Frame(ctx, form, ref now);
        Assert.False(Has(ctx, "keysdefaultsyes"));
    }

    [Fact]
    public void Each_action_is_a_row_loading_the_key_its_command_has()
    {
        var (form, _, _) = Page(new UiKeyAction("impulse 100", "Flashlight", "COMBAT"), new UiKeyAction("slot1", "Weapon 1", "COMBAT"));
        Assert.Equal(2, form.Working.FieldCount);
        Assert.Equal("F", form.Working.GetKey(form.Working.Row("impulse 100")).Key);
        Assert.False(form.Working.GetKey(form.Working.Row("slot1")).IsBound);
    }

    [Fact]
    public void A_key_given_to_a_row_binds_its_whole_command_on_apply()
    {
        var (form, store, _) = Page(new UiKeyAction("use weapon_crowbar;use weapon_stunstick", "Crowbar", null));
        form.Working.SetKey(0, new KeyBinding("Q"));
        Assert.True(form.Apply());
        Assert.Equal(["bind Q use weapon_crowbar;use weapon_stunstick"], store.Writes);
    }

    /// <summary>A label or heading in the file's own words reads as written, and is no token to look up.</summary>
    [Fact]
    public void A_row_and_a_heading_in_the_file_s_own_words_are_drawn_as_written()
    {
        var (_, _, ctx) = Page(new UiKeyAction("timeleft", "Time Left in Round", "MISC WORDS"));
        var names = UiTestKit.Elements(ctx).Select(e => UiTestKit.NameOf(ctx, e)).ToList();
        Assert.Contains(names, n => n.Contains("Time Left in Round", StringComparison.Ordinal));
        Assert.Contains(names, n => n.Contains("MISC WORDS", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mod_with_no_list_has_an_empty_page_and_nothing_to_apply()
    {
        var (form, store, _) = Page();
        Assert.Equal(0, form.Working.FieldCount);
        Assert.True(form.Apply());
        Assert.Empty(store.Writes);
    }
}
