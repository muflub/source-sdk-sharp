using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Analyzers;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Tests.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The key capture field kind (plan_client_ui §6): a KeyBinding field, drawn
/// as the key bound to its command; clicked, it takes the next key or mouse
/// button; Esc gives up without closing the form, Delete clears; a key taken
/// from another row leaves that row empty; Apply unbinds every old key before
/// it binds a new one. Against a fake key store.
/// </summary>
public class UiKeyCaptureTests
{
    private const int KeyE = 15, KeyF = 16, KeyS = 29, KeyW = 33;

    private sealed class Rig
    {
        public readonly MemoryKeyBindingStore Store = KeyBindingsSamples.Stock();
        public readonly UiWindows Windows = new(new MemoryWindowStore());
        public readonly KeyBindings Form;
        public readonly UiContext Ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        private double _now = 1.0;

        public Rig()
        {
            Form = new KeyBindings(Windows, Store, new FakeKeyActions());
            Ctx.RecordNames = true;
            Frame();
            Frame();
        }

        public void Frame()
        {
            UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => Form.Describe(ref ui), _now);
            _now += 1 / 60.0;
        }

        public string? KeyOf(string command) => Form.Working.GetKey(Form.Working.Row(command)).Key;

        public bool Bound(string command) => Form.Working.GetKey(Form.Working.Row(command)).IsBound;

        public UiElementInfo Named(string name) => UiTestKit.Named(Ctx, name);

        public void Click(string name)
        {
            var e = Named(name);
            float x = e.Rect.X + e.Rect.W / 2, y = e.Rect.Y + e.Rect.H / 2;
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
        }

        public void Key(int code)
        {
            Ctx.Input(new UiInputEvent(UiInputKind.Key, 0, 0, code, '\0', 0));
            Frame();
        }
    }

    [Fact]
    public void A_KeyBinding_field_is_a_capture_and_loads_the_key_its_command_has()
    {
        var r = new Rig();
        Assert.Equal(UiFieldKind.Capture, r.Form.Working.Field(r.Form.Working.Row("+forward")).Kind);
        Assert.Equal(("W", "E"), (r.KeyOf("+forward"), r.KeyOf("+use")));
        Assert.False(r.Form.AnyDirty);
    }

    [Fact]
    public void Clicked_it_takes_the_next_key_and_writes_nothing_until_apply()
    {
        var r = new Rig();
        r.Click("+forward");
        Assert.Equal("W", r.KeyOf("+forward"));       // armed, not yet taken
        r.Key(KeyF);
        Assert.Equal("F", r.KeyOf("+forward"));
        Assert.True(r.Form.IsDirty(r.Form.Working.Row("+forward")));
        Assert.Empty(r.Store.Writes);
    }

    [Fact]
    public void A_key_pressed_with_no_field_armed_changes_nothing()
    {
        var r = new Rig();
        r.Key(KeyF);
        Assert.False(r.Form.AnyDirty);
    }

    [Fact]
    public void Apply_unbinds_the_old_key_then_binds_the_new()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Key(KeyF);
        Assert.True(r.Form.Apply());
        Assert.Equal(["unbind W", "bind F +forward"], r.Store.Writes);
        Assert.Equal(("F", null), (r.Store.KeyFor("+forward"), r.Store.CommandOn("W")));
    }

    [Fact]
    public void A_key_taken_from_another_row_leaves_it_empty_and_is_bound_once()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Key(KeyE);                                          // E was Use's
        Assert.Equal("E", r.KeyOf("+forward"));
        Assert.False(r.Bound("+use"));
        Assert.True(r.Form.Apply());
        Assert.Equal("+forward", r.Store.CommandOn("E"));
        Assert.Null(r.Store.KeyFor("+use"));
        Assert.Equal(1, r.Store.Writes.Count(w => w.StartsWith("bind ", StringComparison.Ordinal)));
    }

    [Fact]
    public void Two_rows_swapping_keys_end_with_both_bound()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Key(KeyS);                                          // S was Back's
        r.Click("+back");
        r.Key(KeyW);
        Assert.Equal(("S", "W"), (r.KeyOf("+forward"), r.KeyOf("+back")));
        Assert.True(r.Form.Apply());
        Assert.Equal(("S", "W"), (r.Store.KeyFor("+forward"), r.Store.KeyFor("+back")));
    }

    [Fact]
    public void Escape_gives_up_the_capture_and_the_form_stays_open()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Key((int)UiKey.Escape);
        Assert.Equal("W", r.KeyOf("+forward"));
        Assert.False(r.Windows.Closing("keybindings"));
        r.Key(KeyF);                                          // no longer armed
        Assert.Equal("W", r.KeyOf("+forward"));
    }

    [Fact]
    public void Delete_clears_the_field_and_apply_unbinds_it()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Key((int)UiKey.Delete);
        Assert.False(r.Bound("+forward"));
        Assert.True(r.Form.Apply());
        Assert.Equal(["unbind W"], r.Store.Writes);
    }

    [Fact]
    public void A_mouse_button_is_taken_and_its_release_does_not_arm_again()
    {
        var r = new Rig();
        r.Click("+forward");
        r.Click("+forward");                                   // MOUSE1, pressed on the field itself
        Assert.Equal("MOUSE1", r.KeyOf("+forward"));
        r.Key(KeyF);
        Assert.Equal("MOUSE1", r.KeyOf("+forward"));   // the release did not arm it again
    }

    [Fact]
    public void The_wheel_is_taken_as_its_fake_buttons()
    {
        var r = new Rig();
        r.Click("+jump");
        r.Ctx.Input(new UiInputEvent(UiInputKind.Wheel, 10, 10, 0, '\0', -1f));
        r.Frame();
        Assert.Equal("MWHEELDOWN", r.KeyOf("+jump"));
    }

    [Fact]
    public void The_generator_writes_key_accessors_that_compile()
    {
        var compilation = AnalyzerHarness.Compile("""
            [UiFormModel]
            public sealed partial class Keys
            {
                [Field("#SS_UI_Ok")] public KeyBinding Fire;
            }
            """);
        var driver = CSharpGeneratorDriver.Create([new UiFormGenerator().AsSourceGenerator()], parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output.SyntaxTrees, t => t.ToString().Contains("UiFieldKind.Capture", StringComparison.Ordinal));
    }
}
