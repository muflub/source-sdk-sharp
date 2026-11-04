using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>Every declaration filled over a pool of edge values, and its members as exact bits.</summary>
internal static class TempEntFill
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

    private static readonly int[] Ints =
    [
        0, 1, -1, 2, 127, 128, 200, 255, 256, -128, -129, 32767, 32768, 65535, 65536,
        int.MinValue, int.MaxValue, 1 << 20, (1 << 21) - 1, 0x12345678,
    ];

    private static readonly float[] Floats =
    [
        0f, -0f, 1f, -1f, 0.5f, 1e30f, -1e30f, float.Epsilon, float.NaN, float.PositiveInfinity, 3218.25f,
    ];

    private static readonly EntityHandle[] Handles =
    [
        EntityHandle.None,
        new(0u),
        new(1u),
        new(7u | (3u << 16)),
        new(2047u | (1023u << 16)),
        new(4095u | (0xFFFFu << 16)),
        new(uint.MaxValue - 1),
    ];

    /// <summary>Every <c>[TeProp]</c> field on a type and its chain.</summary>
    private static IEnumerable<FieldInfo> Props(Type type)
        => type.GetFields(Members).Where(f => f.GetCustomAttribute<TePropAttribute>() is not null);

    /// <summary>A new declaration of <paramref name="type"/>, each member at an edge value by seed.</summary>
    public static object Filled(Type type, int seed)
    {
        object target = Activator.CreateInstance(type)!;
        var random = new Random(seed);
        int position = 0;

        foreach (FieldInfo field in Props(type))
            field.SetValue(target, ValueFor(field, field.GetValue(target), seed + position++ * 7, random));

        return target;
    }

    private static object? ValueFor(FieldInfo field, object? current, int pick, Random random)
    {
        Type t = field.FieldType;

        int Int(int shift = 0) => pick % 3 == 2 ? random.Next() - random.Next() : Ints[(pick + shift) % Ints.Length];

        float Float(int shift = 0)
            => pick % 3 == 2 ? (float)(random.NextDouble() * 8000 - 4000) : Floats[(pick + shift) % Floats.Length];

        if (field.GetCustomAttribute<TePropAttribute>() is { Inferred: false, Kind: TePropKind.DataTable })
            return Filled(t, pick);

        if (t.GetCustomAttribute<InlineArrayAttribute>() is { } run)
        {
            object box = current ?? Activator.CreateInstance(t)!;
            var bytes = new byte[run.Length * Unsafe.SizeOf<Vector>()];

            for (int i = 0; i + 4 <= bytes.Length; i += 4)
                BitConverter.TryWriteBytes(bytes.AsSpan(i), Float(i));

            GCHandle pin = GCHandle.Alloc(box, GCHandleType.Pinned);
            Marshal.Copy(bytes, 0, pin.AddrOfPinnedObject(), bytes.Length);
            pin.Free();

            return box;
        }

        if (t == typeof(Vector)) return new Vector(Float(), Float(1), Float(2));
        if (t == typeof(QAngle)) return new QAngle(Float(), Float(1), Float(2));
        if (t == typeof(Color32)) return new Color32(Int(), Int(1), Int(2), Int(3));
        if (t == typeof(float)) return Float();
        if (t == typeof(bool)) return pick % 2 == 0;
        if (t == typeof(byte)) return unchecked((byte)Int());
        if (t == typeof(short)) return unchecked((short)Int());
        if (t == typeof(int)) return Int();
        if (t == typeof(EntityHandle)) return Handles[pick % Handles.Length];
        if (t.IsEnum) return Enum.ToObject(t, unchecked((byte)Int()));

        throw new InvalidOperationException($"nothing fills a {t.Name} ({field.Name})");
    }

    /// <summary>A declaration's every member as exact bits, so NaN and -0 compare as what they are.</summary>
    public static string Dump(object declaration)
    {
        var text = new StringBuilder();

        Dump(declaration, text, "");

        return text.ToString();
    }

    private static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("x8");

    private static void Dump(object declaration, StringBuilder text, string prefix)
    {
        foreach (FieldInfo field in Props(declaration.GetType()))
        {
            object value = field.GetValue(declaration)!;
            string name = prefix + field.Name;
            Type t = field.FieldType;

            if (field.GetCustomAttribute<TePropAttribute>() is { Inferred: false, Kind: TePropKind.DataTable })
            {
                Dump(value, text, name + ".");
                continue;
            }

            if (t.GetCustomAttribute<InlineArrayAttribute>() is { } run)
            {
                var bytes = new byte[run.Length * Unsafe.SizeOf<Vector>()];

                GCHandle pin = GCHandle.Alloc(value, GCHandleType.Pinned);
                Marshal.Copy(pin.AddrOfPinnedObject(), bytes, 0, bytes.Length);
                pin.Free();

                text.Append(name).Append('=').AppendLine(Convert.ToHexString(bytes));
                continue;
            }

            string shown = value switch
            {
                Vector v => $"{Bits(v.X)} {Bits(v.Y)} {Bits(v.Z)}",
                QAngle q => $"{Bits(q.Pitch)} {Bits(q.Yaw)} {Bits(q.Roll)}",
                float f => Bits(f),
                EntityHandle h => h.Value.ToString("x8"),
                Color32 c => $"{c.R} {c.G} {c.B} {c.A}",
                Enum e => Convert.ToInt64(e).ToString(),
                _ => value.ToString() ?? "",
            };

            text.Append(name).Append('=').AppendLine(shown);
        }
    }
}
