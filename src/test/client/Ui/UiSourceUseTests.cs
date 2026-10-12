using System.Reflection;
using System.Reflection.Emit;

using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Screens;
using SourceSharp.Client.Ui.Toolkit;

using Xunit;

namespace SourceSharp.Tests.Ui;

/// <summary>
/// One interface per concern, and a panel takes only what it reads (user
/// ruling 2026-10-05; docs/ui/authoring.md §1). Checked from the IL, with no
/// analyser: every constructor parameter of a screen or panel that is a
/// Client.Interface data source is called on somewhere in that type (its
/// lambdas and local functions included), and every member of every source is
/// read by some screen or panel, so no source carries data nothing shows.
/// </summary>
public class UiSourceUseTests
{
    private static readonly Assembly Contract = typeof(IScoreboardSource).Assembly;

    /// <summary>The data interfaces screens read (not the layer/backend contracts).</summary>
    internal static bool IsSource(Type t)
        => t.IsInterface && t.Assembly == Contract && t.Namespace == "SourceSharp.Client.Ui"
           && t != typeof(IUiScreen) && t != typeof(IUiLayer) && t != typeof(IUiBackend) && t != typeof(IUiFontMetrics)
           && t != typeof(IUiIconAtlas);

    private static IEnumerable<Type> UiTypes()
        => typeof(UiScreen).Assembly.GetTypes().Concat(typeof(ScreensAssembly).Assembly.GetTypes())
            .Where(t => !t.IsAbstract && (typeof(UiScreen).IsAssignableFrom(t) || IsPanel(t)));

    private static bool IsPanel(Type t)
    {
        for (var b = t.BaseType; b is not null; b = b.BaseType)
            if (b == typeof(UiPanel) || (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(UiPanel<>)))
                return true;
        return false;
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);

    /// <summary>Every method a type's IL calls, its nested types' (lambdas, local functions) included.</summary>
    internal static HashSet<MethodBase> Calls(Type type)
    {
        var found = new HashSet<MethodBase>();
        foreach (var t in new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)))
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMethods(all).Cast<MethodBase>().Concat(t.GetConstructors(all)))
            {
                var il = m.GetMethodBody()?.GetILAsByteArray();
                if (il is null) continue;
                for (int i = 0; i < il.Length;)
                {
                    short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
                    i += il[i] == 0xFE ? 2 : 1;
                    var op = OpCodesByValue[value];
                    if (op.OperandType == OperandType.InlineMethod)
                    {
                        int token = BitConverter.ToInt32(il, i);
                        try
                        {
                            var called = m.Module.ResolveMethod(token, t.IsGenericType ? t.GetGenericArguments() : null,
                                                                m.IsGenericMethod ? m.GetGenericArguments() : null);
                            if (called is not null) found.Add(called);
                        }
                        catch (ArgumentException) { }
                    }
                    i += op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                        _ => 4,
                    };
                }
            }
        }
        return found;
    }

    /// <summary>
    /// The source parameters of <paramref name="type"/>'s constructors that it
    /// never calls on, nor hands to a constructor that takes one (a form gives
    /// its stores to UiForm, whose bindings read them). <paramref name="isSource"/>
    /// names the interface family; the descent twin passes the IDescent* one
    /// (DescentUiSourceUseTests).
    /// </summary>
    internal static List<string> UnreadSources(Type type, Func<Type, bool>? isSource = null)
    {
        isSource ??= IsSource;
        var calls = Calls(type);
        return type.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Where(p => isSource(p.ParameterType))
            .Where(p => !calls.Any(m => m.DeclaringType == p.ParameterType
                                        || (m is ConstructorInfo c && c.GetParameters().Any(q => q.ParameterType == p.ParameterType))))
            .Select(p => $"{type.Name}({p.ParameterType.Name} {p.Name})")
            .Distinct().ToList();
    }

    [Fact]
    public void Every_source_a_screen_or_panel_takes_is_read_by_it()
    {
        var types = UiTypes().ToList();
        Assert.Contains(typeof(AmmoPanel), types);
        Assert.Contains(typeof(TeamTable), types);
        int sourced = types.Count(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => IsSource(p.ParameterType)));
        Assert.True(sourced >= 10, $"only {sourced} types take a source: the scan missed them");

        var unread = types.SelectMany(t => UnreadSources(t)).ToList();
        Assert.True(unread.Count == 0, "taken but never read:\n" + string.Join("\n", unread));
    }

    [Fact]
    public void Every_member_of_every_source_is_read_somewhere()
    {
        // UiWindows reads the window store for every windowed screen, and a
        // form's bindings its convar and key stores.
        var calls = UiTypes().Append(typeof(UiWindows)).Append(typeof(UiFormBindings<>)).SelectMany(Calls).ToHashSet();
        var sources = Contract.GetTypes().Where(IsSource).ToList();
        Assert.Contains(typeof(IPlayerAmmo), sources);
        Assert.DoesNotContain(sources, t => t.Name == "ILocalPlayerHud");

        var unread = sources.SelectMany(s => s.GetMethods().Where(m => !calls.Contains(m)).Select(m => $"{s.Name}.{m.Name}")).ToList();
        Assert.True(unread.Count == 0, "no screen or panel reads:\n" + string.Join("\n", unread));
    }

    // The planted half: a panel that takes a source it never reads is reported;
    // one that reads it (in a lambda, even) is not.
    private sealed class Greedy(IPlayerHealth health, IPlayerArmour armour) : UiPanel
    {
        public override void Describe(ref UiFrame ui) => ui.Number(health.Health);
        public IPlayerArmour Unused => armour;   // held, never called on
    }

    private sealed class Lean(IPlayerHealth health) : UiPanel
    {
        private readonly Func<int> _read = () => health.MaxHealth;
        public override void Describe(ref UiFrame ui) => ui.Number(_read());
    }

    [Fact]
    public void A_planted_unread_source_is_reported()
    {
        Assert.Equal(["Greedy(IPlayerArmour armour)"], UnreadSources(typeof(Greedy)));
        Assert.Empty(UnreadSources(typeof(Lean)));
    }
}
