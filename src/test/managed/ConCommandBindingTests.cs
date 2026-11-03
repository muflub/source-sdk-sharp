using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>A command's <c>Execute</c> with real parameters: what the line converts to, what is refused, what is suggested.</summary>
public class ConCommandBindingTests
{
    private enum Skill { Easy, Medium, Hard }

    private sealed class Sink
    {
        public readonly List<string> Printed = [];

        public ConCommandBinding Bind(object target)
        {
            ConCommandBinding? binding = ConCommandBinding.For("cmd", target, Printed.Add, out string? failure);
            Assert.True(binding is not null, failure);
            return binding!;
        }
    }

    private sealed class Typed
    {
        public object?[]? Got;

        public void Execute(string name, int count = 3, float speed = 1.5f, bool loud = false, Skill skill = Skill.Medium)
            => Got = [name, count, speed, loud, skill];
    }

    [Fact]
    public void Each_token_converts_to_its_parameters_type()
    {
        var typed = new Typed();
        new Sink().Bind(typed).Execute("alyx 7 2.5 on HARD");
        Assert.Equal(["alyx", 7, 2.5f, true, Skill.Hard], typed.Got!);
    }

    [Fact]
    public void An_absent_optional_takes_its_default()
    {
        var typed = new Typed();
        new Sink().Bind(typed).Execute("alyx");
        Assert.Equal(["alyx", 3, 1.5f, false, Skill.Medium], typed.Got!);
    }

    [Fact]
    public void Double_quotes_hold_a_token_together()
    {
        var typed = new Typed();
        new Sink().Bind(typed).Execute("\"dr kleiner\" 2");
        Assert.Equal("dr kleiner", typed.Got![0]);
        Assert.Equal(2, typed.Got![1]);
    }

    [Theory]
    [InlineData("", "'name' is missing")]
    [InlineData("alyx lots", "'count' is a whole number, not 'lots'")]
    [InlineData("alyx 1 fast", "'speed' is a number, not 'fast'")]
    [InlineData("alyx 1 1 maybe", "'loud' is 1 or 0, not 'maybe'")]
    [InlineData("alyx 1 1 1 godlike", "'skill' is easy|medium|hard, not 'godlike'")]
    [InlineData("alyx 1 1 1 easy extra", "'extra' is one argument too many")]
    public void A_line_that_does_not_fit_says_why_and_the_usage_and_does_not_run(string line, string why)
    {
        var typed = new Typed();
        var sink = new Sink();
        ConCommandBinding binding = sink.Bind(typed);

        binding.Execute(line);

        Assert.Null(typed.Got);
        string printed = Assert.Single(sink.Printed);
        Assert.Contains(why, printed);
        Assert.Contains(binding.Usage, printed);
    }

    [Fact]
    public void An_enum_takes_a_defined_number_and_refuses_another()
    {
        var typed = new Typed();
        ConCommandBinding binding = new Sink().Bind(typed);

        binding.Execute("alyx 1 1 1 2");
        Assert.Equal(Skill.Hard, typed.Got![4]);

        typed.Got = null;
        binding.Execute("alyx 1 1 1 9");
        Assert.Null(typed.Got);
    }

    [Fact]
    public void The_usage_names_needed_and_optional_arguments_and_their_defaults()
        => Assert.Equal("cmd <name> [count=3] [speed=1.5] [loud=0] [easy|medium|hard=medium]", new Sink().Bind(new Typed()).Usage);

    private sealed class Ranged
    {
        public int? Got;

        public void Execute([ArgRange(1, 10)] int count) => Got = count;
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("1", 1)]
    [InlineData("11", null)]
    [InlineData("0", null)]
    public void A_range_refuses_what_is_outside_it(string line, int? got)
    {
        var ranged = new Ranged();
        new Sink().Bind(ranged).Execute(line);
        Assert.Equal(got, ranged.Got);
    }

    private sealed class Tail
    {
        public string? First;
        public string? Said;

        public void Execute(string first, [Rest] string said = "")
        {
            First = first;
            Said = said;
        }
    }

    [Fact]
    public void A_rest_parameter_takes_the_text_as_typed()
    {
        var tail = new Tail();
        new Sink().Bind(tail).Execute("all  hello   \"big\" world ");
        Assert.Equal("all", tail.First);
        Assert.Equal("hello   \"big\" world", tail.Said);
    }

    private sealed class Many
    {
        public string[]? Got;

        public void Execute(params string[] names) => Got = names;
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a \"b c\" d", new[] { "a", "b c", "d" })]
    public void A_params_array_takes_the_remaining_tokens(string line, string[] names)
    {
        var many = new Many();
        new Sink().Bind(many).Execute(line);
        Assert.Equal(names, many.Got);
    }

    private sealed class Placed
    {
        public Vector Got;

        public void Execute(Vector at) => Got = at;
    }

    [Fact]
    public void A_vector_takes_three_numbers()
    {
        var placed = new Placed();
        var sink = new Sink();
        ConCommandBinding binding = sink.Bind(placed);
        binding.Execute("1 2.5 -3");
        Assert.Equal(new Vector(1f, 2.5f, -3f), placed.Got);

        binding.Execute("1 2");
        Assert.Contains("'at' needs three numbers", Assert.Single(sink.Printed));
    }

    private sealed class Maybe
    {
        public int? Got = -1;

        public void Execute(int? count) => Got = count;
    }

    [Fact]
    public void A_nullable_is_optional_and_null_when_absent()
    {
        var maybe = new Maybe();
        ConCommandBinding binding = new Sink().Bind(maybe);
        binding.Execute("");
        Assert.Null(maybe.Got);
        binding.Execute("4");
        Assert.Equal(4, maybe.Got);
    }

    private sealed class Throws
    {
        public void Execute() => throw new InvalidOperationException("the command's own");
    }

    [Fact]
    public void What_the_command_throws_comes_out_as_itself()
        => Assert.Throws<InvalidOperationException>(() => new Sink().Bind(new Throws()).Execute(""));

    private sealed class Unsayable
    {
        public void Execute(DateTime when) { }
    }

    private sealed class RestNotLast
    {
        public void Execute([Rest] string text, int count) { }
    }

    private sealed class BadSource
    {
        public void Execute([Complete(From = "Missing")] string name) { }
    }

    [Theory]
    [InlineData(typeof(Unsayable), "'when' is a DateTime")]
    [InlineData(typeof(RestNotLast), "must be the last")]
    [InlineData(typeof(BadSource), "'Missing'")]
    public void A_declaration_that_cannot_work_is_refused_and_says_so(Type type, string why)
    {
        Assert.Null(ConCommandBinding.For("cmd", Activator.CreateInstance(type)!, _ => { }, out string? failure));
        Assert.Contains(why, failure);
    }

    private sealed class Suggesting
    {
        public void Execute(Skill skill, bool loud = false, [Complete("alpha", "beta", "big one")] string tag = "",
                            [Complete(From = nameof(Maps))] string map = "", int count = 0)
        {
        }

        private IEnumerable<string> Maps() => ["dm_lockdown", "dm_overwatch", "ss_sandbox"];
    }

    private static IReadOnlyList<string> Suggest(string line) => new Sink().Bind(new Suggesting()).Suggest(line);

    [Fact]
    public void An_enum_suggests_its_names_as_whole_lines()
        => Assert.Equal(["cmd easy", "cmd medium", "cmd hard"], Suggest("cmd "));

    [Fact]
    public void What_is_typed_narrows_it_in_any_case()
        => Assert.Equal(["cmd hard"], Suggest("cmd HA"));

    [Fact]
    public void A_bool_suggests_its_two_values()
        => Assert.Equal(["cmd easy 1", "cmd easy 0"], Suggest("cmd easy "));

    [Fact]
    public void Listed_values_are_suggested_and_one_with_a_space_quoted()
        => Assert.Equal(["cmd easy 1 alpha", "cmd easy 1 beta", "cmd easy 1 \"big one\""], Suggest("cmd easy 1 "));

    [Fact]
    public void A_method_of_the_command_answers_its_list()
        => Assert.Equal(["cmd easy 1 alpha dm_lockdown", "cmd easy 1 alpha dm_overwatch"], Suggest("cmd easy 1 alpha dm"));

    [Theory]
    [InlineData("cmd easy 1 alpha ss_sandbox ")]   // a number: nothing to list
    [InlineData("cmd easy 1 alpha ss_sandbox 3 ")] // past the last parameter
    [InlineData("cmd zzz")]                        // nothing starts so
    [InlineData("cmd")]                            // still naming the command
    public void Where_there_is_nothing_to_list_nothing_is_suggested(string line) => Assert.Empty(Suggest(line));

    [Fact]
    public void A_closed_quote_ends_its_token_and_earlier_tokens_stay_quoted()
        => Assert.Equal(["cmd easy 1 \"big one\" dm_lockdown", "cmd easy 1 \"big one\" dm_overwatch", "cmd easy 1 \"big one\" ss_sandbox"],
                        Suggest("cmd easy 1 \"big one\""));

    private sealed class Lots
    {
        public void Execute([Complete(From = nameof(All))] string name) { }

        private IEnumerable<string> All() => Enumerable.Range(0, 200).Select(i => $"n{i}");
    }

    [Fact]
    public void Suggestions_stop_at_what_the_console_has_room_for()
        => Assert.Equal(ConCommandBinding.MaxSuggestions, new Sink().Bind(new Lots()).Suggest("cmd ").Count);

    [Fact]
    public void A_vector_is_a_position_in_the_line_with_nothing_to_list()
    {
        var binding = new Sink().Bind(new VectorThenChoice());
        Assert.Empty(binding.Suggest("cmd 1 2 "));
        Assert.Equal(["cmd 1 2 3 easy", "cmd 1 2 3 medium", "cmd 1 2 3 hard"], binding.Suggest("cmd 1 2 3 "));
    }

    private sealed class VectorThenChoice
    {
        public void Execute(Vector at, Skill skill = Skill.Easy) { }
    }
}
