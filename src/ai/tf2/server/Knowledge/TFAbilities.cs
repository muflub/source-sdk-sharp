using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Server.Voice;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// TF2's abilities as a bot's usercmds and client commands reach them (plan_bots_teams_tf2 C.3,
/// P6 step 4): the rows of the binding's <see cref="AbilityTable"/>, which an
/// <see cref="ActionKind.Ability"/> one-shot names by index. Every row is a key a player presses or
/// a word a player's key sends: the Soldier's rocket jump as a program (the binding's
/// <see cref="Jump.RocketJumpProgram"/>, which flies a planned recipe with the pure pilot a command
/// at a time, plan_bots_teams_tf2_part_g G.5 step 2), the Heavy's spin-up and the Medic's Über as
/// the second trigger, the Engineer's build and destroy orders and the voice menu's lines as the
/// mod's own verbs.
/// </summary>
public static class TFAbilities
{
    public const string RocketJump = "rocket_jump", SpinUp = "spin_up", Uber = "uber";

    /// <summary>The voice menu's lines a bot says (plan_bots_teams_tf2_part_g G.2.4): "Medic!", "Thanks!", "Spy!" and "Sentry ahead!".</summary>
    public const string VoiceMedic = "voice_medic", VoiceThanks = "voice_thanks", VoiceSpy = "voice_spy", VoiceSentryAhead = "voice_sentry_ahead";

    /// <summary>
    /// Where those lines sit in the game's voice menus (its <c>scripts/voicecommands.txt</c>, which
    /// the mod reads: <see cref="TFVoiceCommandMenus"/>), as menu and item, both from 0: the first
    /// menu's first two items speak TLK_PLAYER_MEDIC and TLK_PLAYER_THANKS, the second menu's second
    /// and third TLK_PLAYER_CLOAKEDSPY and TLK_PLAYER_SENTRYAHEAD.
    /// </summary>
    public const int MedicMenu = 0, MedicItem = 0, ThanksMenu = 0, ThanksItem = 1, SpyMenu = 1, SpyItem = 1, SentryAheadMenu = 1, SentryAheadItem = 2;

    /// <summary>The voice menu's word for a line, as a player's key sends it.</summary>
    public static string Voice(int menu, int item) => $"{TFVoiceMenuCommand.Verb} {menu} {item}";

    /// <summary>The most commands a rocket jump's program may take: its approach's limit, and a flight and its landing (12 s).</summary>
    public const int RocketJumpCommands = 800;

    /// <summary>Commands a spin-up holds the second trigger: 0.75 s of the minigun's wind (TFMinigunBehaviour), and a little over.</summary>
    public const int SpinUpTicks = 60;

    private const int Attack2 = (int)InputButtons.Attack2;

    /// <summary>The build order's word for a building of <paramref name="type"/> and <paramref name="mode"/> (a teleporter's 0 entrance, 1 exit).</summary>
    public static string Build(TFObjectType type, int mode = 0) => $"{TFBuildCommand.Verb} {(int)type} {mode}";

    /// <summary>The destroy order's word for the bot's own building of <paramref name="type"/> and <paramref name="mode"/>.</summary>
    public static string Destroy(TFObjectType type, int mode = 0) => $"{TFDestroyCommand.Verb} {(int)type} {mode}";

    public static AbilityTable Table() => new([
        new AbilityRow(RocketJump, AbilityRun.Program, HoldTicks: RocketJumpCommands),
        new AbilityRow(SpinUp, AbilityRun.Buttons, Buttons: Attack2, HoldTicks: SpinUpTicks),
        new AbilityRow(Uber, AbilityRun.Buttons, Buttons: Attack2, HoldTicks: 2),
        new AbilityRow("build_sentry", AbilityRun.Command, Build(TFObjectType.Sentrygun)),
        new AbilityRow("build_dispenser", AbilityRun.Command, Build(TFObjectType.Dispenser)),
        new AbilityRow("build_teleporter_entrance", AbilityRun.Command, Build(TFObjectType.Teleporter, 0)),
        new AbilityRow("build_teleporter_exit", AbilityRun.Command, Build(TFObjectType.Teleporter, 1)),
        new AbilityRow("destroy_sentry", AbilityRun.Command, Destroy(TFObjectType.Sentrygun)),
        new AbilityRow("destroy_dispenser", AbilityRun.Command, Destroy(TFObjectType.Dispenser)),
        new AbilityRow("destroy_teleporter_entrance", AbilityRun.Command, Destroy(TFObjectType.Teleporter, 0)),
        new AbilityRow("destroy_teleporter_exit", AbilityRun.Command, Destroy(TFObjectType.Teleporter, 1)),
        new AbilityRow(VoiceMedic, AbilityRun.Command, Voice(MedicMenu, MedicItem)),
        new AbilityRow(VoiceThanks, AbilityRun.Command, Voice(ThanksMenu, ThanksItem)),
        new AbilityRow(VoiceSpy, AbilityRun.Command, Voice(SpyMenu, SpyItem)),
        new AbilityRow(VoiceSentryAhead, AbilityRun.Command, Voice(SentryAheadMenu, SentryAheadItem)),
    ]);
}
