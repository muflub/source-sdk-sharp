using System.Text;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Vgui;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The options script (plan_gameui_managed.md G2; stock's CDescription over
/// CScriptObjects): the shape of TF2's cfg/user_default.scr, a user script
/// read over the defaults, the lines the engine is sent, and the script
/// written back as cfg/user.scr.
/// </summary>
public class OptionsScriptTests
{
    private const string Defaults = """
        // comment
        VERSION 1.0

        DESCRIPTION INFO_OPTIONS
        {
        	"category1"
        	{
        		"#TF_OptionCategory_Communication"
        		{ CATEGORY }
        	}

        	"voice_modenable"
        	{
        		"#TF_EnableVoiceChat"
        		"#Tooltip_EnableVoiceChat"
        		{ BOOL }
        		{ "0" }
        	}

        	"chat_filter_settings"
        	{
        		"#TF_ChatFilterSettings"
        		{ BUTTON }
        		{ "open_chat_filter_settings" }
        	}

        	"hud_combattext_style"
        	{
        		"#TF_DamageNumbers"
        		{ LIST "#TF_PerHit" "0" "#TF_Batched" "1" }
        		{ "1" }
        	}

        	"viewmodel_fov"
        	{
        		"#TF_ViewmodelFOV"
        		{ SLIDER 54 70 }
        		{ "62.000" }
        	}

        	"cl_autoreload"
        	{
        		"#TF_AutoReload"
        		{ BOOL }
        		{ "1" }
        		SetInfo
        	}
        }
        """;

    private static OptionsScript Read(params string[] scripts)
    {
        var script = new OptionsScript("INFO_OPTIONS");
        foreach (var text in scripts)
            script.Read(Encoding.UTF8.GetBytes(text));
        return script;
    }

    [Fact]
    public void Every_type_reads_with_its_prompt_tooltip_range_choices_and_default()
    {
        var e = Read(Defaults).Entries();
        Assert.Equal(["category1", "voice_modenable", "chat_filter_settings", "hud_combattext_style", "viewmodel_fov", "cl_autoreload"],
                     e.Select(x => x.Name));
        Assert.Equal(UiScriptKind.Category, e[0].Kind);
        Assert.Equal(("#TF_EnableVoiceChat", "#Tooltip_EnableVoiceChat", UiScriptKind.Bool, "0"),
                     (e[1].Prompt, e[1].Tooltip, e[1].Kind, e[1].Value));
        Assert.Equal((UiScriptKind.Button, "open_chat_filter_settings"), (e[2].Kind, e[2].Value));
        Assert.Equal([new UiScriptItem("#TF_PerHit", "0"), new UiScriptItem("#TF_Batched", "1")], e[3].Items);
        Assert.Equal((54f, 70f, "62"), (e[4].Min, e[4].Max, e[4].Value));   // stock strips a number's trailing zeros
        Assert.True(e[5].SetInfo);
        Assert.False(e[1].SetInfo);
    }

    [Fact]
    public void A_user_script_changes_only_the_defaults_of_entries_it_names_and_adds_new_ones()
    {
        const string user = """
            VERSION 1.0
            DESCRIPTION INFO_OPTIONS
            {
            	"voice_modenable" { "#Renamed" { STRING } { "1" } }
            	"cl_new" { "#TF_New" { NUMBER -1 -1 } { "3" } }
            }
            """;
        var e = Read(Defaults, user).Entries();
        var voice = e.Single(x => x.Name == "voice_modenable");
        Assert.Equal(("#TF_EnableVoiceChat", UiScriptKind.Bool, "1"), (voice.Prompt, voice.Kind, voice.Value));
        Assert.Equal("cl_new", e[^1].Name);
    }

    [Fact]
    public void An_entry_of_an_unknown_type_ends_the_read_keeping_what_came_before()
    {
        const string text = """
            VERSION 1.0
            DESCRIPTION INFO_OPTIONS
            {
            	"a" { "#A" { BOOL } { "1" } }
            	"b" { "#B" { WIDGET } { "1" } }
            	"c" { "#C" { BOOL } { "1" } }
            }
            """;
        var script = new OptionsScript("INFO_OPTIONS");
        Assert.False(script.Read(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(["a"], script.Entries().Select(x => x.Name));
    }

    [Fact]
    public void Another_version_or_description_reads_nothing()
    {
        Assert.Empty(Read(Defaults.Replace("VERSION 1.0", "VERSION 2.0")).Entries());
        Assert.Empty(Read(Defaults.Replace("INFO_OPTIONS", "INFO_SERVERCONFIG")).Entries());
    }

    [Fact]
    public void Obsolete_entries_are_read_and_left_out()
    {
        var e = Read(Defaults.Replace("{ BUTTON }", "{ OBSOLETE }")).Entries();
        Assert.DoesNotContain(e, x => x.Name == "chat_filter_settings");
        Assert.Equal(5, e.Count);
    }

    [Fact]
    public void A_convars_value_becomes_the_entrys_and_an_empty_one_does_not()
    {
        var script = Read(Defaults);
        script.TakeCurrentValues(name => name switch { "viewmodel_fov" => "70", "voice_modenable" => "", _ => null });
        var e = script.Entries();
        Assert.Equal(("70", "0"), (e[4].Value, e[1].Value));
    }

    [Fact]
    public void The_engine_is_sent_each_entry_as_stock_wrote_it()
    {
        var e = Read(Defaults).Entries();
        Assert.Null(OptionsScript.ConfigLine(e[0], ""));                          // a category
        Assert.Null(OptionsScript.ConfigLine(e[2], e[2].Value));                  // a button
        Assert.Equal("voice_modenable \"1\"", OptionsScript.ConfigLine(e[1], "5"));
        Assert.Equal("viewmodel_fov \"70.000000\"", OptionsScript.ConfigLine(e[4], "90"));   // clamped to its range
        Assert.Equal("hud_combattext_style \"0\"", OptionsScript.ConfigLine(e[3], "7"));     // no such choice: 0
        Assert.Equal("setinfo cl_autoreload \"0\"", OptionsScript.ConfigLine(e[5], "0"));
    }

    [Fact]
    public void The_written_script_reads_back_with_the_new_values_as_defaults()
    {
        var e = Read(Defaults).Entries();
        string[] values = ["", "1", "open_chat_filter_settings", "0", "58", "0"];
        var back = Read(OptionsScript.Write("INFO_OPTIONS", e, values)).Entries();
        Assert.Equal(e.Select(x => (x.Name, x.Prompt, x.Tooltip, x.Kind, x.Min, x.Max, x.SetInfo)),
                     back.Select(x => (x.Name, x.Prompt, x.Tooltip, x.Kind, x.Min, x.Max, x.SetInfo)));
        Assert.Equal(e[3].Items, back[3].Items);
        Assert.Equal(values.Skip(1), back.Skip(1).Select(x => x.Value));
    }

    private sealed class Files : IOptionsFiles
    {
        public Dictionary<string, byte[]> Stored { get; } = [];

        public byte[]? ReadAll(string fileName) => Stored.GetValueOrDefault(fileName);

        public void WriteAll(string fileName, byte[] bytes) => Stored[fileName] = bytes;
    }

    [Fact]
    public void The_source_reads_defaults_then_user_then_convars_and_saves_lines_and_the_user_script()
    {
        var files = new Files();
        files.Stored[OptionsScriptSource.DefaultFile] = Encoding.UTF8.GetBytes(Defaults);
        var convars = new SourceSharp.Client.Ui.Screens.MemoryConVarStore().With("voice_modenable", "1");
        var lines = new SourceSharp.Client.Ui.Screens.RecordingCommandLine();
        var source = new OptionsScriptSource(files, convars, lines);
        var entries = source.Load();
        Assert.Equal("1", entries[1].Value);

        source.Save(entries, [.. entries.Select(x => x.Value)]);
        Assert.Contains("voice_modenable \"1\"", lines.Lines);
        Assert.Contains("setinfo cl_autoreload \"1\"", lines.Lines);
        Assert.True(files.Stored.ContainsKey(OptionsScriptSource.UserFile));

        source.Press(entries[2]);
        Assert.Equal("open_chat_filter_settings", lines.Lines[^1]);
    }
}
