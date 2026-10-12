using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// docs/player-usermessages.tsv against the managed server: a row ruled
/// "ported" names classes whose code builds that message's payload and sends
/// it. A port that is undone, or a row that claims one that never was, is red.
/// </summary>
public class ServerPlayerUserMessagesTests
{
    private static IEnumerable<string[]> Rows()
        => File.ReadAllLines(RepoLayout.Repo("docs", "player-usermessages.tsv"))
               .Where(l => l.Length > 0 && l[0] != '#').Skip(1).Select(l => l.Split('\t'));

    /// <summary>The payload struct a message key is declared with ([UserMessage(key)] on IUserMessage&lt;T&gt;).</summary>
    private static string PayloadOf(string key)
    {
        var handler = typeof(DamageMessage).Assembly.GetTypes()
            .Single(t => t.GetCustomAttribute<UserMessageAttribute>()?.Name == key);
        return handler.GetInterfaces()
            .Single(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUserMessage<>))
            .GetGenericArguments()[0].Name;
    }

    /// <summary>The code (comments removed) of the server file declaring class <paramref name="name"/>.</summary>
    private static string CodeOf(string name)
    {
        var files = Directory.EnumerateFiles(RepoLayout.Repo("src", "server", "managed"), "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\bclass\s+" + name + @"\b")).ToArray();
        Assert.True(files.Length == 1, $"{files.Length} files declare class {name}");
        return Regex.Replace(File.ReadAllText(files[0]), @"//[^\n]*|///[^\n]*", "");
    }

    [Fact]
    public void Every_ported_message_is_built_and_sent_by_the_classes_its_row_names()
    {
        var ported = Rows().Where(r => r[4] == "ported").ToArray();
        Assert.True(ported.Length >= 6, "only " + ported.Length + " ported rows read");

        foreach (var row in ported)
        {
            string payload = PayloadOf(row[0]);
            // The class names: capitalised words not after a dot (a member).
            var classes = Regex.Matches(row[3], @"(?<![.\w])(?<c>[A-Z]\w+)")
                .Select(m => m.Groups["c"].Value).Distinct().ToArray();
            Assert.True(classes.Length > 0, row[0] + " names no class");

            // One of them builds the payload and one of them sends it: a report
            // (PlayerDamageReport) or a helper (PickupItem.ReportPickup) may
            // build what another class sends.
            bool built = classes.Any(c => Regex.IsMatch(CodeOf(c), @"new\s+" + payload + @"\s*\{"));
            Assert.True(built, $"{row[0]}: none of [{string.Join(", ", classes)}] builds a {payload}");
            bool sent = classes.Any(c => Regex.IsMatch(CodeOf(c), @"\.Send\("));
            Assert.True(sent, $"{row[0]}: none of [{string.Join(", ", classes)}] sends anything");
        }
    }
}
