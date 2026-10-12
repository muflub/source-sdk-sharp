using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

internal static class SpectatorTargetSamples
{
    private static UiScreen Build(UiSampleServices s, FakeSpectatorTarget target)
        => s.Add<ISpectatorTargetSource>(target).Create<SpectatorTarget>();

    [UiSample("Rebels, full health, SMG")]
    private static UiScreen Rebel(UiSampleServices s) => Build(s, new FakeSpectatorTarget());

    [UiSample("Combine, low health, pistol")]
    private static UiScreen CombineLow(UiSampleServices s) => Build(s, new FakeSpectatorTarget
    {
        Name = "Overwatch", Team = 2, Health = 12, WeaponName = "#HL2_Pistol_Menu",
    });

    [UiSample("longest name, overcharged, RPG")]
    private static UiScreen LongName(UiSampleServices s) => Build(s, new FakeSpectatorTarget
    {
        Name = "xXx_Headcrab_Slayer_From_Ravenholm_xXx", Team = 2, Health = 250, WeaponName = "#HL2_RPG_Menu",
    });

    [UiSample("deathmatch, no team")]
    private static UiScreen NoTeam(UiSampleServices s) => Build(s, new FakeSpectatorTarget
    {
        Name = "barney.calhoun", Team = 0, Health = 64, WeaponName = "#HL2_Crowbar_Menu",
    });

    [UiSample("target dead")]
    private static UiScreen Dead(UiSampleServices s) => Build(s, new FakeSpectatorTarget
    {
        Name = "Dr. Kleiner", Health = 0, WeaponName = string.Empty,
    });
}
