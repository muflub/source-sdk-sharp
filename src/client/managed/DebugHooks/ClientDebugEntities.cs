using System.Runtime.InteropServices;

using SourceSharp.Abi;
using Debug.Api.Client.Interfaces;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientEntities"/> over the client's render-policy list
/// (every standalone entity's engine block, unfiltered), the classes bound to
/// them, the engine's model names and shadows, this side's declared net types
/// and its interpolation rings (plan_client_novalve §4.4). Game thread: the
/// dev API calls it from its pump.
/// </summary>
public sealed class ClientDebugEntities(IRenderPolicyBlocks blocks, IEngineEntityRender render,
                                        IClientStandalones standalones, Interpolation interpolation,
                                        ClientNetSchemaTypes types)
    : IDebugClientEntities
{
    public IReadOnlyList<DebugClientEntity> List(string className)
    {
        var list = new List<DebugClientEntity>();
        foreach (var block in Blocks())
        {
            var row = Row(block);
            if (className.Length > 0 && !string.Equals(row.ClassName, className, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add(row);
        }

        list.Sort((a, b) => a.Index.CompareTo(b.Index));
        return list;
    }

    public DebugClientEntityState? State(int index)
    {
        foreach (var block in Blocks())
        {
            if (IndexOf(block) != index)
                continue;

            var states = new List<DebugClientNetStateBlock>();
            if (standalones.GetByIndex(index) is { } entity)
            {
                foreach (var state in entity.NetStates)
                {
                    if (state is null)
                        continue;
                    byte[] bytes = state.Snapshot();
                    var type = NetSchema.Find(types.All, state.NetClassName);
                    var fields = type is null
                        ? []
                        : NetSchema.Decode(type, bytes)
                                   .Select(d => new DebugClientNetValue(d.Field.Name, d.Field.Kind.ToString().ToLowerInvariant(),
                                                                       d.Field.Offset, d.Field.Bits, d.Present, d.Value, d.Hex))
                                   .ToArray();
                    states.Add(new DebugClientNetStateBlock(state.NetClassName, state.GetType().Name, state.IsLive,
                                                            bytes.Length, fields));
                }
            }

            var interpolated = new List<DebugClientInterpolated>();
            for (int field = 0; field < InterpolatedFields.Count; field++)
            {
                if (interpolation.PeekRing(index, field) is not { Count: > 0 } ring)
                    continue;
                var newest = ring[0];
                interpolated.Add(new DebugClientInterpolated(InterpolatedFields.All[field].Name, ring.Count,
                                                             newest.ChangeTime, new DebugClientVec3(newest.A, newest.B, newest.C)));
            }

            return new DebugClientEntityState(Row(block), Vec(block.Head.Angles), Vec(block.Client.RenderAngles),
                                              states, interpolated, block.Client.Skin);
        }

        return null;
    }

    /// <summary>
    /// The blocks, copied: the policy pass begins its own list before it reads
    /// again, so asking here changes nothing it will see.
    /// </summary>
    private List<EngineBlock> Blocks()
    {
        int count = blocks.Begin();
        var copies = new List<EngineBlock>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            var span = blocks.Block(i);
            if (span.Length >= EngineBlock.SizeBytes)
                copies.Add(MemoryMarshal.Read<EngineBlock>(span));
        }
        return copies;
    }

    private static int IndexOf(in EngineBlock block) => (int)(block.Head.Handle & Interpolation.EntryMask);

    private DebugClientEntity Row(in EngineBlock block)
    {
        int index = IndexOf(block);
        var handle = new EntityHandle(block.Head.Handle);
        render.ShadowCast(handle, out _, out bool hasShadow);

        return new DebugClientEntity(
            index, block.Head.Handle, standalones.GetByIndex(index)?.ClassName ?? "", render.GetModelName(handle),
            block.Head.ModelIndex, Vec(block.Head.Origin), Vec(block.Client.RenderOrigin), block.Client.Dormant != 0,
            interpolation.RingDepth(index),
            block.Client.ShadowType, block.Client.ShadowChoice, hasShadow, block.Client.RenderGroup,
            block.Client.Drawn != 0, block.Client.ViewModel != 0,
            block.Client.PolicyInputs, block.Client.PolicyOutputs);
    }

    private static DebugClientVec3 Vec(Vector v) => new(v.X, v.Y, v.Z);

    private static DebugClientVec3 Vec(QAngle a) => new(a.Pitch, a.Yaw, a.Roll);
}
