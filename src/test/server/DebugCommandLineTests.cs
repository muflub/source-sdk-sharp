using SourceSharp.Abi;
using Debug.Api;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The dev API's copy of the command-line reader (G10) answers exactly as the
/// game's LocalCommandLine does, so moving the dev API onto it changes no flag.
/// </summary>
public class DebugCommandLineTests
{
    public static TheoryData<string[], string> Cases => new()
    {
        { [], "-debugapi" },
        { ["hl2_linux64", "-debugapi"], "-debugapi" },
        { ["hl2_linux64", "-DEBUGAPI"], "-debugapi" },
        { ["hl2_linux64", "-debugapiport", "27999"], "-debugapiport" },
        { ["hl2_linux64", "-debugapiport"], "-debugapiport" },
        { ["hl2_linux64", "-debugapiport", "-debugapi"], "-debugapiport" },
        { ["hl2_linux64", "-debugapiport", "+map"], "-debugapiport" },
        { ["-debugapiport", "1", "-debugapiport", "2"], "-debugapiport" },
        { ["hl2_linux64", "-debugapi"], "" },
        { ["hl2_linux64", "-debugapiportx", "5"], "-debugapiport" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Has_answers_as_the_game_does(string[] args, string param)
        => Assert.Equal(LocalCommandLine.Has(args, param), DebugCommandLine.Has(args, param));

    [Theory]
    [MemberData(nameof(Cases))]
    public void Value_answers_as_the_game_does(string[] args, string param)
        => Assert.Equal(LocalCommandLine.Value(args, param, "fb"), DebugCommandLine.Value(args, param, "fb"));

    [Fact]
    public void The_cases_reach_both_answers()
    {
        // The stimulus reaches the subject: the cases include a present flag,
        // an absent one, a value and a refused value.
        Assert.True(DebugCommandLine.Has(["-debugapi"], "-debugapi"));
        Assert.False(DebugCommandLine.Has(["-debugapi"], "-debugui"));
        Assert.Equal("27999", DebugCommandLine.Value(["-debugapiport", "27999"], "-debugapiport", "fb"));
        Assert.Equal("fb", DebugCommandLine.Value(["-debugapiport", "+map"], "-debugapiport", "fb"));
    }
}
