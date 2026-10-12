using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace SourceSharp.Tests;

/// <summary>Which side of the ABI fills a slot. See plan_baseentity.md Part 4b.</summary>
internal enum SlotSide
{
    /// <summary>Wired only under <c>#ifndef CLIENT_DLL</c> — null on the client.</summary>
    Server,

    /// <summary>Wired only under <c>#ifdef CLIENT_DLL</c> — null on the server.</summary>
    Client,

    /// <summary>Wired outside any CLIENT_DLL guard — live on both sides.</summary>
    Both,

    /// <summary>Wired once in each branch, to two implementations — live on both
    /// sides. <c>EntityGetModelName</c> after the "(no model)" bug.</summary>
    Dual,
}

/// <summary>
/// The engine of Part 4b.3's audits: derives per-slot side-ness from the one
/// place it already exists — the #ifdef position of each assignment in the
/// gamesystem's fill block — and joins it against what the shipped assemblies
/// actually reference.
///
/// <para>
/// Everything here is a pure function over text or a compiled file, so every
/// rule can be provoked in a test by handing it a source snippet or a synthetic
/// model — which is the property AbiParityTests' flat parse lacked, and why a
/// slot wired under the wrong #ifdef passed that test through two shipped bugs.
/// </para>
/// </summary>
internal static class SideAudit
{
    //-------------------------------------------------------------------------
    // 1. The fill block, parsed with an #ifdef stack.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every <c>engineApi.X = …</c> assignment in the given source, classified
    /// by the CLIENT_DLL guards enclosing it. An <c>#else</c> flips the guard;
    /// guards that are not about CLIENT_DLL contribute nothing.
    /// </summary>
    /// <para>
    /// Since plan_engineapi_vtables C6c the fill is the client's alone and the
    /// server serves its slots as shim-area methods: pass those as
    /// <paramref name="serverServed"/>. A slot the client fills and the server
    /// serves is then <see cref="SlotSide.Both"/> -- the fill no longer says
    /// whether the two sides share a body, so it cannot tell Both from Dual.
    /// </para>
    /// <summary>
    /// plan_client_vtables CC-last: neither side fills a struct, so a slot's
    /// side is which side's shim areas serve it (EngineApiCensusTests reads
    /// both sides' hand-kept headers): served by both is Both.
    /// </summary>
    public static Dictionary<string, SlotSide> SlotSidesFromServed(IReadOnlySet<string> clientServed,
                                                                  IReadOnlySet<string> serverServed)
        => clientServed.Union(serverServed).ToDictionary(
            s => s,
            s => clientServed.Contains(s) && serverServed.Contains(s) ? SlotSide.Both
               : clientServed.Contains(s) ? SlotSide.Client : SlotSide.Server,
            StringComparer.Ordinal);

    public static Dictionary<string, SlotSide> SlotSides(string gamesystemSource,
                                                         IReadOnlySet<string>? serverServed = null)
    {
        var stack = new List<string>();
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (string raw in gamesystemSource.Split('\n'))
        {
            string line = raw.TrimStart();

            if (line.StartsWith("#ifndef", StringComparison.Ordinal) && line.Contains("CLIENT_DLL"))
            {
                stack.Add("server");
                continue;
            }
            if (line.StartsWith("#ifdef", StringComparison.Ordinal) && line.Contains("CLIENT_DLL"))
            {
                stack.Add("client");
                continue;
            }
            if (line.StartsWith("#if", StringComparison.Ordinal))
            {
                stack.Add("other");
                continue;
            }
            if (line.StartsWith("#else", StringComparison.Ordinal))
            {
                if (stack.Count > 0)
                {
                    string top = stack[^1];
                    stack[^1] = top == "server" ? "client" : top == "client" ? "server" : "other";
                }
                continue;
            }
            if (line.StartsWith("#endif", StringComparison.Ordinal))
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }

            Match m = Regex.Match(line, @"^engineApi\.(\w+)\s*=");
            if (!m.Success)
                continue;

            string name = m.Groups[1].Value;
            if (name is "struct_size")
                continue;

            string side = "both";
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i] is "server" or "client")
                {
                    side = stack[i];
                    break;
                }
            }

            if (!seen.TryGetValue(name, out var sides))
                seen[name] = sides = new HashSet<string>(StringComparer.Ordinal);
            sides.Add(side);
        }

        var result = new Dictionary<string, SlotSide>(StringComparer.Ordinal);
        foreach (var (name, sides) in seen)
        {
            result[name] =
                sides.Contains("server") && sides.Contains("client") ? SlotSide.Dual
                : sides.Contains("server") ? SlotSide.Server
                : sides.Contains("client") ? SlotSide.Client
                : SlotSide.Both;
        }

        foreach (string name in serverServed ?? new HashSet<string>())
        {
            if (!result.TryGetValue(name, out var side))
                result[name] = SlotSide.Server;
            else if (side == SlotSide.Client)
                result[name] = SlotSide.Both;
        }
        return result;
    }

    /// <summary>
    /// Compares a parsed partition against the checked-in expectation. The
    /// server-only set is everything else, deliberately: appending an ordinary
    /// server slot — the common, benign case — stays green, while a slot that
    /// arrives one-sided on the client, moves sides, or gains or loses a dual
    /// implementation is a red naming the slot until the expectation is updated
    /// in the same commit.
    /// </summary>
    public static List<string> DiffPartition(
        Dictionary<string, SlotSide> actual,
        IReadOnlySet<string> expectedClient,
        IReadOnlySet<string> expectedBoth,
        IReadOnlySet<string> expectedDual)
    {
        var complaints = new List<string>();

        void Check(SlotSide side, IReadOnlySet<string> expected)
        {
            var got = actual.Where(kv => kv.Value == side).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
            foreach (string extra in got.Except(expected).OrderBy(n => n, StringComparer.Ordinal))
                complaints.Add($"slot '{extra}' is {side}-wired but not declared so");
            foreach (string missing in expected.Except(got).OrderBy(n => n, StringComparer.Ordinal))
                complaints.Add($"slot '{missing}' is declared {side} but the fill block disagrees "
                             + $"({(actual.TryGetValue(missing, out var real) ? real.ToString() : "not wired at all")})");
        }

        Check(SlotSide.Client, expectedClient);
        Check(SlotSide.Both, expectedBoth);
        Check(SlotSide.Dual, expectedDual);
        return complaints;
    }

    //-------------------------------------------------------------------------
    // 2. EngineSide.cs: which Api slots each wrapper member touches.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Members of EngineSide.cs (Engine.cs before Phase B1) mapped to the <c>Api.X</c> slots their
    /// bodies reference. Parsed by declaration line: a new
    /// <c>public/internal static</c> line opens a member, and every Api
    /// reference until the next one belongs to it.
    /// </summary>
    public static Dictionary<string, HashSet<string>> WrapperSlots(string engineSource)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string? current = null;

        foreach (string line in engineSource.Split('\n'))
        {
            // `static` optional: the wrappers are EngineSide's instance members
            // since plan_engineapi_vtables Phase B1, at the class's own indent.
            Match decl = Regex.Match(line, @"^    (?:public|internal|private)\s+(?:static\s+)?");
            if (decl.Success)
            {
                string name = MemberNameOf(line);
                if (name.Length > 0)
                {
                    current = name;
                    if (!map.ContainsKey(current))
                        map[current] = new HashSet<string>(StringComparer.Ordinal);
                }
            }

            if (current is null)
                continue;

            // The area accessors since the router's retirement (docs/todo.md):
            // EngineSide reaches a shim-area slot through ServerShims / ClientShims
            // / the ServerShimsOrNull dispatch binding (the __attN pattern variable
            // the generated typed call is bound to).
            foreach (Match m in Regex.Matches(line, @"\b(?:ServerShims|ClientShims|ServerShimsOrNull|__att\d+)\s*\.\s*(\w+)\s*\("))
                map[current].Add(m.Groups[1].Value);
        }

        return map;
    }

    //-------------------------------------------------------------------------
    // 3. EngineBinding.cs: which wrapper each interface member forwards to.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Interface members mapped to the <c>Engine.X</c> wrappers their forwarder
    /// bodies call, keyed by (interface, member). The binding classes are pure
    /// forwarders by design — "Every member is one call" — which is what makes
    /// this parse honest rather than heuristic.
    /// </summary>
    /// <summary>The binding classes <see cref="BindingMap"/> reads, by simple name.</summary>
    public static HashSet<string> BindingClasses(string bindingSource)
        => Regex.Matches(bindingSource, @"(?m)^\s*internal\s+sealed\s+class\s+(\w+)\s*:\s*IEngine\w+")
                .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    public static Dictionary<(string Iface, string Member), HashSet<string>> BindingMap(string bindingSource)
    {
        var map = new Dictionary<(string, string), HashSet<string>>();
        string? iface = null;
        (string, string)? current = null;

        foreach (string line in bindingSource.Split('\n'))
        {
            Match cls = Regex.Match(line, @"^\s*internal\s+sealed\s+class\s+\w+\s*:\s*(IEngine\w+)");
            if (cls.Success)
            {
                iface = cls.Groups[1].Value;
                current = null;
                continue;
            }

            if (iface is null)
                continue;

            if (Regex.IsMatch(line, @"^\s+public\s+"))
            {
                string name = MemberNameOf(line);
                if (name.Length > 0)
                {
                    current = (iface, name);
                    if (!map.ContainsKey(current.Value))
                        map[current.Value] = new HashSet<string>(StringComparer.Ordinal);
                }
            }

            if (current is null)
                continue;

            // `E.X` since plan_engineapi_vtables B2: a binding calls its side.
            foreach (Match m in Regex.Matches(line, @"\b(?:Engine|E)\.(\w+)"))
                map[current.Value].Add(m.Groups[1].Value);
        }

        return map;
    }

    /// <summary>
    /// The declared name on a member's first line: the identifier before its
    /// parameter list, or before <c>=&gt;</c> / <c>{</c> for a property.
    /// </summary>
    private static string MemberNameOf(string line)
    {
        int cut = line.Length;
        foreach (string stop in new[] { "(", "=>", "{" })
        {
            int at = line.IndexOf(stop, StringComparison.Ordinal);
            if (at >= 0 && at < cut)
                cut = at;
        }

        var ids = Regex.Matches(line[..cut], @"[A-Za-z_]\w*");
        return ids.Count > 0 ? ids[^1].Value : string.Empty;
    }

    //-------------------------------------------------------------------------
    // 4. A shipped assembly's references to IEngine* members.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every member of a <c>SourceSharp.Abi.IEngine*</c> interface that the
    /// given assembly references, read from its metadata without loading it.
    /// Property accessors are folded to the property name, matching the parse
    /// of the binding source.
    /// </summary>
    public static HashSet<(string Iface, string Member)> EngineMemberRefs(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        MetadataReader md = pe.GetMetadataReader();

        var refs = new HashSet<(string, string)>();
        foreach (MemberReferenceHandle handle in md.MemberReferences)
        {
            MemberReference mr = md.GetMemberReference(handle);
            if (mr.Parent.Kind != HandleKind.TypeReference)
                continue;

            TypeReference tr = md.GetTypeReference((TypeReferenceHandle)mr.Parent);
            if (md.GetString(tr.Namespace) != "SourceSharp.Abi")
                continue;

            string typeName = md.GetString(tr.Name);
            if (!typeName.StartsWith("IEngine", StringComparison.Ordinal))
                continue;

            string member = md.GetString(mr.Name);
            if (member.StartsWith("get_", StringComparison.Ordinal) || member.StartsWith("set_", StringComparison.Ordinal))
                member = member[4..];

            refs.Add((typeName, member));
        }
        return refs;
    }

    //-------------------------------------------------------------------------
    // 5. The rule.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Which of an assembly's IEngine* references lean on a slot the given
    /// side does not fill. Member-reference granularity is the point: a
    /// server-only slot answers the silent default on the client for EVERY
    /// caller, so the assembly referencing the member at all is the violation.
    /// </summary>
    public static List<string> UsageViolations(
        IEnumerable<(string Iface, string Member)> refs,
        Dictionary<(string Iface, string Member), HashSet<string>> bindingMap,
        Dictionary<string, HashSet<string>> wrapperSlots,
        Dictionary<string, SlotSide> slotSides,
        Func<SlotSide, bool> allowed)
    {
        var complaints = new List<string>();

        foreach ((string iface, string member) in refs.OrderBy(r => r.Iface + "." + r.Member, StringComparer.Ordinal))
        {
            if (!bindingMap.TryGetValue((iface, member), out var wrappers))
                continue;

            foreach (string wrapper in wrappers)
            {
                if (!wrapperSlots.TryGetValue(wrapper, out var slots))
                    continue;

                foreach (string slot in slots)
                {
                    if (slotSides.TryGetValue(slot, out SlotSide side) && !allowed(side))
                        complaints.Add($"{iface}.{member} is backed by {side}-only slot '{slot}'");
                }
            }
        }

        return complaints.Distinct().ToList();
    }

    //-------------------------------------------------------------------------
    // 6. Pairing: the two halves of an entity, as data.
    //-------------------------------------------------------------------------

    /// <summary>One classname's declared networked state on one side.</summary>
    public sealed record Half(string ClassName, IReadOnlySet<string> StateTypes);

    /// <summary>
    /// The pairing rules over the two sides' declarations. Takes models rather
    /// than assemblies so a test can provoke every rule with a synthetic pair;
    /// the extraction from the real assemblies proves itself by finding the
    /// census (see PairingAuditTests).
    /// </summary>
    public static List<string> PairingViolations(
        IReadOnlyDictionary<string, Half> server,
        IReadOnlyDictionary<string, Half> client,
        IReadOnlySet<string> declaredServerOnlyState)
    {
        var complaints = new List<string>();

        foreach (var (name, half) in client.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!server.TryGetValue(name, out var writer))
            {
                complaints.Add($"client reader '{name}' has no server writer — a half waiting for a wire that never comes");
                continue;
            }

            foreach (string state in half.StateTypes.Except(writer.StateTypes).OrderBy(s => s, StringComparer.Ordinal))
                complaints.Add($"client reader '{name}' declares state '{state}' its server half does not");
        }

        foreach (var (name, half) in server.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (half.StateTypes.Count == 0)
                continue;
            if (client.ContainsKey(name))
                continue;
            if (half.StateTypes.All(declaredServerOnlyState.Contains))
                continue;

            complaints.Add($"server class '{name}' networks state {{{string.Join(", ", half.StateTypes.OrderBy(s => s, StringComparer.Ordinal))}}} that no client reader decodes — declare it server-only or write the reader");
        }

        return complaints;
    }
}
