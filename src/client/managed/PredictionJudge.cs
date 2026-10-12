//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Buffers.Binary;

namespace SourceSharp.Abi;

/// <summary>
/// The field-aware verdict on a prediction compare that already failed — the
/// managed twin of SourceSharp_PredictionJudgeFields (shell_prediction.cpp),
/// over the SAME declaration: <see cref="StandaloneNetStates.DescribeOffset"/>
/// names the field each differing byte belongs to and carries its [Predicted]
/// tolerance, and only the float-shaped kinds can be tolerated, per component,
/// exactly as stock's CompareVector does. A tolerance that is not positive
/// never tolerates — stock's rule, predictioncopy.cpp:736.
/// </summary>
public static class PredictionJudge
{
    /// <summary>
    /// The offset of the first field <paramref name="stateType"/> declares, or 0
    /// (compare everything, the old behaviour) when the schema cannot say —
    /// rather than silently comparing nothing.
    /// </summary>
    public static int DeclaredStart(Type stateType, int imageBytes)
    {
        var schema = StandaloneNetStates.Of(stateType);

        if (schema is null || schema.Fields.Count == 0)
            return 0;

        int first = imageBytes;

        foreach (var field in schema.Fields)
        {
            if (field.Offset < first)
                first = field.Offset;
        }

        return first >= 0 && first < imageBytes ? first : 0;
    }

    /// <summary>
    /// The predicted image a compare judges: the ARRIVAL, with everything from
    /// <paramref name="declaredStart"/> on taken from the prediction. Nothing on
    /// the client predicts the engine block in front of the declared fields --
    /// the server writes it (a carried weapon is moved to its owner every
    /// frame) -- so comparing it reports the server's writes as this side's
    /// mistakes. Offsets stay absolute, which is what lets the judge name a
    /// field; a slice would renumber every one of them.
    /// </summary>
    public static byte[] OverlayDeclared(ReadOnlySpan<byte> predicted, ReadOnlySpan<byte> arrived,
                                         int declaredStart)
    {
        byte[] image = arrived.ToArray();
        predicted[declaredStart..].CopyTo(image.AsSpan(declaredStart));
        return image;
    }

    /// <summary>
    /// Walks the differing bytes of two images of one blob.
    /// </summary>
    /// <returns>
    /// How many differing bytes STAND as mispredicted after tolerances — zero
    /// when every difference sat inside a declared tolerance.
    /// </returns>
    public static int Judge(NetStateCatalog catalog, uint typeId, ReadOnlySpan<byte> predicted, ReadOnlySpan<byte> arrived,
                            out string fields, out int toleratedFields)
    {
        int bytes = Math.Min(predicted.Length, arrived.Length);
        int standing = 0;
        toleratedFields = 0;

        List<string>? labels = null;
        int i = 0;

        while (i < bytes)
        {
            if (predicted[i] == arrived[i])
            {
                i++;
                continue;
            }

            int answer = catalog.DescribeOffset(typeId, i,
                out string name, out int start, out int size, out int kind, out float tolerance);

            string label;
            bool tolerated = false;
            int advanceTo = i + 1;

            if (answer == 1 && start >= 0 && start + size <= bytes)
            {
                tolerated = Tolerated(kind, tolerance,
                    predicted.Slice(start, size), arrived.Slice(start, size));

                // The whole field is judged at once; skip to its end so a
                // vector that moved is one entry, not twelve.
                advanceTo = start + size;
                label = tolerated ? $"{name} (tolerated)" : name;
            }
            else
            {
                label = $"(padding@{i})";
            }

            if (tolerated)
            {
                toleratedFields++;
            }
            else
            {
                for (int b = i; b < advanceTo && b < bytes; b++)
                {
                    if (predicted[b] != arrived[b])
                        standing++;
                }
            }

            (labels ??= new List<string>()).Add(label);
            i = Math.Max(advanceTo, i + 1);
        }

        fields = labels is null ? "" : string.Join(", ", labels);
        return standing;
    }

    /// <summary>
    /// Whether one field's difference sits inside its declared tolerance.
    /// Kind numbering is <see cref="StandaloneNetStates.TypeCodeOf"/>'s:
    /// 1 float, 2 vector, 7 qangle are the float-shaped kinds; an integer
    /// either matches or it does not.
    /// </summary>
    public static bool Tolerated(int kind, float tolerance,
                                 ReadOnlySpan<byte> predicted, ReadOnlySpan<byte> arrived)
    {
        if (tolerance <= 0f)
            return false;

        int components = kind switch
        {
            1 => 1,  // float
            2 => 3,  // vector
            7 => 3,  // qangle
            _ => 0,
        };

        if (components == 0 || predicted.Length < components * 4 || arrived.Length < components * 4)
            return false;

        for (int c = 0; c < components; c++)
        {
            float was = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(predicted.Slice(c * 4, 4)));
            float now = BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32LittleEndian(arrived.Slice(c * 4, 4)));

            if (MathF.Abs(was - now) > tolerance)
                return false;
        }

        return true;
    }
}
