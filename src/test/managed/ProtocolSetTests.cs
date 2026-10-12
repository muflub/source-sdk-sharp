extern alias client;
using System.Reflection;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE TWO SIDES DECLARE ONE PROTOCOL. Before plan_side_isolation P7 every
/// wire declaration sat in SourceSharp.Shared and a registry warned
/// (<c>WarnIfNotShared</c>) about one found anywhere else, because a
/// declaration only one side compiles shifts or breaks the wire. The shared
/// source now compiles into each side, so "anywhere else" is the side's own
/// assembly and the warning could no longer see it; this is the check instead,
/// at build time and over the built side assemblies: every declaration that IS
/// the protocol is in both, under one full name, and in neither alone.
/// </summary>
public class ProtocolSetTests
{
    /// <summary>The attributes whose types are the wire protocol.</summary>
    private static readonly string[] ProtocolAttributes =
    [
        "SourceSharp.Abi.UserMessageAttribute",
        "SourceSharp.Abi.StringTableAttribute",
        "SourceSharp.Abi.TempEntityAttribute",
        "SourceSharp.Abi.StandaloneNetStateAttribute",
    ];

    private const string EntityField = "SourceSharp.Abi.EntityFieldAttribute";

    private static Assembly Server => typeof(SourceSharp.Server.ServerModule).Assembly;

    private static Assembly Client => typeof(client::SourceSharp.Client.ClientModule).Assembly;

    /// <summary>
    /// Every protocol declaration in <paramref name="assembly"/> as
    /// "attribute type", read by NAME: each side has its own copy of the
    /// shared attribute types, and a name is the one thing they agree on.
    /// </summary>
    internal static SortedSet<string> Declarations(Assembly assembly)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (Type type in assembly.GetTypes())
        {
            foreach (CustomAttributeData a in type.CustomAttributes)
            {
                string name = a.AttributeType.FullName ?? string.Empty;
                if (ProtocolAttributes.Contains(name))
                    found.Add($"{name[(name.LastIndexOf('.') + 1)..]} {type.FullName}");
            }

            const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public
                                        | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (type.GetMembers(Declared).Any(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == EntityField)))
                found.Add($"EntityLayout {type.FullName}");
        }

        return found;
    }

    /// <summary>What one side declares and the other does not, named by side.</summary>
    internal static List<string> Differences(IEnumerable<string> server, IEnumerable<string> client)
    {
        var s = server.ToHashSet(StringComparer.Ordinal);
        var c = client.ToHashSet(StringComparer.Ordinal);

        return s.Except(c).Select(d => "server only: " + d)
                .Concat(c.Except(s).Select(d => "client only: " + d))
                .OrderBy(d => d, StringComparer.Ordinal)
                .ToList();
    }

    [Fact]
    public void TheServerAndTheClientDeclareTheSameProtocol()
    {
        List<string> differences = Differences(Declarations(Server), Declarations(Client));

        Assert.True(differences.Count == 0,
            "a wire declaration only one side compiles; it belongs in src/shared/managed:\n  "
            + string.Join("\n  ", differences));
    }

    /// <summary>
    /// The stimulus reaches the subject: each side's scan finds declarations of
    /// every kind the game has, so an empty comparison cannot pass for an equal one.
    /// </summary>
    [Fact]
    public void EachSideDeclaresEveryKindOfMessage()
    {
        foreach (Assembly side in new[] { Server, Client })
        {
            SortedSet<string> declared = Declarations(side);
            foreach (string kind in new[] { "UserMessageAttribute", "StringTableAttribute",
                                            "TempEntityAttribute", "StandaloneNetStateAttribute" })
                Assert.True(declared.Count(d => d.StartsWith(kind + " ", StringComparison.Ordinal)) > 0,
                            $"{side.GetName().Name} declares no {kind}: the scan read nothing");
        }
    }

    /// <summary>
    /// The fail-first arm: a declaration one side has and the other lacks is
    /// named, on either side, over the real sets.
    /// </summary>
    [Fact]
    public void ADeclarationOnlyOneSideHasIsNamed()
    {
        SortedSet<string> server = Declarations(Server);
        string dropped = server.First(d => d.StartsWith("UserMessageAttribute ", StringComparison.Ordinal));
        var client = Declarations(Client);
        client.Remove(dropped);

        Assert.Equal(["server only: " + dropped], Differences(server, client));

        client.Add(dropped);
        client.Add("TempEntityAttribute Planted.ClientOnly");
        Assert.Equal(["client only: TempEntityAttribute Planted.ClientOnly"], Differences(server, client));
    }
}
