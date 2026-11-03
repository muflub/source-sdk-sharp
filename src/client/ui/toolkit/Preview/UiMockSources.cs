using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SourceSharp.Client.Ui.Toolkit;

/// <summary>
/// One value a mocked screen read in its last render: its key (the interface,
/// the member and its arguments, then the field: <c>IScoreboardSource.Player(2).Name</c>),
/// the interface it came from, its type (<c>bool</c>, <c>int</c>, <c>float</c>,
/// <c>string</c>, <c>enum</c>), the value it was answered, whether that was an
/// edit, and an enum's names.
/// </summary>
public sealed record UiMockLeaf(string Key, string Source, string Type, object? Value, bool Changed,
                                IReadOnlyList<string>? Options = null);

/// <summary>
/// The data a live mock-up reads, with edits (the dev web UI's Mockups tab):
/// every data interface a screen takes is answered by a proxy that asks the
/// sample's fake (or, for a screen without one, makes a plausible value up),
/// lays the edits keyed by <see cref="UiMockLeaf.Key"/> over the answer, and
/// records every leaf value it answered, so the editor lists exactly what the
/// screen read. A call the fake cannot answer (an index past its list, after a
/// count was raised) is answered as the member's last good answer. One
/// instance per render; not a per-frame path of the game.
/// </summary>
public sealed class UiMockSources
{
    private const int MaxDepth = 4, MaxListCount = 256;

    private readonly IReadOnlyDictionary<string, JsonElement> _edits;
    private readonly bool _fakes;
    private readonly Dictionary<string, UiMockLeaf> _leaves = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();
    private readonly Dictionary<string, object?[]> _lastGood = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _children = new(StringComparer.Ordinal);

    /// <param name="edits">The values to answer instead, by leaf key.</param>
    /// <param name="fakes">False answers made-up values even where a fake is registered (the "auto" data).</param>
    public UiMockSources(IReadOnlyDictionary<string, JsonElement> edits, bool fakes = true)
    {
        _edits = edits;
        _fakes = fakes;
    }

    /// <summary>The leaves answered so far, in the order first read.</summary>
    public IReadOnlyList<UiMockLeaf> Leaves => _order.Select(k => _leaves[k]).ToList();

    /// <summary>The <see cref="UiSampleServices.Source"/> hook: a proxy for <paramref name="iface"/> over the registration (a made-up value for a value type).</summary>
    public object Wrap(Type iface, object? registered)
        => !iface.IsInterface ? Make(iface, iface.Name, 0) ?? throw new InvalidOperationException($"{iface.Name} cannot be made up")
         : Proxy(iface, _fakes ? registered : null, iface.Name + ".", iface.Name) ?? registered
           ?? throw new InvalidOperationException($"{iface.Name} cannot be mocked");

    /// <summary>
    /// A value with the edits under <paramref name="key"/> laid over it, every
    /// leaf of it recorded under <paramref name="source"/>: a panel's props go
    /// through here.
    /// </summary>
    public object? Value(string source, string key, Type type, object? value) => Apply(source, key, type, value, 0);

    /// <summary>A plausible value of <paramref name="type"/> for a member named <paramref name="name"/>.</summary>
    public object? Plausible(Type type, string name) => Make(type, name, 0);

    private object? Proxy(Type iface, object? inner, string prefix, string source)
    {
        if (!Proxiable(iface))
            return null;
        try
        {
            var proxy = DispatchProxy.Create(iface, typeof(UiMockProxy));
            var mock = (UiMockProxy)proxy;
            mock.Owner = this;
            mock.Inner = inner;
            mock.Prefix = prefix;
            mock.Source = source;
            return proxy;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or TypeLoadException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a proxy can answer <paramref name="iface"/>: a method that takes
    /// or answers a ref struct (a panel's <c>ref UiFrame</c>) is one
    /// <see cref="DispatchProxy"/> cannot box into its argument array.
    /// </summary>
    public static bool Proxiable(Type iface)
        => iface.GetMethods().Concat(iface.GetInterfaces().SelectMany(i => i.GetMethods()))
                .All(m => !Unboxable(m.ReturnType) && m.GetParameters().All(p => !Unboxable(p.ParameterType)));

    private static bool Unboxable(Type t) => (t.IsByRef ? t.GetElementType()! : t).IsByRefLike;

    internal object? Call(UiMockProxy proxy, MethodInfo method, object?[] args)
    {
        var ps = method.GetParameters();
        bool getter = method.IsSpecialName && method.Name.StartsWith("get_", StringComparison.Ordinal);
        bool readable = method.ReturnType != typeof(void) || ps.Any(p => p.IsOut);
        if ((method.IsSpecialName && !getter) || !readable || ps.Any(p => p.ParameterType.IsByRef && !p.IsOut))
            return Forward(proxy, method, args);

        string member = getter ? method.Name[4..] : method.Name;
        var shown = ps.Where(p => !p.IsOut).Select(p => Format(args[p.Position])).ToArray();
        string key = proxy.Prefix + member + (getter && shown.Length == 0 ? "" : $"({string.Join(",", shown)})");
        string slot = proxy.Prefix + member;

        object? result;
        bool answered = proxy.Inner is not null;
        if (answered)
        {
            try
            {
                result = method.Invoke(proxy.Inner, args);
                _lastGood[slot] = [result, .. args];
            }
            catch (TargetInvocationException)
            {
                answered = false;
                result = Fallback(slot, method, ps, args, member);
            }
        }
        else
        {
            result = Fallback(slot, method, ps, args, member);
        }

        foreach (var p in ps.Where(p => p.IsOut))
            args[p.Position] = Apply(proxy.Source, $"{key}.{p.Name}", p.ParameterType.GetElementType()!, args[p.Position], 0);

        if (method.ReturnType == typeof(void))
            return null;
        if (IsSource(method.ReturnType))
        {
            if (result is null && answered)
                return null;
            if (!_children.TryGetValue(key, out var child))
                _children[key] = child = Proxy(method.ReturnType, result, key + ".", proxy.Source) ?? result!;
            return child;
        }
        return Apply(proxy.Source, key, method.ReturnType, result, 0);
    }

    private static object? Forward(UiMockProxy proxy, MethodInfo method, object?[] args)
    {
        if (proxy.Inner is null)
            return method.ReturnType == typeof(void) || !method.ReturnType.IsValueType ? null : Activator.CreateInstance(method.ReturnType);
        try
        {
            return method.Invoke(proxy.Inner, args);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    /// <summary>The member's last good answer (its outs too), else a made-up one.</summary>
    private object? Fallback(string slot, MethodInfo method, ParameterInfo[] ps, object?[] args, string member)
    {
        if (_lastGood.TryGetValue(slot, out var good))
        {
            foreach (var p in ps.Where(p => p.IsOut))
                args[p.Position] = good[p.Position + 1];
            return good[0];
        }
        foreach (var p in ps.Where(p => p.IsOut))
            args[p.Position] = Make(p.ParameterType.GetElementType()!, p.Name ?? member, 0);
        return method.ReturnType == typeof(void) || IsSource(method.ReturnType) ? null : Make(method.ReturnType, member, 0);
    }

    private static bool IsSource(Type t)
        => t.IsInterface && !typeof(IEnumerable).IsAssignableFrom(t);

    private static string Format(object? arg) => arg switch
    {
        null => "null",
        string s => JsonSerializer.Serialize(s),
        bool b => b ? "true" : "false",
        Enum e => e.ToString(),
        IFormattable f when arg.GetType().IsPrimitive => f.ToString(null, CultureInfo.InvariantCulture),
        _ => arg.GetType().Name,
    };

    //-------------------------------------------------------------------------
    // LEAVES: a value's editable parts, the edits laid over them.

    private static bool IsLeaf(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal);
    }

    private static string KindOf(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string) || t == typeof(char)) return "string";
        if (t.IsEnum) return "enum";
        if (t == typeof(float) || t == typeof(double) || t == typeof(decimal)) return "float";
        return "int";
    }

    private object? Apply(string source, string key, Type type, object? value, int depth)
    {
        if (IsLeaf(type))
        {
            object? edited = null;
            bool changed = _edits.TryGetValue(key, out var edit) && TryConvert(edit, type, out edited);
            if (changed)
                value = edited;
            Record(new UiMockLeaf(key, source, KindOf(type), Shown(value), changed,
                (Nullable.GetUnderlyingType(type) ?? type).IsEnum ? Enum.GetNames(Nullable.GetUnderlyingType(type) ?? type) : null));
            return value;
        }
        if (depth >= MaxDepth || value is null)
            return value;
        if (ElementOf(type) is { } element)
            return ApplyList(source, key, type, element, (IEnumerable)value, depth);
        if (type.IsInterface || type == typeof(object) || type.IsPointer || type == typeof(IntPtr))
            return value;

        var members = Members(value.GetType());
        if (members.Count == 0)
            return value;
        object copy = value.GetType().IsValueType ? value : Clone(value);
        bool any = false;
        foreach (var m in members)
        {
            var before = m.Get(copy);
            var after = Apply(source, $"{key}.{m.Name}", m.Type, before, depth + 1);
            if (!Equals(before, after))
            {
                m.Set(copy, after);
                any = true;
            }
        }
        return any ? copy : value;
    }

    private object? ApplyList(string source, string key, Type type, Type element, IEnumerable value, int depth)
    {
        var items = value.Cast<object?>().ToList();
        int count = items.Count;
        object? edited = null;
        bool changed = _edits.TryGetValue(key + ".Count", out var edit) && TryConvert(edit, typeof(int), out edited);
        if (changed)
            count = Math.Clamp((int)edited!, 0, MaxListCount);
        Record(new UiMockLeaf(key + ".Count", source, "int", count, changed));

        var result = new List<object?>(count);
        bool any = count != items.Count;
        for (int i = 0; i < count; i++)
        {
            object? before = i < items.Count ? items[i] : items.Count > 0 ? items[^1] : Make(element, key, depth + 1);
            var after = Apply(source, $"{key}[{i}]", element, before, depth + 1);
            any |= i >= items.Count || !Equals(before, after);
            result.Add(after);
        }
        if (!any)
            return value;
        if (type.IsArray)
        {
            var array = Array.CreateInstance(element, count);
            for (int i = 0; i < count; i++) array.SetValue(result[i], i);
            return array;
        }
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
        foreach (var item in result) list.Add(item);
        return list;
    }

    private void Record(UiMockLeaf leaf)
    {
        if (_leaves.TryAdd(leaf.Key, leaf))
            _order.Add(leaf.Key);
        else
            _leaves[leaf.Key] = leaf;
    }

    /// <summary>A leaf as JSON can hold it: an enum by name, and a float JSON has no number for (infinity, NaN) as its text.</summary>
    private static object? Shown(object? value) => value switch
    {
        Enum e => e.ToString(),
        char ch => ch.ToString(),
        double d when !double.IsFinite(d) => d.ToString(CultureInfo.InvariantCulture),
        float f when !float.IsFinite(f) => f.ToString(CultureInfo.InvariantCulture),
        _ => value,
    };

    /// <summary>An edit as <paramref name="type"/>; false (and the value kept) for one that does not convert.</summary>
    public static bool TryConvert(JsonElement edit, Type type, out object? value)
    {
        value = null;
        var under = Nullable.GetUnderlyingType(type);
        if (edit.ValueKind == JsonValueKind.Null)
            return under is not null || !type.IsValueType;
        var t = under ?? type;
        try
        {
            if (t == typeof(string))
            {
                value = edit.ValueKind == JsonValueKind.String ? edit.GetString() : edit.GetRawText();
                return true;
            }
            if (t == typeof(bool))
            {
                if (edit.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = edit.GetBoolean(); return true; }
                if (edit.ValueKind == JsonValueKind.String && bool.TryParse(edit.GetString(), out bool b)) { value = b; return true; }
                return false;
            }
            if (t == typeof(char))
            {
                string? s = edit.ValueKind == JsonValueKind.String ? edit.GetString() : null;
                if (string.IsNullOrEmpty(s)) return false;
                value = s[0];
                return true;
            }
            if (t.IsEnum)
            {
                if (edit.ValueKind == JsonValueKind.String && Enum.TryParse(t, edit.GetString(), true, out var named)) { value = named; return true; }
                if (edit.ValueKind == JsonValueKind.Number) { value = Enum.ToObject(t, edit.GetInt64()); return true; }
                return false;
            }
            double d;
            if (edit.ValueKind == JsonValueKind.Number) d = edit.GetDouble();
            else if (edit.ValueKind == JsonValueKind.String && double.TryParse(edit.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) d = parsed;
            else return false;
            if (t == typeof(float)) value = (float)d;
            else if (t == typeof(double)) value = d;
            else if (t == typeof(decimal)) value = (decimal)d;
            else value = Convert.ChangeType(Math.Round(Math.Clamp(d, MinOf(t), MaxOf(t))), t, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or InvalidOperationException)
        {
            value = null;
            return false;
        }
    }

    private static double MinOf(Type t) => Convert.ToDouble(t.GetField("MinValue")!.GetValue(null), CultureInfo.InvariantCulture);

    private static double MaxOf(Type t) => Convert.ToDouble(t.GetField("MaxValue")!.GetValue(null), CultureInfo.InvariantCulture);

    /// <summary>The element type of an array or a list interface (IReadOnlyList&lt;T&gt;, IList&lt;T&gt;, List&lt;T&gt;...); null for anything else.</summary>
    private static Type? ElementOf(Type t)
    {
        if (t == typeof(string)) return null;
        if (t.IsArray) return t.GetElementType();
        if (!t.IsGenericType) return null;
        var def = t.GetGenericTypeDefinition();
        return def == typeof(IReadOnlyList<>) || def == typeof(IList<>) || def == typeof(List<>) || def == typeof(IReadOnlyCollection<>)
               || def == typeof(IEnumerable<>) || def == typeof(ICollection<>)
            ? t.GetGenericArguments()[0] : null;
    }

    private sealed record Member(string Name, Type Type, Func<object, object?> Get, Action<object, object?> Set);

    /// <summary>A value's settable public members: properties with a setter (init too) and fields.</summary>
    private static List<Member> Members(Type t)
    {
        var list = new List<Member>();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0 || p.GetMethod is null || p.SetMethod is null || p.PropertyType == t)
                continue;
            list.Add(new Member(p.Name, p.PropertyType, p.GetValue, p.SetValue));
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (f.FieldType != t)
                list.Add(new Member(f.Name, f.FieldType, f.GetValue, f.SetValue));
        }
        return list;
    }

    private static object Clone(object value)
        => typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;

    //-------------------------------------------------------------------------
    // MADE-UP VALUES, for the interfaces a screen without samples reads.

    private object? Make(Type type, string name, int depth)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(bool)) return true;
        if (t == typeof(string)) return Words(name);
        if (t == typeof(char)) return 'A';
        if (t.IsEnum) return Enum.GetValues(t).Length > 0 ? Enum.GetValues(t).GetValue(0) : Activator.CreateInstance(t);
        if (t == typeof(float)) return 0.75f;
        if (t == typeof(double)) return 0.75;
        if (t == typeof(decimal)) return 0.75m;
        if (t.IsPrimitive)
            return Convert.ChangeType(name.Contains("Max", StringComparison.Ordinal) ? 100 : 3, t, CultureInfo.InvariantCulture);
        if (depth >= MaxDepth) return t.IsValueType ? Activator.CreateInstance(t) : null;
        if (ElementOf(t) is { } element)
        {
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element))!;
            // A list of what cannot be made up (an interface no proxy can
            // answer) is an empty one.
            for (int i = 0; i < 3 && !(element.IsInterface && !Proxiable(element)); i++)
                list.Add(Make(element, $"{name}[{i}]", depth + 1));
            if (!t.IsArray) return list;
            var array = Array.CreateInstance(element, list.Count);
            list.CopyTo(array, 0);
            return array;
        }
        if (IsSource(t))
            return Proxy(t, null, name.Replace("[]", "", StringComparison.Ordinal) + ".", t.Name);
        if (t.IsInterface || t.IsAbstract || t.IsPointer || t == typeof(IntPtr) || typeof(Delegate).IsAssignableFrom(t))
            return null;

        var ctor = t.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        object? made;
        try
        {
            made = ctor is not null && ctor.GetParameters().Length > 0
                ? ctor.Invoke(ctor.GetParameters().Select(p => Make(p.ParameterType, p.Name ?? "", depth + 1)).ToArray())
                : Activator.CreateInstance(t);
        }
        catch (Exception e) when (e is TargetInvocationException or MissingMethodException or ArgumentException or MemberAccessException)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
        if (made is null || (ctor?.GetParameters().Length ?? 0) > 0)
            return made;
        foreach (var m in Members(t))
            m.Set(made, Make(m.Type, m.Name, depth + 1));
        return made;
    }

    /// <summary>"ServerName" → "Server Name": what a made-up string says.</summary>
    public static string Words(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }
}

/// <summary>The proxy <see cref="UiMockSources"/> answers an interface with. Made by <see cref="DispatchProxy"/>.</summary>
public class UiMockProxy : DispatchProxy
{
    internal UiMockSources? Owner;
    internal object? Inner;
    internal string Prefix = "";
    internal string Source = "";

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => Owner!.Call(this, targetMethod!, args ?? []);
}
