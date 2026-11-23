using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Ai.TF2;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using TF2.Shared;
using Xunit;

namespace SourceSharp.Ai.Tests.TF2;

/// <summary>
/// The class pick through the service itself (plan_bots_teams_tf2 C.4; review-p7f F2): <see cref="TFBotClassPick.Step"/>,
/// the body of its frame after the engine reads, on the service's own clock, latches and asks; and its level hook.
/// Every class is ready here (<c>ai_bot_classes_ready</c>), so a team of Soldiers wants a Medic.
/// </summary>
public sealed class TFClassPickServiceTests
{
    private sealed class Commands : IBotClientCommand
    {
        public List<(EntityHandle Bot, string Command)> Sent { get; } = [];

        public void Run(EntityHandle bot, string command) => Sent.Add((bot, command));
    }

    private readonly Commands _commands = new();
    private readonly TFBotClassPick _pick;
    private const float Cooldown = 30f, Stay = 120f;

    public TFClassPickServiceTests()
    {
        var convars = new ConVarRegistry(new NamedConVars(new Dictionary<string, string>
        {
            ["ai_bot_classes_ready"] = "scout,sniper,demoman,medic,heavy,pyro,spy,engineer", ["ai_bot_class_cooldown"] = "30", ["ai_bot_class_stay"] = "120",
        }));
        _pick = new TFBotClassPick(null!, null!, null!, _commands, null!, new TFBotPins(), convars.Declare<AiBotClassCooldownConVar>(),
                                   convars.Declare<AiBotClassStayConVar>(), convars.Declare<AiBotClassesReadyConVar>(), null!, null!);
    }

    private static EntityHandle Player(int slot) => new(0x0001_0000u | (uint)slot);

    private static TFClassSeat Soldier(int slot, bool alive, TFBotPin? pin = null) => new(slot, Player(slot), (int)TFTeam.Red, TFClass.Soldier, alive, pin);

    /// <summary>A red team of <paramref name="soldiers"/> Soldiers and nothing else, by class number.</summary>
    private static int[] Red(int soldiers)
    {
        var counts = new int[(int)TFClass.CountAll];
        counts[(int)TFClass.Soldier] = soldiers;
        return counts;
    }

    private void Step(float now, params TFClassSeat[] seats) => _pick.Step(now, false, seats, Red(5), new int[(int)TFClass.CountAll], Cooldown, Stay);

    private int Changes => _commands.Sent.Count(c => c.Command != TFBotClassPick.Word(TFClass.Soldier));

    /// <summary>Two bots dying together make one change: the window lets one through, and the other is held.</summary>
    [Fact]
    public void TwoBotsDyingTogetherMakeOneChange()
    {
        Step(10f, Soldier(3, alive: false), Soldier(4, alive: false));
        Assert.Equal((1, 1, 1), (Changes, _pick.Clock.Changes, _pick.Clock.Held));
        Assert.Equal((Player(3), "joinclass medic"), _commands.Sent.Single());
    }

    /// <summary>A bot inside its own stay does not change on its next death, though the window is open again.</summary>
    [Fact]
    public void ABotInsideItsStayDoesNotChange()
    {
        Step(10f, Soldier(3, alive: false));
        Step(50f, Soldier(3, alive: true));
        Step(100f, Soldier(3, alive: false));
        Assert.Equal((1, 1), (Changes, _pick.Clock.Held));
        // The control: past its stay, the same death is a change.
        Step(140f, Soldier(3, alive: true));
        Step(200f, Soldier(3, alive: false));
        Assert.Equal(2, Changes);
    }

    /// <summary>A death is weighed once: a bot the window held stays as it is until it has lived and died again.</summary>
    [Fact]
    public void ADeathIsWeighedOnce()
    {
        Step(10f, Soldier(3, alive: false));
        Step(15f, Soldier(4, alive: false));
        Step(45f, Soldier(4, alive: false));
        Assert.Equal(1, Changes);
        Step(50f, Soldier(4, alive: true));
        Step(60f, Soldier(4, alive: false));
        Assert.Equal(2, Changes);
    }

    /// <summary>
    /// The clock is the level's: a change at 1,500 s shuts the window, and after the level's shutdown the next level's
    /// clock (which starts again) finds it open at 40 s. The control: with no shutdown it is still shut.
    /// </summary>
    [Fact]
    public void ANewLevelResetsTheClock()
    {
        Step(1500f, Soldier(3, alive: false));
        Step(40f, Soldier(4, alive: false));
        Assert.Equal(1, Changes);
        _pick.OnLevelShutdownPreEntity();
        Step(41f, Soldier(4, alive: true));
        Step(42f, Soldier(4, alive: false));
        Step(43f, Soldier(3, alive: true));
        Step(44f, Soldier(3, alive: false));
        Assert.Equal(2, Changes);
        Assert.Equal(Player(4), _commands.Sent[^1].Bot);
    }

    /// <summary>A pinned bot never changes, however much its team needs another class.</summary>
    [Fact]
    public void APinnedBotNeverChanges()
    {
        var pin = new TFBotPin(new ActorId(3, 1), 7, TFClass.Soldier, "");
        Step(10f, Soldier(3, alive: false, pin));
        Assert.Equal((0, 0), (Changes, _pick.Clock.Held));
    }

    /// <summary>A bot with no class asks for its pinned class, else the one its team needs, and that does not start the clock.</summary>
    [Fact]
    public void AFirstPickAsksAndStartsNoClock()
    {
        _pick.Step(5f, false, [new TFClassSeat(3, Player(3), (int)TFTeam.Red, TFClass.Undefined, true, null)], Red(5), new int[(int)TFClass.CountAll], Cooldown, Stay);
        Assert.Equal("joinclass medic", _commands.Sent.Single().Command);
        Assert.True(_pick.Clock.Open(6f, Cooldown, setup: false));
    }
}
