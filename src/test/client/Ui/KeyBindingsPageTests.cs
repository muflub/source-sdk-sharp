using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>The keyboard page over the running mod's key actions.</summary>
public class KeyBindingsPageTests
{
    private sealed class RowList(params UiKeyAction[] rows) : IUiKeyActions
    {
        public IReadOnlyList<UiKeyAction> Actions() => rows;
    }

    private static (KeyBindings Form, MemoryKeyBindingStore Store, UiContext Ctx) Page(params UiKeyAction[] rows)
    {
        var store = new MemoryKeyBindingStore().With("F", "impulse 100");
        var form = new KeyBindings(new UiWindows(new MemoryWindowStore()), store, new RowList(rows));
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        for (int i = 0; i < 2; i++)
            UiTestKit.Frame(ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => form.Describe(ref ui), 1.0 + i / 60.0);
        return (form, store, ctx);
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
