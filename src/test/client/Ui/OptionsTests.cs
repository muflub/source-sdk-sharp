using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The options window and its pages (plan_gameui_managed.md G2), as stock's
/// options dialog behaved: every page loads when it opens, Apply writes every
/// page, Cancel discards; and each page's loads and writes against the
/// convars, console lines and lists stock's pages used.
/// </summary>
public class OptionsTests
{
    private sealed class Rig
    {
        public readonly UiSampleServices Services = new();
        public readonly Options Window;
        public readonly UiContext Ctx = UiTestKit.Context();
        private double _now = 1.0;

        public Rig(Action<UiSampleServices>? setup = null, int tab = 0)
        {
            setup?.Invoke(Services);
            Window = OptionsSamples.Build(Services, tab);
        }

        public MemoryConVarStore Convars => (MemoryConVarStore)Services.Create<IUiConVarStore>();
        public RecordingCommandLine Lines => (RecordingCommandLine)Services.Create<IUiCommandLine>();
        public T Page<T>() where T : class => Services.Create<T>();

        public void Frame()
        {
            UiTestKit.Frame(Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => Window.Describe(ref ui), _now);
            _now += 1 / 60.0;
        }

        public void Click(float x, float y)
        {
            Ctx.Input(new UiInputEvent(UiInputKind.MouseMove, x, y, 0, '\0', 0));
            Ctx.Input(new UiInputEvent(UiInputKind.MouseDown, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
            Ctx.Input(new UiInputEvent(UiInputKind.MouseUp, x, y, (int)UiKey.MouseLeft, '\0', 0));
            Frame();
        }

        public void ClickText(string token)
        {
            var els = UiTestKit.Elements(Ctx);
            int i = Array.FindIndex(els, e => UiTestKit.RunText(Ctx, Array.IndexOf(els, e)) == token);
            Assert.True(i >= 0, $"no element drew {token}");
            var r = els[i].Rect;
            Click(r.X + r.W / 2, r.Y + r.H / 2);
        }
    }

    // ---- the window ----

    [Fact]
    public void Opened_every_page_loads_and_nothing_is_dirty()
    {
        var r = new Rig();
        r.Convars.With("volume", "0.3");
        r.Frame();
        Assert.Equal(0.3f, r.Page<AudioOptions>().Working.Volume);
        Assert.Equal("Gordon", r.Page<MultiplayerOptions>().Working.Name);
        Assert.False(r.Window.AnyDirty);
        Assert.Empty(r.Convars.Writes);
        Assert.Empty(r.Lines.Lines);
    }

    [Fact]
    public void A_tab_clicked_shows_its_page()
    {
        var r = new Rig();
        r.Frame();
        Assert.Equal(0, r.Window.Tab);
        r.ClickText("#SS_UI_Video_Title");
        Assert.Equal(3, r.Window.Tab);
        r.Frame();          // the click lands mid-frame: the next one draws the page
        Assert.Contains(UiTestKit.Elements(r.Ctx), e => UiTestKit.RunText(r.Ctx, Array.IndexOf(UiTestKit.Elements(r.Ctx), e)) == "#SS_UI_Video_Resolution");
    }

    private static int Named(Rig r, string name) => UiTestKit.Elements(r.Ctx).Count(e => UiTestKit.NameOf(r.Ctx, e) == name);

    // The tabs' left edges: what a mark must not move.
    private static float[] TabEdges(Rig r)
        => [.. UiTestKit.Elements(r.Ctx).Where(e => UiTestKit.NameOf(r.Ctx, e).StartsWith('"')).Take(8).Select(e => e.Rect.X)];

    private static void ChangeVolume(Rig r)
    {
        var audio = r.Page<AudioOptions>();
        audio.Working.Volume = audio.Working.Volume == 0.25f ? 0.5f : 0.25f;
    }

    [Fact]
    public void A_clean_window_marks_nothing()
    {
        var r = new Rig();
        r.Frame();
        r.Frame();
        Assert.Equal(0, Named(r, "tabchanged"));
        Assert.Equal(0, Named(r, "dirty"));
    }

    [Fact]
    public void A_changed_setting_marks_its_row_and_its_tab()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        ChangeVolume(r);
        r.Frame();
        Assert.Equal(1, Named(r, "dirty"));
        Assert.Equal(1, Named(r, "tabchanged"));
    }

    [Fact]
    public void Each_page_with_a_change_marks_its_own_tab_though_another_is_showing()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        ChangeVolume(r);
        var voice = r.Page<VoiceOptions>();
        voice.Working.Receive = voice.Working.Receive == 0.5f ? 0.25f : 0.5f;
        r.Frame();
        Assert.Equal(2, Named(r, "tabchanged"));
        Assert.Equal(1, Named(r, "dirty"));   // the rows of the page on show only
    }

    [Fact]
    public void A_change_set_back_clears_both_marks()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        var audio = r.Page<AudioOptions>();
        float was = audio.Working.Volume;
        ChangeVolume(r);
        r.Frame();
        Assert.Equal(1, Named(r, "tabchanged"));
        audio.Working.Volume = was;
        r.Frame();
        Assert.Equal(0, Named(r, "tabchanged"));
        Assert.Equal(0, Named(r, "dirty"));
    }

    [Fact]
    public void Apply_clears_both_marks()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        ChangeVolume(r);
        r.Frame();
        Assert.Equal(1, Named(r, "tabchanged"));
        Assert.True(r.Window.Apply());
        r.Frame();
        Assert.Equal(0, Named(r, "tabchanged"));
        Assert.Equal(0, Named(r, "dirty"));
    }

    [Fact]
    public void Marking_a_tab_moves_no_tab()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        r.Frame();
        var before = TabEdges(r);
        ChangeVolume(r);
        r.Frame();
        Assert.Equal(1, Named(r, "tabchanged"));
        Assert.True(before.Length >= 7, "the stimulus: the tabs were read");
        Assert.Equal(before, TabEdges(r));
    }

    /// <summary>The live gate's order: Cancel, and the window open again with no frame between to notice it shut.</summary>
    [Fact]
    public void Reopened_at_once_after_cancel_it_has_discarded_the_edit()
    {
        var r = new Rig();
        r.Frame();
        var audio = r.Page<AudioOptions>();
        float saved = audio.Working.Volume;
        audio.Working.Volume = saved == 0.25f ? 0.5f : 0.25f;
        Assert.True(audio.AnyDirty);

        r.Window.Cancel();
        r.Frame();
        Assert.False(audio.AnyDirty);
        Assert.Equal(saved, audio.Working.Volume);
    }

    [Fact]
    public void Apply_writes_every_pages_changes_and_Cancel_discards_them()
    {
        var r = new Rig();
        r.Frame();
        var audio = r.Page<AudioOptions>();
        audio.Working.Volume = 0.25f;
        var voice = r.Page<VoiceOptions>();
        voice.Working.Receive = 0.5f;
        Assert.True(r.Window.AnyDirty);
        Assert.True(r.Window.Apply());
        Assert.Contains(("volume", "0.25"), r.Convars.Writes);
        Assert.Contains(("voice_scale", "0.5"), r.Convars.Writes);
        Assert.False(r.Window.AnyDirty);

        audio.Working.Volume = 0.9f;
        r.Window.Cancel();
        r.Frame();          // closing: the window's next open reloads
        r.Frame();
        int writes = r.Convars.Writes.Count;
        Assert.DoesNotContain(("volume", "0.9"), r.Convars.Writes);
        Assert.Equal(writes, r.Convars.Writes.Count);
    }

    [Fact]
    public void A_reopened_window_reloads_every_page()
    {
        var r = new Rig();
        r.Frame();
        r.Page<AudioOptions>().Working.Volume = 0.1f;
        r.Convars.With("volume", "0.7");
        r.Frame();           // still open: the edit stands
        Assert.Equal(0.1f, r.Page<AudioOptions>().Working.Volume);
        r.Frame();
        UiTestKit.Frame(r.Ctx, UiTestKit.Screen(1920, 1080), (ref UiFrame ui) => { }, 50.0);  // a frame it was not described
        r.Frame();
        Assert.Equal(0.7f, r.Page<AudioOptions>().Working.Volume);
    }

    // ---- audio ----

    [Fact]
    public void Captions_load_from_both_convars_and_save_closecaption_as_a_console_line()
    {
        var r = new Rig(s => s.Add<IUiConVarStore>(OptionsSamples.Convars().With("closecaption", "1").With("cc_subtitles", "1")));
        r.Frame();
        var audio = r.Page<AudioOptions>();
        Assert.Equal(ClosedCaptions.SubtitlesOnly, audio.Working.Captions);
        audio.Working.Captions = ClosedCaptions.None;
        audio.Apply();
        Assert.Contains(("cc_subtitles", "0"), r.Convars.Writes);
        Assert.Equal(["closecaption 0"], r.Lines.Lines);
    }

    [Theory]
    [InlineData("1", "0", SoundQuality.High)]
    [InlineData("0", "0", SoundQuality.Medium)]
    [InlineData("0", "1", SoundQuality.Low)]
    public void Sound_quality_is_made_of_two_convars(string pitch, string slowCpu, SoundQuality quality)
    {
        var r = new Rig(s => s.Add<IUiConVarStore>(OptionsSamples.Convars().With("Snd_PitchQuality", pitch).With("dsp_slow_cpu", slowCpu)));
        r.Frame();
        Assert.Equal(quality, r.Page<AudioOptions>().Working.Quality);
    }

    [Fact]
    public void Saved_quality_writes_both_convars_and_enhanced_stereo_follows_headphones_at_high()
    {
        var r = new Rig();
        r.Frame();
        var audio = r.Page<AudioOptions>();
        audio.Working.Quality = SoundQuality.Low;
        audio.Apply();
        Assert.Contains(("dsp_slow_cpu", "1"), r.Convars.Writes);
        Assert.Contains(("Snd_PitchQuality", "0"), r.Convars.Writes);
        Assert.Contains(("dsp_enhance_stereo", "0"), r.Convars.Writes);
    }

    [Fact]
    public void An_unset_or_absent_speaker_setup_reads_as_two_speakers()
    {
        var r = new Rig(s => s.Add<IUiConVarStore>(OptionsSamples.Convars().With("Snd_Surround_Speakers", "-1")));
        r.Frame();
        Assert.Equal(SpeakerSetup.TwoSpeakers, r.Page<AudioOptions>().Working.Speakers);
    }

    // ---- video ----

    [Fact]
    public void The_resolutions_are_the_engines_of_the_chosen_aspect_never_above_the_desktop()
    {
        var r = new Rig(tab: 3);
        r.Frame();
        var video = r.Page<VideoOptions>();
        Assert.Equal(1, video.Working.Aspect);                         // 1920x1080 is 16:9
        Assert.Equal([new UiVideoMode(1280, 720), new UiVideoMode(1600, 900), new UiVideoMode(1920, 1080)], video.ListedModes);
        Assert.Equal(new UiVideoMode(1920, 1080), video.ListedModes[video.Working.Mode]);
        video.Working = video.Working with { Aspect = 0 };
        r.Frame();
        Assert.Equal([new UiVideoMode(640, 480), new UiVideoMode(800, 600), new UiVideoMode(1024, 768)], video.ListedModes);
    }

    /// <summary>Written back, the settings Load read are not all what it read: no
    /// antialiasing read 0 and went out as 1 sample, and the material system answers
    /// that with a device reset, which left every font blank.</summary>
    [Fact]
    public void A_video_page_nobody_changed_applies_nothing()
    {
        var r = new Rig(tab: 3);
        r.Frame();
        int convars = r.Convars.Writes.Count;
        Assert.True(r.Page<VideoOptions>().Apply());
        Assert.Empty(r.Lines.Lines);
        Assert.Equal(convars, r.Convars.Writes.Count);
    }

    [Fact]
    public void A_video_setting_changed_writes_that_setting_and_saves()
    {
        var r = new Rig(tab: 3);
        r.Frame();
        var video = r.Page<VideoOptions>();
        video.Working = video.Working with { VSync = video.Working.VSync == 0 ? 1 : 0 };
        Assert.True(video.Apply());
        Assert.Equal([$"mat_vsync {video.Working.VSync}", "mat_savechanges"], r.Lines.Lines);
    }

    /// <summary>The trigger: ticking the developer console and pressing OK re-sent every video setting.</summary>
    [Fact]
    public void Applying_a_change_on_one_page_runs_no_other_page_s_apply()
    {
        var r = new Rig(tab: 6);
        r.Frame();
        var advanced = r.Page<AdvancedOptions>();
        advanced.Working.DeveloperConsole = !advanced.Working.DeveloperConsole;
        r.Convars.Writes.Clear();
        Assert.True(r.Window.Apply());
        Assert.Equal(["con_enable"], r.Convars.Writes.Select(w => w.Name));
        Assert.Empty(r.Lines.Lines);
    }

    [Fact]
    public void A_windowed_mode_is_set_as_chosen_and_fullscreen_keeps_the_desktops_aspect()
    {
        var r = new Rig(tab: 3);
        r.Frame();
        var video = r.Page<VideoOptions>();
        video.Working = video.Working with { Mode = 0, Display = 1 };   // 1280x720 windowed
        video.Apply();
        Assert.Contains("mat_setvideomode 1280 720 1", r.Lines.Lines);

        var card = (FixedVideoModes)r.Services.Create<IVideoModeSource>();
        card.Windowed = true;
        card.Current = new UiVideoMode(1280, 720);
        video.Load();
        video.Working = video.Working with { Aspect = 0, Display = 0 };
        r.Frame();
        video.Working = video.Working with { Mode = 2 };                // 1024x768 fullscreen on a 16:9 desktop
        video.Apply();
        Assert.Contains("mat_setvideomode 1024 576 0", r.Lines.Lines);
    }

    [Fact]
    public void The_antialiasing_choices_are_the_cards_and_load_from_samples_and_quality()
    {
        var r = new Rig(tab: 3);
        r.Frame();
        var video = r.Page<VideoOptions>();
        Assert.Equal(2, video.Working.Antialias);                       // None, 2x, 4x(current), 8x
        video.Working = video.Working with { Antialias = 3 };
        video.Apply();
        Assert.Contains("mat_antialias 8", r.Lines.Lines);
        Assert.Contains("mat_aaquality 0", r.Lines.Lines);
    }

    [Theory]
    [InlineData("8", "0", 4, "mat_forceaniso 8", "mat_trilinear 0")]
    [InlineData("1", "1", 1, "mat_forceaniso 1", "mat_trilinear 1")]
    [InlineData("0", "0", 0, "mat_forceaniso 1", "mat_trilinear 0")]
    public void Filtering_is_anisotropy_or_trilinear_as_stock_mapped_it(string aniso, string trilinear, int choice, string anisoLine, string triLine)
    {
        var r = new Rig(s => s.Add<IUiConVarStore>(OptionsSamples.Convars().With("mat_forceaniso", aniso).With("mat_trilinear", trilinear)), 3);
        r.Frame();
        var video = r.Page<VideoOptions>();
        Assert.Equal(choice, video.Working.Filtering);

        // From another setting, that choice writes what the store held.
        r = new Rig(s => s.Add<IUiConVarStore>(OptionsSamples.Convars().With("mat_forceaniso", "16").With("mat_trilinear", "0")), tab: 3);
        r.Frame();
        video = r.Page<VideoOptions>();
        video.Working = video.Working with { Filtering = choice };
        video.Apply();
        Assert.Contains(anisoLine, r.Lines.Lines);
        Assert.Contains(triLine, r.Lines.Lines);
    }

    // ---- voice ----

    [Fact]
    public void The_microphone_is_written_only_when_the_engine_has_the_controls()
    {
        var mic = new MemoryMicrophone { Available = false, Volume = 0.6f };
        var r = new Rig(s => s.Add<IMicrophoneSettings>(mic));
        r.Frame();
        var voice = r.Page<VoiceOptions>();
        voice.Working.Microphone = 0.2f;
        voice.Apply();
        Assert.Equal(0.6f, mic.Volume);
        mic.Available = true;
        voice.Working.Microphone = 0.3f;
        voice.Working.Boost = true;
        voice.Apply();
        Assert.Equal((0.3f, true), (mic.Volume, mic.Boost));
    }

    // ---- multiplayer ----

    [Fact]
    public void The_models_are_every_vmt_under_the_folder_and_a_logo_needs_its_vmt()
    {
        var r = new Rig();
        r.Frame();
        var mp = r.Page<MultiplayerOptions>();
        Assert.Equal(["police.vmt", "humans/group03/male_07.vmt", "humans/group03/female_02.vmt", "combine/super_soldier.vmt"],
                     mp.Models);
        Assert.Equal(["lambda", "smiley"], mp.Logos);
        Assert.Equal("humans/group03/male_07.vmt", mp.Models[mp.Working.Model]);
        Assert.Equal(0, mp.Working.Logo);
        Assert.Equal(1, mp.Working.DownloadFilter);
    }

    [Fact]
    public void Applied_the_logo_and_model_go_as_console_lines_and_the_rest_as_convars()
    {
        var r = new Rig();
        r.Frame();
        var mp = r.Page<MultiplayerOptions>();
        mp.Working = mp.Working with { Logo = 1, Model = 0, DownloadFilter = 3, Name = "Alyx" };
        mp.Apply();
        Assert.Contains("cl_logofile materials/vgui/logos/smiley.vtf", r.Lines.Lines);
        Assert.Contains("cl_playermodel models/police.mdl", r.Lines.Lines);
        Assert.Contains(("cl_downloadfilter", "none"), r.Convars.Writes);
        Assert.Contains(("name", "Alyx"), r.Convars.Writes);
    }

    [Fact]
    public void The_advanced_entries_save_through_the_script()
    {
        var r = new Rig(tab: 5);
        r.Frame();
        var mp = r.Page<MultiplayerOptions>();
        var script = (MemoryOptionsScript)r.Services.Create<IOptionsScriptSource>();
        mp.Values[2] = "1";
        Assert.True(mp.AnyDirty);
        mp.Apply();
        Assert.Equal("1", script.Saved.Single()[2]);
        Assert.False(mp.AnyDirty);
    }

    // ---- layout ----

    /// <summary>The audio page's mute toggle: a short title in the label
    /// column and its sentence after the box, in the field column (its long
    /// label once ran under the box).</summary>
    [Fact]
    public void A_toggles_sentence_sits_after_its_box_and_its_title_in_the_label_column()
    {
        var r = new Rig(tab: 2);
        r.Frame();
        var els = UiTestKit.Elements(r.Ctx);
        int Find(string token) => Array.FindIndex(els, e => UiTestKit.RunText(r.Ctx, Array.IndexOf(els, e)) == token);
        int title = Find("#SS_UI_Audio_Background"), sentence = Find("#SS_UI_Audio_MuteUnfocused");
        Assert.True(title >= 0 && sentence >= 0, "the mute toggle's title and sentence were not both drawn");
        Assert.True(els[title].Rect.X + els[title].Rect.W <= els[title].Rect.X + UiForm<MouseOptions.Model>.LabelColumnDu);
        Assert.True(els[sentence].Rect.X >= els[title].Rect.X + UiForm<MouseOptions.Model>.LabelColumnDu,
                    $"the sentence starts at {els[sentence].Rect.X}, inside the label column");
        Assert.True(Math.Abs(els[sentence].Rect.Y - els[title].Rect.Y) < els[title].Rect.H, "the sentence is not on the title's row");
    }

    // ---- opening on a page ----

    [Fact]
    public void A_pages_name_opens_the_window_on_that_page()
    {
        var r = new Rig();
        Assert.Contains("videooptions", r.Window.Aliases);
        Assert.Contains("mouseoptions", r.Window.Aliases);
        Assert.Empty(r.Window.Panels);                       // no viewport panel's
        r.Window.PanelOpened("VideoOptions");
        Assert.Equal(3, r.Window.Tab);
        r.Window.PanelOpened("nosuchpage");
        Assert.Equal(3, r.Window.Tab);
    }

    /// <summary>The composed UI: cs_ui_open's name for a page opens the options window on it.</summary>
    [Fact]
    public void The_composed_ui_opens_the_options_window_on_a_named_page()
    {
        string dir = Directory.CreateTempSubdirectory("ss-options-").FullName;
        try
        {
            var engine = TestServices.Fake().Returns("Hud.LocalPlayerHealth", 80).Returns("Clock.Now", 3f)
                .Returns("Hud.LocalPlayerIndex", 1).Returns("Console.FindConVarValue", "test server")
                .Returns("Environment.Globals", default(SourceSharp.Abi.Globals));
            var services = UiHostKit.RuntimeServices(engine, new UiFidelityTests.RecordingSurface(), dir);
            var hud = new SourceSharp.Client.Hud(new RecordingLog(), engine, engine, new DamageMessage(), new HudMsgMessage(),
                              new HudTextMessage(), new KeyHintTextMessage(), new BuildInfo());
            var ui = ClientUi.Build(services, new RecordingLog(), hud, hud.Messages, hud.Hints, hud.Build, new UiHostKit.NoArsenal(),
                                    null, new DefaultForwarders.NoEngineClient(), new DefaultForwarders.NoInputSystem());
            Assert.True(ui.Has("audiooptions"));      // cs_ui_open asks before it opens
            Assert.Equal("mainmenu", ui.Menu.ChromeWith);       // the taskbar is up with the main menu
            ui.Open("audiooptions");
            Assert.True(ui.Menu.IsOpen("options"));
            Assert.Equal(2, ui.Menu.Screens.OfType<Options>().Single().Tab);

            // Opened while the GameUI is up, it is not held as a viewport screen is.
            ui.Close("options");
            ui.ShowPauseMenu();
            ui.Open("videooptions");
            Assert.True(ui.Menu.IsOpen("options"));
            Assert.False(ui.Menu.IsHeld("options"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Every tab shows at the window's default width (TF2's wider face needed about 920 du of it, live).</summary>
    [Fact]
    public void Every_tab_fits_the_window_at_its_default_width()
    {
        var ctx = UiSamples.PreviewContext(SourceSharpTheme.Create(), new UiFontLibrary(), UiSamplesTests.Language());
        var options = OptionsSamples.Build(new UiSampleServices());
        UiSamples.Render(ctx, options, UiSamples.Screen(1920, 1080), 100.0);
        var frame = UiTestKit.Named(ctx, "wnd-frame").Rect;
        var last = UiTestKit.Elements(ctx).Where(e => UiTestKit.NameOf(ctx, e).Contains("Advanced", StringComparison.OrdinalIgnoreCase)
                                                      || UiTestKit.NameOf(ctx, e).Contains("ADVANCED", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(last);
        Assert.All(last, e => Assert.True(e.Rect.X + e.Rect.W <= frame.X + frame.W, $"{UiTestKit.NameOf(ctx, e)} ends at {e.Rect.X + e.Rect.W}, the window at {frame.X + frame.W}"));
        Assert.Equal(960f, frame.W, 1);
    }
}
