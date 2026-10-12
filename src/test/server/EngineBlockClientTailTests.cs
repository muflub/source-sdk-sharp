using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE FINGERPRINT COVERS THE CLIENT TAIL — step 5c's third gate, and the one
/// step 1a does not already answer.
///
/// <para>
/// 1a's mutation facts are about the head and about the server tail's
/// reservations. That is not an accident of coverage: when 1a landed, the client
/// tail was declared and written by nothing, so a hash that ignored it would
/// have looked perfectly healthy for as long as the client did not read the
/// block. Step 5c is what makes the client read it, so this is the moment the
/// tail's participation stops being decoration.
/// </para>
///
/// <para>
/// A WARNING ABOUT THE OBVIOUS WAY TO WRITE THIS, WHICH DOES NOT WORK, and
/// which 5c wrote first and caught with a mutation. Wrapping a reordered copy
/// of the tail in a block-shaped outer and asserting the two hashes differ
/// PASSES EVEN IF THE HASH NEVER LOOKS INSIDE THE TAIL AT ALL — because
/// <c>TypeTokenOf</c> mixes the field's type FullName, and a copy of a struct
/// necessarily has a different name from the original. Verified by mutation:
/// with <c>if (Recurses(f))</c> turned into <c>if (false &amp;&amp; Recurses(f))</c>
/// in <c>EngineBlockFingerprint</c>, all five of those facts still passed — and
/// so, in the same run, did 1a's three nested-mutation facts, which are built
/// the same way. See the handover.
/// </para>
///
/// <para>
/// SO THE FACTS BELOW ARE IN TWO GROUPS. The first hashes the TAIL TYPES
/// THEMSELVES, where every field is a primitive and there is no nested type
/// name to smuggle the difference through — that is what proves the tail's
/// field order, field types and field count each move a hash this function
/// computes. The second is an ORACLE: an independent walk, written out here,
/// that visibly descends into all three of the block's sub-structs. It goes red
/// the moment the production walk stops descending, which is the failure the
/// first group cannot see.
/// </para>
/// </summary>
public class EngineBlockClientTailTests
{
    //-------------------------------------------------------------------------
    // Group one: the tail's own fields move a hash this function computes.
    //
    // Hashed as TOP-LEVEL TYPES rather than wrapped in a block, deliberately.
    // Every field of the tail is a primitive, so nothing here is reached by
    // recursion and nothing here can differ by a nested type's name: the only
    // difference between each pair is the edit the fact is named for.
    //-------------------------------------------------------------------------

    [Fact]
    public void ReorderingTwoFieldsInTheClientTailMovesTheFingerprint()
    {
        // Skin and Body swapped. Two ints, so the struct's size, its field
        // count and every field's own width are untouched, and so is the set of
        // names. Only the ORDER moved — which on the wire is a skin decoded as
        // a body group.
        Assert.Equal(Marshal.SizeOf<EngineBlockClientTail>(), Marshal.SizeOf<ClientTailReordered>());

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(EngineBlockClientTail)),
                        EngineBlockFingerprint.Of(typeof(ClientTailReordered)));
    }

    [Fact]
    public void RetypingAClientTailFieldAtTheSameWidthMovesTheFingerprint()
    {
        // Cycle as an int rather than a float: same width, same offset, same
        // name, same struct size. Nothing a size check or an offset check could
        // ever see — and it is the difference between a cycle of 0.5 and a
        // cycle of 1056964608.
        Assert.Equal(Marshal.SizeOf<EngineBlockClientTail>(), Marshal.SizeOf<ClientTailRetyped>());

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(EngineBlockClientTail)),
                        EngineBlockFingerprint.Of(typeof(ClientTailRetyped)));
    }

    [Fact]
    public void AddingAFieldToTheClientTailMovesTheFingerprint()
    {
        // This one cannot keep the size, and that is the honest shape of the
        // mutation: Phase 6's pose parameters make the tail longer.
        Assert.NotEqual(Marshal.SizeOf<EngineBlockClientTail>(), Marshal.SizeOf<ClientTailExtended>());

        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(EngineBlockClientTail)),
                        EngineBlockFingerprint.Of(typeof(ClientTailExtended)));
    }

    //-------------------------------------------------------------------------
    // Group two: the BLOCK's fingerprint actually descends into the tail.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheBlocksFingerprintIsTheWalkThatDescendsIntoTheClientTail()
    {
        // AN ORACLE, AND THE ONLY FACT HERE THAT SEES THE RECURSION. Walk()
        // below is written out in this file and visibly descends into Head,
        // Server and Client; the production function is asserted to agree with
        // it on the real block. Turn off the recursion in
        // EngineBlockFingerprint and the two part company at once, which is
        // what every other fact in this file — and 1a's three — miss.
        //
        // A second implementation, on purpose. In production that would be two
        // opinions about one thing; in a test it is the oracle, and it is the
        // only construction that can tell "the tail was hashed" from "the tail's
        // TYPE NAME was hashed".
        Assert.Equal(Walk(typeof(EngineBlock)), EngineBlockFingerprint.Of(typeof(EngineBlock)));
    }

    [Fact]
    public void TheOracleItselfNoticesAReorderInsideTheClientTail()
    {
        // WHAT THE ORACLE IS WORTH, checked rather than assumed. If Walk() were
        // itself shallow, the fact above would be two shallow walks agreeing and
        // would say nothing. These two outers have identical top-level shapes
        // and differ only one level down, inside the tail.
        Assert.NotEqual(Walk(typeof(BlockWithReorderedClientTail)),
                        Walk(typeof(EngineBlock)));
    }

    [Fact]
    public void RemovingTheClientTailMovesTheFingerprint()
    {
        // The shallowest possible statement of "the tail is in the hash", kept
        // because it is the one that survives a rewrite of everything above it.
        Assert.NotEqual(EngineBlockFingerprint.Of(typeof(EngineBlock)),
                        EngineBlockFingerprint.Of(typeof(BlockWithNoClientTail)));
    }

    [Fact]
    public void TheClientTailIsTheThirdAndLastFieldOfTheBlock()
        => Assert.Equal(new[] { "Head", "Server", "Client" },
                        Array.ConvertAll(EngineBlock.FieldsOf(typeof(EngineBlock)), f => f.Name));

    //-------------------------------------------------------------------------
    // The stand-ins. Each is a copy of EngineBlockClientTail with exactly one
    // edit, wrapped in a block-shaped outer that carries the real head and the
    // real server tail so that everything except the edit is genuinely equal.
    //-------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockWithReorderedClientTail
    {
        public EngineBlockHead Head;
        public EngineBlockServerTail Server;
        public ClientTailReordered Client;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockWithNoClientTail
    {
        public EngineBlockHead Head;
        public EngineBlockServerTail Server;
    }

    /// <summary>Skin and Body swapped. Two ints, so nothing else moves.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientTailReordered
    {
        public Vector RenderOrigin;
        public QAngle RenderAngles;
        public Vector RenderMins;
        public Vector RenderMaxs;
        public byte RenderColorR;
        public byte RenderColorG;
        public byte RenderColorB;
        public byte RenderColorA;
        public int FxBlend;
        public int Body;
        public int Skin;
        public int ShadowChoice;
        public int RenderGroup;
        public int Sequence;
        public float Cycle;
        public float PlaybackRate;
        public float AnimTime;
        public float SimulationTime;
        public Transform RenderTransform;
        public int Drawn;
        public int Transparent;
        public int ShadowType;
        public int ShadowFlags;
        public int Dormant;
        public int ViewModel;
        public int CustomDraw;
        public int Translucent;
        public int HideWhenOwnerSeen;
        public int FadeStyle;
        public float FadeStartDistance;
        public float FadeEndDistance;
        public float FadeMinAlpha;
        public int ModelType;
        public int StudioSequenceCount;
        public int StudioPoseParameterCount;
        public int StudioBoneControllerCount;
        public int StudioIkChainCount;
        public uint PolicyInputs;
        public uint PolicyOutputs;
        public int HitboxSet;
    }

    /// <summary>Cycle as an int rather than a float. Same four bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientTailRetyped
    {
        public Vector RenderOrigin;
        public QAngle RenderAngles;
        public Vector RenderMins;
        public Vector RenderMaxs;
        public byte RenderColorR;
        public byte RenderColorG;
        public byte RenderColorB;
        public byte RenderColorA;
        public int FxBlend;
        public int Skin;
        public int Body;
        public int ShadowChoice;
        public int RenderGroup;
        public int Sequence;
        public int Cycle;
        public float PlaybackRate;
        public float AnimTime;
        public float SimulationTime;
        public Transform RenderTransform;
        public int Drawn;
        public int Transparent;
        public int ShadowType;
        public int ShadowFlags;
        public int Dormant;
        public int ViewModel;
        public int CustomDraw;
        public int Translucent;
        public int HideWhenOwnerSeen;
        public int FadeStyle;
        public float FadeStartDistance;
        public float FadeEndDistance;
        public float FadeMinAlpha;
        public int ModelType;
        public int StudioSequenceCount;
        public int StudioPoseParameterCount;
        public int StudioBoneControllerCount;
        public int StudioIkChainCount;
        public uint PolicyInputs;
        public uint PolicyOutputs;
        public int HitboxSet;
    }

    /// <summary>One more field, where Phase 6's pose parameters will go.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ClientTailExtended
    {
        public Vector RenderOrigin;
        public QAngle RenderAngles;
        public Vector RenderMins;
        public Vector RenderMaxs;
        public byte RenderColorR;
        public byte RenderColorG;
        public byte RenderColorB;
        public byte RenderColorA;
        public int FxBlend;
        public int Skin;
        public int Body;
        public int ShadowChoice;
        public int RenderGroup;
        public int Sequence;
        public float Cycle;
        public float PlaybackRate;
        public float AnimTime;
        public float SimulationTime;
        public Transform RenderTransform;
        public int Drawn;
        public int Transparent;
        public int ShadowType;
        public int ShadowFlags;
        public int Dormant;
        public int ViewModel;
        public int CustomDraw;
        public int Translucent;
        public int HideWhenOwnerSeen;
        public int FadeStyle;
        public float FadeStartDistance;
        public float FadeEndDistance;
        public float FadeMinAlpha;
        public int ModelType;
        public int StudioSequenceCount;
        public int StudioPoseParameterCount;
        public int StudioBoneControllerCount;
        public int StudioIkChainCount;
        public uint PolicyInputs;
        public uint PolicyOutputs;
        public float PoseParameter0;
        public int HitboxSet;
    }

    //-------------------------------------------------------------------------
    // The oracle.
    //
    // FNV-1a over the same shape description EngineBlockFingerprint documents:
    // field count, marshalled size, then each field's name, type token, offset
    // and size in declaration order — and, for a field that is a struct rather
    // than a primitive, an enum or a fixed buffer, the same walk over IT.
    //
    // Written out rather than shared with production, because a helper both
    // sides called would agree with itself no matter what either did.
    //-------------------------------------------------------------------------
    private static uint Walk(Type type)
    {
        uint hash = 2166136261u;

        void Mix(uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                hash ^= (byte)(value >> (i * 8));
                hash *= 16777619u;
            }
        }

        void MixText(string text)
        {
            foreach (char c in text)
            {
                hash ^= (byte)c;
                hash *= 16777619u;
            }

            hash ^= 0xFF;
            hash *= 16777619u;
        }

        void MixStruct(Type t)
        {
            FieldInfo[] fields = EngineBlock.FieldsOf(t);

            Mix((uint)fields.Length);
            Mix((uint)Marshal.SizeOf(t));

            foreach (FieldInfo f in fields)
            {
                FixedBufferAttribute? buffer = f.GetCustomAttribute<FixedBufferAttribute>();

                MixText(f.Name);
                MixText(buffer is not null
                            ? buffer.ElementType.FullName + "[]"
                            : f.FieldType.FullName ?? f.FieldType.Name);
                Mix((uint)Marshal.OffsetOf(t, f.Name));
                Mix((uint)Marshal.SizeOf(f.FieldType));

                if (f.FieldType.IsValueType && !f.FieldType.IsPrimitive
                    && !f.FieldType.IsEnum && buffer is null)
                {
                    MixStruct(f.FieldType);
                }
            }
        }

        MixStruct(type);

        return hash;
    }
}
