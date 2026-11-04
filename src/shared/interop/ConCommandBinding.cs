using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;

namespace SourceSharp.Abi;

/// <summary>
/// What a typed command's parameter may be completed with: these values, or
/// those a method of the command's own answers (<see cref="From"/>, taking no
/// arguments and returning <c>IEnumerable&lt;string&gt;</c>), so a list that
/// depends on the game comes through the command's injected services. An enum
/// or a bool needs none: its names are the list.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class CompleteAttribute(params string[] values) : Attribute
{
    public string[] Values { get; } = values;

    /// <summary>The name of the command's method that answers the list.</summary>
    public string? From { get; init; }
}

/// <summary>A string parameter that takes the rest of the line as typed, spaces and all.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class RestAttribute : Attribute;

/// <summary>A number parameter's bounds, inclusive; outside them the command does not run.</summary>
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ArgRangeAttribute(double min, double max) : Attribute
{
    public double Min { get; } = min;

    public double Max { get; } = max;
}

/// <summary>
/// A command that can suggest how its line goes on: stock's
/// <c>ConCommand::AutoCompleteSuggest</c>. Each suggestion is the whole line.
/// </summary>
public interface IConCommandSuggestions
{
    IReadOnlyList<string> Suggest(string line);
}

/// <summary>
/// A command class's <c>Execute</c> with real parameters, as the console runs it:
/// the line is split into tokens (whitespace, double quotes), each converted to its
/// parameter's type, and a line that does not fit prints why and the usage instead
/// of running. The parameters also say what to suggest.
///
/// <para>
/// A parameter is a string, an int, a float, a bool (1 0 true false on off yes no),
/// an enum (its names, any case, or a defined number) or a <see cref="Vector"/> (three
/// tokens). A default value, or a nullable type, makes it optional; a
/// <c>params string[]</c> takes the remaining tokens, a <see cref="RestAttribute"/>
/// string the remaining text.
/// </para>
/// </summary>
public sealed class ConCommandBinding : IConCommand, IConCommandSuggestions
{
    /// <summary>COMMAND_COMPLETION_MAXITEMS: what stock's console has room for.</summary>
    public const int MaxSuggestions = 64;

    private readonly string _name;
    private readonly object _target;
    private readonly MethodInfo _method;
    private readonly Parameter[] _parameters;
    private readonly Action<string> _print;

    private ConCommandBinding(string name, object target, MethodInfo method, Parameter[] parameters, Action<string> print)
    {
        _name = name;
        _target = target;
        _method = method;
        _parameters = parameters;
        _print = print;
    }

    /// <summary>The method a class's typed command runs, or null: its one public instance <c>Execute</c>.</summary>
    public static MethodInfo? MethodOf(Type type)
    {
        MethodInfo[] found = [.. type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(m => m.Name == nameof(IConCommand.Execute))];
        return found.Length == 1 && found[0].ReturnType == typeof(void) ? found[0] : null;
    }

    /// <summary>
    /// The binding of <paramref name="target"/>'s <c>Execute</c>, or null with why
    /// not: a parameter of a type this cannot convert is the declaration's mistake.
    /// </summary>
    public static ConCommandBinding? For(string name, object target, Action<string> print, out string? failure)
    {
        failure = null;
        Type type = target.GetType();
        if (MethodOf(type) is not { } method)
        {
            failure = "it has no single public void Execute";
            return null;
        }

        ParameterInfo[] infos = method.GetParameters();
        var parameters = new Parameter[infos.Length];
        for (int i = 0; i < infos.Length; i++)
        {
            parameters[i] = Parameter.Of(infos[i], i == infos.Length - 1, type, out failure)!;
            if (failure is not null)
                return null;
        }

        return new ConCommandBinding(name, target, method, parameters, print);
    }

    /// <summary>The class whose <c>Execute</c> this runs, which a listing means by the command.</summary>
    public Type CommandType => _target.GetType();

    /// <summary>What the line should look like: <c>name &lt;needed&gt; [optional]</c>.</summary>
    public string Usage
    {
        get
        {
            var line = new StringBuilder(_name);
            foreach (Parameter p in _parameters)
                line.Append(' ').Append(p.Usage);
            return line.ToString();
        }
    }

    /// <summary>The line's parameters converted, and the command run; or why not, and the usage.</summary>
    public void Execute(string args)
    {
        List<Token> tokens = Tokenize(args);
        var values = new object?[_parameters.Length];
        int next = 0;

        for (int i = 0; i < _parameters.Length; i++)
        {
            if (_parameters[i].Take(args, tokens, ref next, out values[i]) is { } problem)
            {
                Refuse(problem);
                return;
            }
        }

        if (next < tokens.Count)
        {
            Refuse($"'{tokens[next].Text}' is one argument too many");
            return;
        }

        try
        {
            _method.Invoke(_target, values);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }

    private void Refuse(string problem) => _print($"{_name}: {problem}\nusage: {Usage}\n");

    /// <summary>The line's suggestions: what its last parameter may be, each as the whole line.</summary>
    public IReadOnlyList<string> Suggest(string line)
    {
        int space = line.IndexOf(' ', StringComparison.Ordinal);
        if (space < 0)
            return [];

        string rest = line[(space + 1)..];
        List<Token> tokens = Tokenize(rest);
        bool startingNew = rest.Length == 0 || char.IsWhiteSpace(rest[^1]) || (tokens.Count > 0 && tokens[^1].Closed);
        string typing = startingNew || tokens.Count == 0 ? string.Empty : tokens[^1].Text;
        int done = startingNew ? tokens.Count : tokens.Count - 1;

        if (ParameterAt(done) is not { } parameter)
            return [];

        var head = new StringBuilder(line[..space]);
        for (int i = 0; i < done; i++)
            head.Append(' ').Append(Quoted(tokens[i].Text));

        var suggestions = new List<string>();
        foreach (string value in parameter.Values(_target))
        {
            if (!value.StartsWith(typing, StringComparison.OrdinalIgnoreCase))
                continue;
            suggestions.Add($"{head} {Quoted(value)}");
            if (suggestions.Count == MaxSuggestions)
                break;
        }
        return suggestions;
    }

    /// <summary>The parameter the token at <paramref name="index"/> goes to; none past the last, or inside a vector.</summary>
    private Parameter? ParameterAt(int index)
    {
        foreach (Parameter p in _parameters)
        {
            if (p.TakesTheRest || index < p.Width)
                return p.Width == 1 ? p : null;
            index -= p.Width;
        }
        return null;
    }

    private static string Quoted(string token)
        => token.Any(char.IsWhiteSpace) ? $"\"{token}\"" : token;

    /// <summary>One token: its text, where it starts in the line, and whether a closing quote ended it.</summary>
    private readonly record struct Token(string Text, int Start, bool Closed);

    private static List<Token> Tokenize(string line)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            int start = i;
            if (line[i] == '"')
            {
                int close = line.IndexOf('"', i + 1);
                int end = close < 0 ? line.Length : close;
                tokens.Add(new Token(line[(i + 1)..end], start, close >= 0));
                i = end + 1;
                continue;
            }

            while (i < line.Length && !char.IsWhiteSpace(line[i]))
                i++;
            tokens.Add(new Token(line[start..i], start, false));
        }
        return tokens;
    }

    /// <summary>One parameter of the method: how it takes its tokens, and what it can suggest.</summary>
    private sealed class Parameter
    {
        private enum Kind { Text, Whole, Number, Flag, Choice, Vector3, Tokens, Remainder }

        private readonly string _name;
        private readonly Type _type;
        private readonly Kind _kind;
        private readonly bool _optional;
        private readonly object? _default;
        private readonly ArgRangeAttribute? _range;
        private readonly string[]? _values;
        private readonly MethodInfo? _from;
        private readonly string? _defaultText;

        private Parameter(string name, Type type, Kind kind, bool optional, object? defaultValue,
                          ArgRangeAttribute? range, string[]? values, MethodInfo? from, string? defaultText)
        {
            _name = name;
            _type = type;
            _kind = kind;
            _optional = optional;
            _default = defaultValue;
            _range = range;
            _values = values;
            _from = from;
            _defaultText = defaultText;
        }

        public bool TakesTheRest => _kind is Kind.Tokens or Kind.Remainder;

        public int Width => _kind == Kind.Vector3 ? 3 : 1;

        public static Parameter? Of(ParameterInfo info, bool last, Type owner, out string? failure)
        {
            failure = null;
            string name = info.Name ?? "arg";
            Type declared = info.ParameterType;
            Type type = Nullable.GetUnderlyingType(declared) ?? declared;
            bool optional = info.HasDefaultValue || type != declared;
            object? defaultValue = info.HasDefaultValue ? info.DefaultValue : null;

            Kind kind;
            if (type == typeof(string))
                kind = info.GetCustomAttribute<RestAttribute>() is not null ? Kind.Remainder : Kind.Text;
            else if (type == typeof(string[]) && info.GetCustomAttribute<ParamArrayAttribute>() is not null)
            {
                kind = Kind.Tokens;
                optional = true;
                defaultValue = Array.Empty<string>();
            }
            else if (type == typeof(int))
                kind = Kind.Whole;
            else if (type == typeof(float))
                kind = Kind.Number;
            else if (type == typeof(bool))
                kind = Kind.Flag;
            else if (type.IsEnum)
                kind = Kind.Choice;
            else if (type == typeof(Vector))
                kind = Kind.Vector3;
            else
            {
                failure = $"its parameter '{name}' is a {type.Name}, which a console line cannot say";
                return null;
            }

            if (kind is Kind.Tokens or Kind.Remainder && !last)
            {
                failure = $"its parameter '{name}' takes the rest of the line, so it must be the last";
                return null;
            }

            CompleteAttribute? complete = info.GetCustomAttribute<CompleteAttribute>();
            MethodInfo? from = null;
            if (complete?.From is { } method)
            {
                from = owner.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
                if (from is null || !typeof(IEnumerable<string>).IsAssignableFrom(from.ReturnType))
                {
                    failure = $"its parameter '{name}' completes from '{method}', which is no parameterless method of it answering strings";
                    return null;
                }
            }

            string[]? values = complete?.Values is { Length: > 0 } listed ? listed
                             : from is not null ? null
                             : kind == Kind.Flag ? ["1", "0"]
                             : kind == Kind.Choice ? [.. Enum.GetNames(type).Select(n => n.ToLowerInvariant())]
                             : null;

            string? defaultText = !info.HasDefaultValue || defaultValue is null ? null
                                : defaultValue is string { Length: 0 } ? "\"\""
                                : defaultValue is bool b ? (b ? "1" : "0")
                                : defaultValue is Enum e ? e.ToString().ToLowerInvariant()
                                : Convert.ToString(defaultValue, CultureInfo.InvariantCulture);

            return new Parameter(name, type, kind, optional, defaultValue,
                                 info.GetCustomAttribute<ArgRangeAttribute>(), values, from, defaultText);
        }

        public string Usage
        {
            get
            {
                string body = _kind == Kind.Vector3 ? $"{_name}.x {_name}.y {_name}.z"
                            : _kind != Kind.Flag && _values is { Length: > 0 and <= 8 } ? string.Join('|', _values)
                            : _name;
                if (_kind == Kind.Tokens)
                    return $"[{body}...]";
                if (!_optional)
                    return $"<{body}>";
                return _defaultText is null ? $"[{body}]" : $"[{body}={_defaultText}]";
            }
        }

        /// <summary>Takes this parameter's tokens from <paramref name="next"/> on, or says what is wrong.</summary>
        public string? Take(string line, List<Token> tokens, ref int next, out object? value)
        {
            value = _default;
            if (_kind == Kind.Tokens)
            {
                value = tokens.Skip(next).Select(t => t.Text).ToArray();
                next = tokens.Count;
                return null;
            }

            if (next >= tokens.Count)
                return _optional ? null : $"'{_name}' is missing";

            if (_kind == Kind.Remainder)
            {
                value = line[tokens[next].Start..].TrimEnd();
                next = tokens.Count;
                return null;
            }

            if (_kind == Kind.Vector3)
            {
                if (next + 3 > tokens.Count)
                    return $"'{_name}' needs three numbers";
                if (!Real(tokens[next].Text, out float x) || !Real(tokens[next + 1].Text, out float y) || !Real(tokens[next + 2].Text, out float z))
                    return $"'{_name}' needs three numbers";
                value = new Vector(x, y, z);
                next += 3;
                return null;
            }

            string text = tokens[next++].Text;
            return Parse(text, out value);
        }

        private string? Parse(string text, out object? value)
        {
            value = null;
            switch (_kind)
            {
                case Kind.Text:
                    value = text;
                    return null;

                case Kind.Whole:
                    if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int whole))
                        return $"'{_name}' is a whole number, not '{text}'";
                    value = whole;
                    return InRange(whole);

                case Kind.Number:
                    if (!Real(text, out float real))
                        return $"'{_name}' is a number, not '{text}'";
                    value = real;
                    return InRange(real);

                case Kind.Flag:
                    switch (text.ToLowerInvariant())
                    {
                        case "1" or "true" or "on" or "yes": value = true; return null;
                        case "0" or "false" or "off" or "no": value = false; return null;
                        default: return $"'{_name}' is 1 or 0, not '{text}'";
                    }

                default:
                    if (Enum.TryParse(_type, text, ignoreCase: true, out object? choice) && Enum.IsDefined(_type, choice!))
                    {
                        value = choice;
                        return null;
                    }
                    return $"'{_name}' is {string.Join('|', _values!)}, not '{text}'";
            }
        }

        private static bool Real(string text, out float value)
            => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private string? InRange(double number)
            => _range is null || (number >= _range.Min && number <= _range.Max) ? null
             : $"'{_name}' is from {_range.Min} to {_range.Max}, not {number}";

        /// <summary>What this parameter can be: the declared list, or what the command answers now.</summary>
        public IEnumerable<string> Values(object target)
            => _from is not null ? (IEnumerable<string>)_from.Invoke(target, null)! : _values ?? [];
    }
}
