using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens.Samples;
using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.Client.Ui.Screens;

public static class HudSamples
{
    /// <summary>The HUD from fakes, registered one per interface, built by the services as DI will.</summary>
    public static UiScreen Build(UiSampleServices s, FakeLocalPlayer player, FakeHudText? text = null, FakeBuildStamp? stamp = null,
                                 FakeKillFeed? kills = null, FakeVoice? voice = null)
    {
        player.Register(s);
        (text ?? new FakeHudText()).Register(s);
        s.Add<IBuildStampSource>(stamp ?? new FakeBuildStamp());
        s.Add<IKillFeedSource>(kills ?? new FakeKillFeed());
        s.Add<IVoiceSource>(voice ?? new FakeVoice());
        return s.Create<Hud>();
    }

    [UiSample("full health, SMG")]
    private static UiScreen Full(UiSampleServices s) => Build(s, new FakeLocalPlayer());

    [UiSample("low health, hurt from the left")]
    private static UiScreen Hurt(UiSampleServices s) => Build(s, new FakeLocalPlayer
    {
        Health = 18, Armour = 0, WeaponName = "#HL2_Pistol_Menu", Clip = 3, Reserve = 18, Secondary = -1, SecondaryMax = -1,
        LastDamageAt = 99.75, LastDamageYaw = -90,
    });

    [UiSample("map text, hints, empty clip")]
    private static UiScreen MapText(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { Health = 74, Armour = 15, WeaponName = "#HL2_357Handgun_Menu", Clip = 0, Reserve = 12, Secondary = -1, SecondaryMax = -1 },
        new FakeHudText
        {
            Messages =
            [
                new HudTextLine("CONTROL POINT B IS UNDER ATTACK", -1, 0.22f, new UiColor(255, 190, 60, 255), 1f),
                new HudTextLine("Lockdown lifts in 30 seconds", 0.04f, 0.42f, new UiColor(140, 220, 255, 255), 1f),
            ],
            Hints = [("E", "Use the health charger"), ("R", "Reload")],
        },
        new FakeBuildStamp
        {
            Stale = true,
            Line = "n 19:04:12 1a0e REBUILT-cs_cl_reload · m 20:31:40 4337 · client-ui@8cd02c00",
            DebugLine = "t 34.305  pos -2739.0 2563.0 192.8  ang 0.0 -150.0",
        });

    [UiSample("SMG, 2 of 3 grenades")]
    private static UiScreen SmgGrenades(UiSampleServices s) => Build(s, new FakeLocalPlayer { Secondary = 2, SecondaryMax = 3 });

    [UiSample("AR2, 0 of 3 balls")]
    private static UiScreen Ar2Empty(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { WeaponName = "#HL2_Pulse_Rifle_Menu", Clip = 30, Reserve = 60, Secondary = 0, SecondaryMax = 3 });

    [UiSample("pistol, no secondary")]
    private static UiScreen Pistol(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { WeaponName = "#HL2_Pistol_Menu", Clip = 18, Reserve = 150, Secondary = -1, SecondaryMax = -1 });

    [UiSample("secondary, no clip")]
    private static UiScreen NoClip(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { WeaponName = "#HL2_Pulse_Rifle_Menu", Clip = -1, Reserve = 60, Secondary = 1, SecondaryMax = 3 });

    [UiSample("crowbar")]
    private static UiScreen Crowbar(UiSampleServices s) => Build(s,
        new FakeLocalPlayer { WeaponName = "#HL2_Crowbar_Menu", Clip = -1, Reserve = -1, Secondary = -1, SecondaryMax = -1 });

    [UiSample("kill feed, busy")]
    private static UiScreen KillsBusy(UiSampleServices s) => Build(s, new FakeLocalPlayer(), kills: new FakeKillFeed
    {
        Lines =
        [
            FakeKillFeed.Kill("Alyx", 2, "Barney", 3, "smg1"),
            FakeKillFeed.Kill("Barney", 3, "Grigori", 2, "shotgun"),
            FakeKillFeed.Kill("Gordon", 3, "Alyx", 2, "crossbow_bolt", local: true),
            FakeKillFeed.Kill("Grigori", 2, "Kleiner", 3, "rpg_missile"),
        ],
    });

    [UiSample("kill feed, suicide and the world")]
    private static UiScreen KillsSuicide(UiSampleServices s) => Build(s, new FakeLocalPlayer(), kills: new FakeKillFeed
    {
        Lines = [FakeKillFeed.Suicide("Barney", 3, "grenade_frag"), FakeKillFeed.Suicide("Alyx", 2, "worldspawn")],
    });

    [UiSample("kill feed, headshot")]
    private static UiScreen KillsHeadshot(UiSampleServices s) => Build(s, new FakeLocalPlayer(), kills: new FakeKillFeed
    {
        Lines = [FakeKillFeed.Kill("Alyx", 2, "Barney", 3, "357", headshot: true)],
    });

    [UiSample("kill feed, local player killed")]
    private static UiScreen KillsLocal(UiSampleServices s) => Build(s, new FakeLocalPlayer { Alive = false }, kills: new FakeKillFeed
    {
        Lines = [FakeKillFeed.Kill("Alyx", 2, "Gordon", 3, "ar2", local: true)],
    });

    [UiSample("kill feed, long names, no teams")]
    private static UiScreen KillsLong(UiSampleServices s) => Build(s, new FakeLocalPlayer(), kills: new FakeKillFeed
    {
        Lines =
        [
            FakeKillFeed.Kill("xX_TheOneFreeManWithTheCrowbarOfDestiny_Xx", 0, "Dr. Wallace Breen, Administrator of Earth", 0, "physics"),
            FakeKillFeed.Kill("Gordon", 0, "a-player-with-a-name-that-goes-on-and-on-forever", 0, "stunstick"),
        ],
    });

    [UiSample("voice, three speaking and self")]
    private static UiScreen Voice(UiSampleServices s) => Build(s, new FakeLocalPlayer(), voice: new FakeVoice
    {
        LocalSpeaking = true,
        Speakers = [new("Alyx", 2, true), new("Barney", 3, false), new("Dr. Wallace Breen, Administrator", 0, true)],
    });

    [UiSample("menu, no local player")]
    private static UiScreen NoPlayer(UiSampleServices s) => Build(s, new FakeLocalPlayer { Alive = false });
}
