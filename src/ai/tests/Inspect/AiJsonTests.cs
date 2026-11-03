using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Tests;
using Xunit;

namespace SourceSharp.Ai.Tests.Inspect;

/// <summary>
/// The JSON the AI's routes answer (plan_hl2mp_ai §7.3) is written by
/// <see cref="AiJson"/>, not by <see cref="JsonSerializer"/>: the serializer's
/// caches live in the default context and kept the AI's record types, and so
/// the server's collectible load context, alive after an inspector record
/// (<c>/ai/bot/&lt;slot&gt;</c>) or a replay frame (<c>/ai/replay/bot/&lt;slot&gt;</c>)
/// was asked (the live gate's "ServerALC did NOT unload" at shutdown).
/// </summary>
public sealed class AiJsonTests
{
    /// <summary>The routes' old spelling: the serializer with the inspector's options (camel case, vectors as arrays, NaN spelled out).</summary>
    private sealed class VectorAsArray : JsonConverter<AiVector>
    {
        public override AiVector Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, AiVector value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(MathF.Round(value.X, 1));
            writer.WriteNumberValue(MathF.Round(value.Y, 1));
            writer.WriteNumberValue(MathF.Round(value.Z, 1));
            writer.WriteEndArray();
        }
    }

    private static JsonSerializerOptions OldOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new VectorAsArray() },
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// A value of <paramref name="type"/> with every member filled (one element
    /// per list, one entry per map, a NaN beside every finite float), built by
    /// reflection so it works over a second load of the AI's assemblies too.
    /// </summary>
    private static object? Sample(Type type, int depth = 0)
    {
        if (depth > 12)
            return null;
        if (type == typeof(string))
            return "s\"q";
        if (type == typeof(bool))
            return true;
        if (type == typeof(float))
            return depth % 2 == 0 ? 1.25f : float.NaN;
        if (type == typeof(double))
            return 2.5;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(Enum.GetValues(type).Length - 1);
        if (type.IsPrimitive)
            return Convert.ChangeType(7, type, System.Globalization.CultureInfo.InvariantCulture);
        // A vector's components are finite (the serializer's converter refused a NaN one, as the writer does).
        if (type.Name == nameof(AiVector))
            return Activator.CreateInstance(type, 1.25f, -2.5f, 300.04f);
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return Sample(inner, depth + 1);
        if (type.IsArray)
        {
            Array a = Array.CreateInstance(type.GetElementType()!, 1);
            a.SetValue(Sample(type.GetElementType()!, depth + 1), 0);
            return a;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            object map = Activator.CreateInstance(type)!;
            Type[] kv = type.GetGenericArguments();
            type.GetMethod("Add")!.Invoke(map, [Sample(kv[0], depth + 1), Sample(kv[1], depth + 1)]);
            return map;
        }

        if (type.IsInterface && type.IsGenericType)
            return Sample(type.GetGenericArguments()[0].MakeArrayType(), depth);
        ConstructorInfo? ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null)
            return Activator.CreateInstance(type);
        return ctor.Invoke([.. ctor.GetParameters().Select(p => Sample(p.ParameterType, depth + 1))]);
    }

    /// <summary>The routes' JSON of every record they answer is what the serializer wrote (the web inspector reads the same fields).</summary>
    [Fact]
    public void AiJsonSpellsWhatTheSerializerSpelled()
    {
        object?[] values =
        [
            Sample(typeof(BotInspectRecord)), Sample(typeof(IntentCommand)), Sample(typeof(InspectDoing)),
            new { path = "p", world = true, first = -1, levels = new[] { 1, 2 }, bots = new[] { new { slot = 1, levels = new[] { 3 } } }, fault = (string?)null },
            "a \"quoted\" <name>", float.PositiveInfinity,
        ];
        foreach (object? v in values)
            Assert.Equal(JsonSerializer.Serialize(v, v?.GetType() ?? typeof(object), OldOptions()), AiJson.Write(v));
    }

    /// <summary>The sample reaches every member: a NaN and an array of each record kind are in the record's JSON.</summary>
    [Fact]
    public void TheSampleRecordFillsEveryPart()
    {
        string json = AiJson.Write(Sample(typeof(BotInspectRecord)));
        foreach (string part in (string[])["\"memory\":{", "\"actors\":[{", "\"habits\":{", "\"belief\":{", "\"doing\":{", "\"decides\":[{", "\"nodes\":[{", "\"derived\":{", "\"NaN\""])
            Assert.Contains(part, json, StringComparison.Ordinal);
    }

    /// <summary>No AI route spells its JSON through the serializer (the one shared writer is <see cref="AiJson"/>).</summary>
    [Fact]
    public void NoAiRouteCallsTheSerializer()
    {
        string[] callers = IlCallCensus.Callers(m => m.DeclaringType == typeof(JsonSerializer), typeof(AiJson).Assembly, typeof(BotInspectRecord).Assembly);
        Assert.Empty(callers);
    }

    /// <summary>
    /// A second load of the AI's two assemblies in a collectible context, as the
    /// game's server context holds them: an inspector record and a replay frame's
    /// intent written by that load's <see cref="AiJson"/>, then the context
    /// unloaded. It must be collected.
    /// </summary>
    [Fact]
    public void TheServersContextUnloadsAfterTheRecordRoutesAnswered()
    {
        (WeakReference weak, int written) = LoadAskAndUnload();
        Assert.True(written > 100, $"the copy wrote the record and the intent ({written} chars)");
        for (int i = 0; i < 20 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(weak.IsAlive, "the context that wrote the AI's records unloads");
    }

    private sealed class AiCopyContext() : AssemblyLoadContext("AiJsonUnload", isCollectible: true)
    {
        private static readonly string[] Copied = ["SourceSharp.Ai", "SourceSharp.Ai.Hl2mp"];

        protected override Assembly? Load(AssemblyName name)
        {
            if (!Copied.Contains(name.Name))
                return null;
            string dir = Path.GetDirectoryName(typeof(AiJson).Assembly.Location)!;
            return LoadFromAssemblyPath(Path.Combine(dir, name.Name + ".dll"));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Weak, int Written) LoadAskAndUnload()
    {
        var context = new AiCopyContext();
        Assembly binding = context.LoadFromAssemblyName(new AssemblyName("SourceSharp.Ai.Hl2mp"));
        Assembly core = context.LoadFromAssemblyName(new AssemblyName("SourceSharp.Ai"));
        Assert.NotSame(typeof(AiJson).Assembly, binding);
        Assert.Same(context, AssemblyLoadContext.GetLoadContext(core));
        MethodInfo write = binding.GetType(typeof(AiJson).FullName!, true)!.GetMethod(nameof(AiJson.Write))!;
        int written = 0;
        foreach (string name in (string[])[typeof(BotInspectRecord).FullName!, typeof(IntentCommand).FullName!])
            written += ((string)write.Invoke(null, [Sample(core.GetType(name, true)!)])!).Length;
        var weak = new WeakReference(context, trackResurrection: true);
        context.Unload();
        return (weak, written);
    }
}
