using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Analyzers;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Tests.Ui.Analyzers;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// Forms (plan_client_ui §6, U4): the generated model, the lifecycle against a
/// fake convar store (load, dirty, Apply writes only what changed, OK, Cancel,
/// validation), the custom binding, and the fields' input.
/// </summary>
public class UiFormTests
{
    private sealed class Rig
    {
        public readonly MemoryConVarStore Store = MouseOptionsSamples.Stock();
        public readonly UiWindows Windows = new(new MemoryWindowStore());
        public readonly MouseOptions Form;
        public readonly UiContext Ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        private double _now = 1.0;

        public Rig() => Form = new MouseOptions(Windows, Store);

        public void Frame()
        {
            UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => Form.Describe(ref ui), _now);
            _now += 1 / 60.0;
        }

        public UiElementInfo Named(string name) => UiTestKit.Named(Ctx, name);

        public void Click(UiElementInfo e) => Click(e.Rect.X + e.Rect.W / 2, e.Rect.Y + e.Rect.H / 2);

        public void Click(float x, float y)
        {
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
        }
    }

    [Fact]
    public void The_generator_describes_each_field_by_its_type()
    {
        var m = new MouseOptions.Model();
        Assert.Equal(5, m.FieldCount);
        Assert.Equal((UiFieldKind.Slider, 0.1f, 20f, 0.1f), (m.Field(MouseOptions.Model.Fields.Sensitivity).Kind,
            m.Field(0).Min, m.Field(0).Max, m.Field(0).Step));
        Assert.Equal(UiFieldKind.Toggle, m.Field(MouseOptions.Model.Fields.Reverse).Kind);
        var accel = m.Field(MouseOptions.Model.Fields.Acceleration);
        Assert.Equal((UiFieldKind.Choice, 4), (accel.Kind, accel.Choices));
        Assert.Equal("Custom and power", m.ChoiceName(MouseOptions.Model.Fields.Acceleration, 3));
        Assert.Equal("#SS_UI_Mouse_Look", m.Field(0).Section);
    }

    [Fact]
    public void Opened_it_loads_from_the_store_and_nothing_is_dirty()
    {
        var r = new Rig();
        r.Store.With("sensitivity", "6.5").With("m_pitch", "-0.022").With("m_customaccel", "2");
        r.Frame();
        Assert.Equal((6.5f, true, MouseAcceleration.PowerOnly), (r.Form.Working.Sensitivity, r.Form.Working.Reverse, r.Form.Working.Acceleration));
        Assert.False(r.Form.AnyDirty);
        Assert.Empty(r.Store.Writes);
    }

    [Fact]
    public void Apply_writes_only_the_dirty_fields_and_takes_them_as_saved()
    {
        var r = new Rig();
        r.Frame();
        r.Form.Working.Sensitivity = 5f;
        Assert.True(r.Form.IsDirty(MouseOptions.Model.Fields.Sensitivity));
        Assert.False(r.Form.IsDirty(MouseOptions.Model.Fields.RawInput));

        Assert.True(r.Form.Apply());
        Assert.Equal([("sensitivity", "5")], r.Store.Writes);
        Assert.False(r.Form.AnyDirty);
    }

    [Fact]
    public void Each_kind_is_written_in_its_convar_spelling_and_reverse_flips_m_pitch()
    {
        var r = new Rig();
        r.Frame();
        r.Form.Working.Filter = true;
        r.Form.Working.Acceleration = MouseAcceleration.CustomAndPower;
        r.Form.Working.Reverse = true;
        r.Form.Apply();
        Assert.Equal(new[] { ("m_pitch", "-0.022"), ("m_filter", "1"), ("m_customaccel", "3") }.OrderBy(w => w.Item1),
                     r.Store.Writes.OrderBy(w => w.Name));
    }

    [Fact]
    public void Cancel_discards_writes_nothing_and_closes()
    {
        var r = new Rig();
        r.Frame();
        r.Form.Working.Sensitivity = 9f;
        r.Form.Cancel();
        Assert.Equal(3f, r.Form.Working.Sensitivity);
        Assert.Empty(r.Store.Writes);
        Assert.True(r.Windows.Closing("mouseoptions"));
    }

    [Fact]
    public void An_invalid_field_blocks_apply_and_ok_and_says_why()
    {
        var r = new Rig();
        r.Store.With("sensitivity", "0.3");
        r.Ctx.RecordNames = true;
        r.Frame();
        Assert.False(r.Form.IsValid);
        Assert.False(r.Form.Apply());
        r.Form.Ok();
        Assert.Empty(r.Store.Writes);
        Assert.False(r.Windows.Closing("mouseoptions"));
        r.Frame();
        Assert.Equal("Below 0.5 the view barely moves", UiTestKit.RunText(r.Ctx, UiTestKit.Elements(r.Ctx).ToList().FindIndex(e => UiTestKit.NameOf(r.Ctx, e).EndsWith("invalid", StringComparison.Ordinal))));
    }

    [Fact]
    public void Reopened_it_reloads_what_the_store_holds_now()
    {
        var r = new Rig();
        r.Frame();
        r.Form.Working.Sensitivity = 9f;           // edited, then closed without applying
        r.Store.With("sensitivity", "4");          // and changed elsewhere
        r.Frame();                                 // a frame skipped: the form was not described
        UiTestKit.Frame(r.Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => { }, 5.0);
        r.Frame();
        Assert.Equal(4f, r.Form.Working.Sensitivity);
    }

    [Fact]
    public void A_press_on_the_slider_track_sets_the_value_where_it_was_pressed()
    {
        var r = new Rig();
        r.Ctx.RecordNames = true;
        r.Frame();
        r.Frame();
        var track = r.Named("Sensitivity");
        r.Click(track.Rect.X + track.Rect.W * 0.5f, track.Rect.Y + track.Rect.H / 2);
        Assert.InRange(r.Form.Working.Sensitivity, 9.9f, 10.2f);    // halfway along 0.1..20, on the 0.1 grid
        Assert.True(r.Form.IsDirty(MouseOptions.Model.Fields.Sensitivity));
    }

    [Fact]
    public void A_click_on_a_toggle_flips_it_and_a_choice_opens_and_chooses()
    {
        var r = new Rig();
        r.Ctx.RecordNames = true;
        r.Frame();
        r.Frame();
        r.Click(r.Named("Filter"));
        Assert.True(r.Form.Working.Filter);

        r.Click(r.Named("Acceleration"));
        var rows = UiTestKit.Elements(r.Ctx).Where(e => UiTestKit.NameOf(r.Ctx, e).Contains("choice-list", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(rows);
        var third = UiTestKit.Elements(r.Ctx).First(e => UiTestKit.RunText(r.Ctx, Array.IndexOf(UiTestKit.Elements(r.Ctx), e)) == "Power only");
        r.Click(third);
        Assert.Equal(MouseAcceleration.PowerOnly, r.Form.Working.Acceleration);
    }

    [Fact]
    public void A_field_the_generator_cannot_draw_is_an_error()
    {
        var compilation = AnalyzerHarness.Compile("""
            [UiFormModel]
            public sealed partial class Bad
            {
                [Field("#SS_UI_Ok")] public System.DateTime When;
                [Field("#SS_UI_Ok")] public int NoRange;
            }
            """);
        var driver = CSharpGeneratorDriver.Create([new UiFormGenerator().AsSourceGenerator()], parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        Assert.Equal(["When", "NoRange"], diagnostics.Where(d => d.Id == "SSUI010").Select(d => (string)d.GetMessage().Split('\'')[1]));
    }

    [Fact]
    public void A_good_model_compiles_with_what_the_generator_writes()
    {
        var compilation = AnalyzerHarness.Compile("""
            public enum Pick { One, TwoWords }
            [UiFormModel]
            public sealed partial class Good
            {
                [Field("#SS_UI_Ok", Min = 0, Max = 10)] public int Count;
                [Field("#SS_UI_Ok")] public string Name = "";
                [Field("#SS_UI_Ok")] public Pick Which;
            }
            """);
        var driver = CSharpGeneratorDriver.Create([new UiFormGenerator().AsSourceGenerator()], parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(output.SyntaxTrees, t => t.ToString().Contains("public const int Which = 2;", StringComparison.Ordinal));
    }
}
