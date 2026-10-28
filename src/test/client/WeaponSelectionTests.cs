using System.Reflection;
using System.Text.RegularExpressions;

using Xunit;

using SourceSharp.Abi;
using SourceSharp.Client;

namespace SourceSharp.Tests;

/// <summary>
/// The weapon-selection binds, managed since lane M1-B (they were shell_hud.cpp's
/// nine CON_COMMANDs): each stock bind name sends the server the line the
/// managed Arsenal answers.
/// </summary>
public class WeaponSelectionTests
{
    private static readonly string[] Binds =
        ["invnext", "invprev", "lastinv", "slot1", "slot2", "slot3", "slot4", "slot5", "slot6"];

    private sealed class Sent : IWeaponSelectionCommand
    {
        public List<string> Lines { get; } = [];
        public void SendSelection(string line) => Lines.Add(line);
    }

    [Theory]
    [InlineData("invnext", "invnext")]
    [InlineData("invprev", "invprev")]
    [InlineData("lastinv", "lastinv")]
    public void A_cycling_bind_sends_its_own_name(string bind, string line)
        => Assert.Equal(line, WeaponSelection.ServerLine(bind));

    [Theory]
    [InlineData("slot1", "ss_slot 0")]
    [InlineData("slot2", "ss_slot 1")]
    [InlineData("slot6", "ss_slot 5")]
    public void A_slot_bind_sends_its_zero_based_slot(string bind, string line)
        => Assert.Equal(line, WeaponSelection.ServerLine(bind));

    [Theory]
    [InlineData("slot0")]
    [InlineData("slot7")]
    [InlineData("invfoo")]
    public void A_name_that_is_not_a_selection_bind_sends_nothing(string bind)
        => Assert.Null(WeaponSelection.ServerLine(bind));

    [Fact]
    public void No_line_carries_a_trailing_newline()
        => Assert.All(Binds, b => Assert.DoesNotContain('\n', WeaponSelection.ServerLine(b)!));

    [Fact]
    public void Executing_a_bind_sends_its_line_once()
    {
        var sent = new Sent();

        new Slot3Command(sent).Execute("");

        Assert.Equal(["ss_slot 2"], sent.Lines);
    }

    [Fact]
    public void Each_bind_name_is_declared_once_as_a_managed_command()
    {
        var declared = typeof(InvNextCommand).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<ConCommandAttribute>()?.Name)
            .Where(n => n is not null && Binds.Contains(n))
            .ToList();

        Assert.Equal(Binds.OrderBy(b => b), declared.OrderBy(b => b));
    }

    [Fact]
    public void Each_declared_bind_sends_the_line_for_its_own_name()
    {
        foreach (var type in typeof(InvNextCommand).Assembly.GetTypes())
        {
            string? name = type.GetCustomAttribute<ConCommandAttribute>()?.Name;
            if (name is null || !Binds.Contains(name))
                continue;
            var sent = new Sent();
            ((IConCommand)Activator.CreateInstance(type, sent)!).Execute("");
            Assert.Equal([WeaponSelection.ServerLine(name)!], sent.Lines);
        }
    }

    [Fact]
    public void The_native_hud_declares_none_of_them()
    {
        string path = Path.Combine(RepoLayout.SdkTree, "src", "sourcesharp", "game", "client", "shell_hud.cpp");
        Assert.True(File.Exists(path), $"{path} was not found; a scan that read nothing would pass");
        string code = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
        Assert.Contains("DECLARE_HUDELEMENT( CSourceSharpHud );", code);
        Assert.DoesNotMatch(new Regex(@"CON_COMMAND\(\s*(invnext|invprev|lastinv|slot\d)\b"), code);
    }
}
