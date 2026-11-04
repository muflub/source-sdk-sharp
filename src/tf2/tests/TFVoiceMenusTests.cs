extern alias client;

using SourceSharp.Abi;
using SourceSharp.Tests;

using Xunit;

using TFVoiceMenus = client::TF2.Client.Ui.TFVoiceMenus;
using RadialMenus = client::SourceSharp.Client.Vgui.RadialMenus;
using IRadialPointer = client::SourceSharp.Client.Vgui.IRadialPointer;

namespace TF2.Tests;

/// <summary>The voice menus' labels, from the file's own shape.</summary>
public sealed class TFVoiceMenusTests
{
    private const string File = """
        "voicemenus"
        {
            "menu_0"
            {
                "1" { "concept" "TLK_PLAYER_MEDIC" "menu_label" "#Voice_Menu_Medic" }
                "2" { "concept" "TLK_PLAYER_THANKS" "menu_label" "#Voice_Menu_Thanks" }
            }
            "menu_1"
            {
                "1" { "concept" "TLK_PLAYER_INCOMING" "menu_label" "#Voice_Menu_Incoming" }
            }
        }
        """;

    [Fact]
    public void Each_menu_s_labels_read_in_the_file_s_order()
    {
        var menus = TFVoiceMenus.Parse(File);
        Assert.Equal(2, menus.Length);
        Assert.Equal(["#Voice_Menu_Medic", "#Voice_Menu_Thanks"], menus[0]);
        Assert.Equal(["#Voice_Menu_Incoming"], menus[1]);
    }

    [Theory]
    [InlineData(true, 2, "Scout", 125, true)]
    [InlineData(true, 3, "Spy", 1, true)]
    [InlineData(false, 2, "Scout", 125, false)]   // at the menu
    [InlineData(true, 0, "Scout", 125, false)]    // no team yet
    [InlineData(true, 1, "Scout", 125, false)]    // spectating
    [InlineData(true, 2, "", 125, false)]         // no class yet
    [InlineData(true, 2, "Scout", 0, false)]      // dead
    public void Only_a_player_spawned_in_a_game_can_speak(bool inGame, int team, string className, int health, bool can)
        => Assert.Equal(can, TFVoiceMenus.CanSpeak(inGame, team, className, health, client::SourceSharp.Client.Ui.SpectatorMode.None));

    [Fact]
    public void An_observer_cannot_speak()
        => Assert.False(TFVoiceMenus.CanSpeak(true, 2, "Scout", 125, client::SourceSharp.Client.Ui.SpectatorMode.Roaming));

    [Fact]
    public void A_file_without_the_root_has_no_menus()
        => Assert.Empty(TFVoiceMenus.Parse("\"other\" { }"));

    /// <summary>An interface answering only what a fact names; the rest answer their type's default.</summary>
    public class Stub : System.Reflection.DispatchProxy
    {
        public Dictionary<string, Func<object?[]?, object?>> Answers = [];

        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
        {
            if (Answers.TryGetValue(method!.Name, out var answer))
                return answer(args);
            return method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    private static T Of<T>(params (string Name, Func<object?[]?, object?> Answer)[] answers) where T : class
    {
        var proxy = System.Reflection.DispatchProxy.Create<T, Stub>();
        foreach (var (name, answer) in answers)
            ((Stub)(object)proxy).Answers[name] = answer;
        return proxy;
    }

    private sealed class Pointer : IRadialPointer
    {
        public void Centre()
        {
        }
    }

    private readonly RadialMenus _radial = new(new Pointer());
    private readonly List<string> _lines = [];
    private bool _inGame = true;

    private client::SourceSharp.Abi.IFwdEngineClient Engine()
        => Of<client::SourceSharp.Abi.IFwdEngineClient>(("IsInGame", _ => _inGame), ("SubmitConsoleLine", a => { _lines.Add((string)a![0]!); return null; }));

    private TFVoiceMenus Menus()
        => new(_radial,
               Of<client::SourceSharp.Abi.IEngineFiles>(("ReadGameFile", _ => File)),
               Engine(),
               Of<TF2.Client.Ui.ITFLocalPlayer>(("get_Team", _ => 2), ("get_ClassName", _ => "Scout"), ("get_Health", _ => 125)),
               Of<client::SourceSharp.Client.Ui.IObserverSource>());

    [Fact]
    public void Holding_the_key_opens_and_letting_go_on_a_line_speaks_it()
    {
        var menus = Menus();
        menus.Open(0);
        Assert.True(_radial.Showing);

        _radial.Hover(1);
        menus.Release(0);
        Assert.Equal(["voicemenu 0 1"], _lines);
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void Letting_go_on_the_hub_says_nothing()
    {
        var menus = Menus();
        menus.Open(0);
        menus.Release(0);
        Assert.Empty(_lines);
        Assert.False(_radial.Showing);
    }

    [Fact]
    public void A_click_speaks_and_the_let_go_after_it_does_not_speak_again()
    {
        var menus = Menus();
        menus.Open(0);
        _radial.Hover(0);
        _radial.Choose(0);
        menus.Release(0);
        Assert.Equal(["voicemenu 0 0"], _lines);
    }

    [Fact]
    public void Letting_go_of_another_menu_s_key_leaves_this_one_up()
    {
        var menus = Menus();
        menus.Open(0);
        menus.Open(1);
        _radial.Hover(0);
        menus.Release(0);
        Assert.Empty(_lines);
        Assert.True(_radial.Showing);
        menus.Release(1);
        Assert.Equal(["voicemenu 1 0"], _lines);
    }

    [Fact]
    public void Not_spawned_the_key_opens_nothing_and_letting_go_says_nothing()
    {
        _inGame = false;
        var menus = Menus();
        menus.Open(0);
        Assert.False(_radial.Showing);
        menus.Release(0);
        Assert.Empty(_lines);
    }

    /// <summary>The engine adds a key code and a time to the line of any + or - bind.</summary>
    [Fact]
    public void A_held_keys_line_carries_its_code_and_time_and_still_opens_the_menu()
    {
        new client::TF2.Client.Ui.TFVoiceMenu2HoldCommand(Menus()).RunLine("89 1234");
        Assert.True(_radial.Showing);
    }

    /// <summary>A config from before the key was held, or from stock: its key keeps working, and moves.</summary>
    [Fact]
    public void Stock_s_command_opens_the_menu_and_moves_its_key_to_the_held_command()
    {
        var command = new client::TF2.Client.Ui.TFVoiceMenu2Command(Menus(), Of<client::SourceSharp.Abi.IEngineHud>(("LookupBinding", a => (string)a![0]! == "voice_menu_2" ? "x" : "")), Engine());
        command.Execute();
        Assert.True(_radial.Showing);
        Assert.Equal(["bind \"x\" \"+voice_menu_2\""], _lines);
    }

    [Fact]
    public void Stock_s_command_with_no_key_binds_nothing()
    {
        var command = new client::TF2.Client.Ui.TFVoiceMenu2Command(Menus(), Of<client::SourceSharp.Abi.IEngineHud>(("LookupBinding", _ => "")), Engine());
        command.Execute();
        Assert.Empty(_lines);
    }

    [Theory]
    [InlineData("+voice_menu_1")]
    [InlineData("-voice_menu_1")]
    [InlineData("+voice_menu_2")]
    [InlineData("-voice_menu_2")]
    [InlineData("+voice_menu_3")]
    [InlineData("-voice_menu_3")]
    [InlineData("voice_menu_1")]
    public void The_commands_are_declared(string name)
        => Assert.Contains(typeof(TFVoiceMenus).Assembly.GetTypes(), t =>
            t.GetCustomAttributes(typeof(client::SourceSharp.Abi.ConCommandAttribute), false).Cast<client::SourceSharp.Abi.ConCommandAttribute>().Any(c => c.Name == name));
}
