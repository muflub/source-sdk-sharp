using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// The preview run's HUD-art seed: a run that decoded --content textures
/// seeds <see cref="UiSampleServices.HudArt"/>, and the screen a sample then
/// builds draws real pixels; seedless builds (the tests, the lint) answer the
/// stateless none, every name misses, and screens draw their fallbacks. The
/// seed also wins over a registration: a sample's own fake must not hide the
/// --content art the run was asked for.
/// </summary>
public class UiSampleArtTests
{
    private sealed class RecordingLibrary : IUiTextureLibrary
    {
        public List<string> Asked = [];

        public int Find(string name)
        {
            Asked.Add(name);
            return 0x51;
        }

        public int Material(string name) => 0;

        public UiTextureInfo? About(string name) => new(0x51, 128, 128, "RGBA8888");
        public UiTextureIcon? Icon(string name) { Asked.Add(name); return null; }
    }

    private sealed class ProbeScreen(IUiTextureLibrary textures) : UiScreen
    {
        public IUiTextureLibrary Textures => textures;

        public override string Name => "probe hud art";

        public override void Describe(ref UiFrame ui)
            => ui.Texture(textures.Find("hud/health_bg"), Size.Du(124), Size.Du(124)).Name("cross");
    }

    [UiSample("seeded")]
    private static UiScreen Seeded(UiSampleServices s) => new ProbeScreen(s.Create<IUiTextureLibrary>());

    [UiSample("seedless probe")]
    private static UiScreen Seedless(UiSampleServices s)
        => new ProbeScreen(s.Add<IUiTextureLibrary>(new RecordingLibrary()).Create<IUiTextureLibrary>());

    private static UiSample Sample(string slug)
        => Assert.Single(UiSamples.Discover(typeof(UiSampleArtTests).Assembly),
                         s => !s.Gallery && s.Slug == slug);

    [Fact]
    public void A_seeded_sample_builds_its_screen_on_the_seeded_library()
    {
        var library = new RecordingLibrary();
        var screen = Assert.IsType<ProbeScreen>(Sample("seeded").Create(library));
        // The screen holds the seeded library itself, and its asks go to it.
        Assert.Same(library, screen.Textures);
        Assert.Equal(0x51, screen.Textures.Find("hud/health_bg"));
        Assert.Equal(["hud/health_bg"], library.Asked);
    }

    [Fact]
    public void A_seedless_build_answers_the_stateless_none()
    {
        var screen = Assert.IsType<ProbeScreen>(Sample("seedless-probe").Create());
        Assert.Equal(0, screen.Textures.Find("hud/health_bg"));
        Assert.Null(screen.Textures.About("hud/health_bg"));
    }

    [Fact]
    public void The_seed_wins_over_a_sample_registration()
    {
        var library = new RecordingLibrary();
        // The probe sample registers its own library; the seed still reaches the screen.
        var screen = (ProbeScreen)Sample("seedless-probe").Create(library);
        Assert.Same(library, screen.Textures);
    }
}
