using System.Globalization;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

/// <summary>Stock's closed caption choices, in order.</summary>
public enum ClosedCaptions
{
    None,
    SubtitlesAndSoundEffects,
    SubtitlesOnly,
}

/// <summary>Stock's sound quality, made of two convars.</summary>
public enum SoundQuality
{
    Low,
    Medium,
    High,
}

/// <summary>Stock's speaker setups; the value each writes is <see cref="AudioOptions.SpeakerCount"/>'s.</summary>
public enum SpeakerSetup
{
    Headphones,
    TwoSpeakers,
    FourSpeakers,
    FiveOneSurround,
    SevenOneSurround,
}

/// <summary>
/// The audio options (plan_gameui_managed.md G2; stock: GameUI's audio page):
/// the two volumes, closed captions (closecaption is a userinfo convar, so it
/// goes through the console as stock sent it, and cc_subtitles beside it),
/// sound quality (dsp_slow_cpu and Snd_PitchQuality), the speaker setup, and
/// muting while the window is not focused. Headphones at high quality turn
/// dsp_enhance_stereo on, as stock's apply did. The spoken language (a Steam
/// app language) and the speaker test map are not offered.
/// </summary>
public sealed partial class AudioOptions(UiWindows windows, IUiConVarStore convars, IUiCommandLine commands)
    : UiForm<AudioOptions.Model>(windows, convars)
{
    [UiFormModel]
    public sealed partial class Model
    {
        [Field("#SS_UI_Audio_Volume", Min = 0f, Max = 1f, Step = 0.05f, Section = "#SS_UI_Audio_Levels")]
        public float Volume = 1f;

        [Field("#SS_UI_Audio_Music", Min = 0f, Max = 1f, Step = 0.05f, Section = "#SS_UI_Audio_Levels")]
        public float Music = 1f;

        [Field("#SS_UI_Audio_Captions", Section = "#SS_UI_Audio_Sound")]
        public ClosedCaptions Captions;

        [Field("#SS_UI_Audio_Quality", Section = "#SS_UI_Audio_Sound")]
        public SoundQuality Quality = SoundQuality.High;

        [Field("#SS_UI_Audio_Speakers", Section = "#SS_UI_Audio_Sound")]
        public SpeakerSetup Speakers = SpeakerSetup.TwoSpeakers;

        [Field("#SS_UI_Audio_Background", Section = "#SS_UI_Audio_Sound", Description = "#SS_UI_Audio_MuteUnfocused")]
        public bool MuteUnfocused;
    }

    private readonly IUiConVarStore _convars = convars;

    public override string Name => "audiooptions";

    public override string WindowId => "audiooptions";

    protected override string TitleToken => "#SS_UI_Audio_Title";

    /// <summary>Snd_Surround_Speakers' value for each setup.</summary>
    public static int SpeakerCount(SpeakerSetup setup) => setup switch
    {
        SpeakerSetup.Headphones => 0,
        SpeakerSetup.FourSpeakers => 4,
        SpeakerSetup.FiveOneSurround => 5,
        SpeakerSetup.SevenOneSurround => 7,
        _ => 2,
    };

    protected override void Bind(UiFormBindings<Model> b)
        => b.ConVar(Model.Fields.Volume, "volume")
            .ConVar(Model.Fields.Music, "Snd_MusicVolume")
            .ConVar(Model.Fields.MuteUnfocused, "snd_mute_losefocus")
            .Custom(Model.Fields.Captions, LoadCaptions, SaveCaptions)
            .Custom(Model.Fields.Quality, LoadQuality, SaveQuality)
            .Custom(Model.Fields.Speakers, LoadSpeakers, SaveSpeakers);

    private bool On(string name) => Number(name) != 0f;

    private float Number(string name)
        => float.TryParse(_convars.Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    private void LoadCaptions(Model m)
        => m.Captions = !On("closecaption") ? ClosedCaptions.None
            : On("cc_subtitles") ? ClosedCaptions.SubtitlesOnly : ClosedCaptions.SubtitlesAndSoundEffects;

    private void SaveCaptions(Model m)
    {
        _convars.Set("cc_subtitles", m.Captions == ClosedCaptions.SubtitlesOnly ? "1" : "0");
        commands.Submit(m.Captions == ClosedCaptions.None ? "closecaption 0" : "closecaption 1");
    }

    private void LoadQuality(Model m)
        => m.Quality = On("Snd_PitchQuality") ? SoundQuality.High : !On("dsp_slow_cpu") ? SoundQuality.Medium : SoundQuality.Low;

    private void SaveQuality(Model m)
    {
        _convars.Set("dsp_slow_cpu", m.Quality == SoundQuality.Low ? "1" : "0");
        _convars.Set("Snd_PitchQuality", m.Quality == SoundQuality.High ? "1" : "0");
        SaveEnhanceStereo(m);
    }

    // Stock lists only two speakers off Windows and reads no setup (0) or an
    // unset one (-1) as two.
    private void LoadSpeakers(Model m)
    {
        int speakers = (int)Number("Snd_Surround_Speakers");
        if (speakers < 0 || (speakers == 0 && !OperatingSystem.IsWindows()))
            speakers = 2;
        m.Speakers = speakers switch
        {
            0 => SpeakerSetup.Headphones,
            4 => SpeakerSetup.FourSpeakers,
            5 => SpeakerSetup.FiveOneSurround,
            7 => SpeakerSetup.SevenOneSurround,
            _ => SpeakerSetup.TwoSpeakers,
        };
    }

    private void SaveSpeakers(Model m)
    {
        int speakers = OperatingSystem.IsWindows() ? SpeakerCount(m.Speakers) : 2;
        _convars.Set("Snd_Surround_Speakers", speakers.ToString(CultureInfo.InvariantCulture));
        SaveEnhanceStereo(m);
    }

    private void SaveEnhanceStereo(Model m)
        => _convars.Set("dsp_enhance_stereo",
                        m.Speakers == SpeakerSetup.Headphones && m.Quality == SoundQuality.High && OperatingSystem.IsWindows() ? "1" : "0");
}
