using SourceSharp.Abi;
using TF2.Server.Buildings;
using TF2.Shared;

namespace SourceSharp.Ai.TF2.Knowledge;

/// <summary>
/// TF2's abilities as a bot's usercmds and client commands reach them (plan_bots_teams_tf2 C.3,
/// P6 step 4): the rows of the binding's <see cref="AbilityTable"/>, which an
/// <see cref="ActionKind.Ability"/> one-shot names by index. Every row is a key a player presses or
/// a word a player's key sends: the Soldier's rocket jump as a sequence (look down, jump and duck,
/// fire at the ground, stay ducked through the climb), the Heavy's spin-up and the Medic's Über as
/// the second trigger, the Engineer's build and destroy orders as the mod's own verbs.
/// </summary>
public static class TFAbilities
{
    public const string RocketJump = "rocket_jump", SpinUp = "spin_up", Uber = "uber";

    /// <summary>The rocket jump's look: down at the floor ahead (plan C.3's 70°), a launcher's own self-hit.</summary>
    public const float RocketJumpPitch = 70f;

    /// <summary>Commands the rocket jump looks down for before it jumps: the motion reacts, then flicks.</summary>
    public const int RocketJumpLookTicks = 40;

    /// <summary>Commands a spin-up holds the second trigger: 0.75 s of the minigun's wind (TFMinigunBehaviour), and a little over.</summary>
    public const int SpinUpTicks = 60;

    private const int Jump = (int)InputButtons.Jump, Duck = (int)InputButtons.Duck, Attack = (int)InputButtons.Attack, Attack2 = (int)InputButtons.Attack2;

    /// <summary>The build order's word for a building of <paramref name="type"/> and <paramref name="mode"/> (a teleporter's 0 entrance, 1 exit).</summary>
    public static string Build(TFObjectType type, int mode = 0) => $"{TFBuildCommand.Verb} {(int)type} {mode}";

    /// <summary>The destroy order's word for the bot's own building of <paramref name="type"/> and <paramref name="mode"/>.</summary>
    public static string Destroy(TFObjectType type, int mode = 0) => $"{TFDestroyCommand.Verb} {(int)type} {mode}";

    public static AbilityTable Table() => new([
        new AbilityRow(RocketJump, AbilityRun.Sequence, Steps: [
            new AbilityStep(0, RocketJumpLookTicks, RocketJumpPitch),
            new AbilityStep(Jump | Duck, 1, RocketJumpPitch),
            new AbilityStep(Jump | Duck | Attack, 2, RocketJumpPitch),
            new AbilityStep(Duck, 30),
        ]),
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
    ]);
}
