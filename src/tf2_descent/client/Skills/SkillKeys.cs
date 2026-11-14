using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Input;

using TF2.Descent.Shared;

namespace TF2.Descent.Client.Skills;

//=============================================================================
// The hero's skill keys (plan_descent_logic HE6). Six skill slots on 4 to 9:
// a Soldier's weapons are slots 1 to 3, so TF2's slot4..slot9 select nothing
// for him, and a skill bar beside the weapon keys is where an ARPG puts it.
// Each key is held (+/-): the press and the release both reach the server,
// so a charge skill builds while the key is down. Their Order comes after
// TF2's whole list (its last is 700), so on a fresh config these binds are
// the ones the keys end with. The belt's four potion keys are RESERVED for
// HE8 and not declared here: they would bind a command nothing answers yet
// (SkillKeys.BeltKeys names the keys kept for them).
//=============================================================================

/// <summary>The skill keys' shared numbers.</summary>
public static class SkillKeys
{
    /// <summary>The keys of skill slots 1 to 6.</summary>
    public static IReadOnlyList<string> SlotKeys => ["4", "5", "6", "7", "8", "9"];

    /// <summary>The keys kept for the belt's four potions (HE8): the keypad's 1 to 4, which TF2 binds to nothing.</summary>
    public static IReadOnlyList<string> BeltKeys => ["KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW"];

    /// <summary>The line a slot's key edge sends the server: <c>tf2_descent_cast &lt;slot&gt; &lt;1|0&gt;</c>.</summary>
    public static string CastLine(int slot, bool down) => $"tf2_descent_cast {slot} {(down ? 1 : 0)}";
}

[KeyAction("+tf2_descent_skill1", "Skill 1", Section = "Descent", Order = 1010, Keys = ["4"])]
public sealed class Skill1Key;

[KeyAction("+tf2_descent_skill2", "Skill 2", Section = "Descent", Order = 1020, Keys = ["5"])]
public sealed class Skill2Key;

[KeyAction("+tf2_descent_skill3", "Skill 3", Section = "Descent", Order = 1030, Keys = ["6"])]
public sealed class Skill3Key;

[KeyAction("+tf2_descent_skill4", "Skill 4", Section = "Descent", Order = 1040, Keys = ["7"])]
public sealed class Skill4Key;

[KeyAction("+tf2_descent_skill5", "Skill 5", Section = "Descent", Order = 1050, Keys = ["8"])]
public sealed class Skill5Key;

[KeyAction("+tf2_descent_skill6", "Skill 6", Section = "Descent", Order = 1060, Keys = ["9"])]
public sealed class Skill6Key;

/// <summary>A skill key's edge, to the server as the client's command line (forwarded: the client answers no such command).</summary>
public abstract class SkillKeyCommand(IFwdEngineClient engine, int slot, bool down)
{
    // A held key's line carries its key code and the time ("+tf2_descent_skill1 52 1234"): taken and not read.
    public void Execute(params string[] keyAndTime) => engine.SubmitConsoleLine(SkillKeys.CastLine(slot, down));
}

[ConCommand("+tf2_descent_skill1", Help = "Skill slot 1's key down")]
public sealed class Skill1Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 1, true);

[ConCommand("-tf2_descent_skill1", Help = "Skill slot 1's key up")]
public sealed class Skill1Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 1, false);

[ConCommand("+tf2_descent_skill2", Help = "Skill slot 2's key down")]
public sealed class Skill2Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 2, true);

[ConCommand("-tf2_descent_skill2", Help = "Skill slot 2's key up")]
public sealed class Skill2Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 2, false);

[ConCommand("+tf2_descent_skill3", Help = "Skill slot 3's key down")]
public sealed class Skill3Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 3, true);

[ConCommand("-tf2_descent_skill3", Help = "Skill slot 3's key up")]
public sealed class Skill3Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 3, false);

[ConCommand("+tf2_descent_skill4", Help = "Skill slot 4's key down")]
public sealed class Skill4Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 4, true);

[ConCommand("-tf2_descent_skill4", Help = "Skill slot 4's key up")]
public sealed class Skill4Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 4, false);

[ConCommand("+tf2_descent_skill5", Help = "Skill slot 5's key down")]
public sealed class Skill5Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 5, true);

[ConCommand("-tf2_descent_skill5", Help = "Skill slot 5's key up")]
public sealed class Skill5Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 5, false);

[ConCommand("+tf2_descent_skill6", Help = "Skill slot 6's key down")]
public sealed class Skill6Down(IFwdEngineClient engine) : SkillKeyCommand(engine, 6, true);

[ConCommand("-tf2_descent_skill6", Help = "Skill slot 6's key up")]
public sealed class Skill6Up(IFwdEngineClient engine) : SkillKeyCommand(engine, 6, false);

/// <summary>
/// The hero's skill bar as the server last sent it (<see cref="SkillBarMessage"/>):
/// what a cooldown sweep and a cast bar draw. Ticks are the server's.
/// </summary>
[SideService(Side.Client)]
public sealed class SkillBarState : ISkillBarHook, IOrderedHook
{
    public int HookOrder => 20;

    /// <summary>The last bar, or null before the first.</summary>
    public SkillBarView? Bar { get; private set; }

    public void OnSkillBar(SkillBarView bar) => Bar = bar;
}

/// <summary>The client's dispatch of the skill bar's hook.</summary>
[SideService(Side.Client)]
public sealed class SkillClientRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<ISkillBarHook>();
}
