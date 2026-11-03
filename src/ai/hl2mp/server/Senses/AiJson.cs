using System.Buffers;
using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The JSON every AI route answers (plan_hl2mp_ai §7.3), written here by
/// walking the value's public properties onto a <see cref="Utf8JsonWriter"/>:
/// camel-case names, an <see cref="AiVector"/> as <c>[x, y, z]</c> rounded to
/// a tenth, NaN and the infinities spelled out, enums as numbers, nulls
/// written. Never through <see cref="JsonSerializer"/>: its caches, which live
/// in the default context, kept the type metadata of the AI's records, and with
/// it the server's collectible load context, alive past the unload at
/// shutdown (the live gate's "ServerALC did NOT unload" after any record or
/// replay route was asked). A writer keeps nothing of the types it is handed.
/// </summary>
public static class AiJson
{
    /// <summary>The deepest a value nests (the serializer's own default).</summary>
    public const int MaxDepth = 64;

    /// <summary><paramref name="value"/> as the routes' JSON.</summary>
    public static string Write(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            Value(writer, value, 0);
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>A string as a JSON string literal, quotes included.</summary>
    public static string Quote(string? s) => Write(s);

    private static void Value(Utf8JsonWriter w, object? value, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException($"AiJson: a value nests deeper than {MaxDepth}");
        switch (value)
        {
            case null:
                w.WriteNullValue();
                return;
            case string s:
                w.WriteStringValue(s);
                return;
            case bool b:
                w.WriteBooleanValue(b);
                return;
            case float f:
                if (float.IsFinite(f))
                    w.WriteNumberValue(f);
                else
                    w.WriteStringValue(NonFinite(f));
                return;
            case double d:
                if (double.IsFinite(d))
                    w.WriteNumberValue(d);
                else
                    w.WriteStringValue(NonFinite(d));
                return;
            case decimal m:
                w.WriteNumberValue(m);
                return;
            case Enum e:
                if (Type.GetTypeCode(Enum.GetUnderlyingType(e.GetType())) is TypeCode.UInt64)
                    w.WriteNumberValue(Convert.ToUInt64(e, System.Globalization.CultureInfo.InvariantCulture));
                else
                    w.WriteNumberValue(Convert.ToInt64(e, System.Globalization.CultureInfo.InvariantCulture));
                return;
            case ulong u:
                w.WriteNumberValue(u);
                return;
            case sbyte or byte or short or ushort or int or uint or long:
                w.WriteNumberValue(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                return;
            case AiVector v:
                w.WriteStartArray();
                w.WriteNumberValue(MathF.Round(v.X, 1));
                w.WriteNumberValue(MathF.Round(v.Y, 1));
                w.WriteNumberValue(MathF.Round(v.Z, 1));
                w.WriteEndArray();
                return;
            case IDictionary map:
                w.WriteStartObject();
                foreach (DictionaryEntry kv in map)
                {
                    w.WritePropertyName(Convert.ToString(kv.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "");
                    Value(w, kv.Value, depth + 1);
                }

                w.WriteEndObject();
                return;
            case IEnumerable list:
                w.WriteStartArray();
                foreach (object? item in list)
                    Value(w, item, depth + 1);
                w.WriteEndArray();
                return;
            default:
                Members(w, value, depth);
                return;
        }
    }

    /// <summary>An object: each public instance property with a getter, in declaration order, by its camel-case name.</summary>
    private static void Members(Utf8JsonWriter w, object value, int depth)
    {
        w.WriteStartObject();
        foreach (PropertyInfo p in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0 || p.GetMethod is not { IsPublic: true })
                continue;
            w.WritePropertyName(JsonNamingPolicy.CamelCase.ConvertName(p.Name));
            Value(w, p.GetValue(value), depth + 1);
        }

        w.WriteEndObject();
    }

    private static string NonFinite(double d) => double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity";
}
