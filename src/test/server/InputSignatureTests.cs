using System.Reflection;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Every [ScriptedEvent] in the server binds. The registry skips an input
/// whose signature it cannot bind with only a warning in the log, so a mapper's
/// input to it does nothing and nothing fails. Found 2026-10-28: three of
/// CBaseAnimating's integer inputs (skin, SetBodyGroup, IgniteNumHitboxFires)
/// were written taking an int and were never inputs at all.
/// </summary>
public class InputSignatureTests
{
    [Fact]
    public void Every_scripted_event_takes_nothing_or_one_float_string_or_InputData()
    {
        var bad = new List<string>();
        int checkedInputs = 0;

        foreach (Type type in typeof(ScriptedEntity).Assembly.GetTypes())
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttributes<ScriptedEventAttribute>().Any() is false)
                    continue;

                checkedInputs++;
                var parameters = method.GetParameters();
                bool bindable = parameters.Length == 0
                             || (parameters.Length == 1 && (parameters[0].ParameterType == typeof(float)
                                                         || parameters[0].ParameterType == typeof(string)
                                                         || parameters[0].ParameterType == typeof(InputData)));
                if (!bindable)
                    bad.Add($"{type.Name}.{method.Name}({string.Join(", ", parameters.Select(p => p.ParameterType.Name))})");
            }
        }

        Assert.True(checkedInputs > 200, $"only {checkedInputs} input(s) were checked");
        Assert.Empty(bad);
    }
}
