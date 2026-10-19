namespace Debug.Api.Client.Interfaces;

/// <summary>
/// One of the client's own entities, as <c>/client/{slot}/entities</c> reports
/// it (plan_client_novalve §4.4): what its engine block says, the class the
/// client bound to it, and the managed state the client keeps for it.
/// </summary>
/// <param name="Handle">The raw entity handle.</param>
/// <param name="ClassName">The client class bound to it; empty when none is.</param>
/// <param name="Model">The model's name, as the engine names it.</param>
/// <param name="RenderOrigin">Where it was drawn last.</param>
/// <param name="InterpolationDepth">The deepest of its interpolation rings, in samples.</param>
/// <param name="ShadowType">The shadow type the shell last applied.</param>
/// <param name="ShadowChoice">The shadow the client chose for it.</param>
/// <param name="HasShadow">Whether the engine holds a shadow for it now.</param>
/// <param name="PolicyInputs">The render-policy block's input serial.</param>
/// <param name="PolicyOutputs">Its output serial: 0 when no decision was written.</param>
public sealed record DebugClientEntity(
    int Index, uint Handle, string ClassName, string Model, int ModelIndex,
    DebugClientVec3 Origin, DebugClientVec3 RenderOrigin, bool Dormant, int InterpolationDepth,
    int ShadowType, int ShadowChoice, bool HasShadow, int RenderGroup, bool Drawn, bool ViewModel,
    uint PolicyInputs, uint PolicyOutputs);

/// <summary>One decoded field of a [NetState]: its declaration and its value now.</summary>
/// <param name="Present">False when the state's bytes end before the field: no value is invented.</param>
public sealed record DebugClientNetValue(string Name, string Kind, int Offset, int Bits, bool Present, string Value, string Hex);

/// <summary>One [NetState] object on an entity, decoded through its declared layout.</summary>
/// <param name="Live">Whether the state has storage behind it now.</param>
public sealed record DebugClientNetStateBlock(string ClassName, string StateType, bool Live, int Bytes,
                                              IReadOnlyList<DebugClientNetValue> Fields);

/// <summary>One interpolated field: how deep its ring is, and the newest sample in it.</summary>
public sealed record DebugClientInterpolated(string Field, int Depth, float NewestTime, DebugClientVec3 Newest);

/// <summary>
/// One entity, as <c>/client/{slot}/entity/{n}</c> reports it: its list row,
/// its [NetState] blocks, and its interpolated values against the latest
/// (the engine block's networked origin and angles against what was drawn),
/// and the skin the block holds (what GetSkin answers; the server sends it as
/// m_nSkin since 2026-10-19).
/// </summary>
public sealed record DebugClientEntityState(
    DebugClientEntity Entity, DebugClientVec3 Angles, DebugClientVec3 RenderAngles,
    IReadOnlyList<DebugClientNetStateBlock> States, IReadOnlyList<DebugClientInterpolated> Interpolated,
    int Skin = 0);

/// <summary>The client's own entity list (plan_client_novalve §4.4). Game thread.</summary>
public interface IDebugClientEntities
{
    /// <summary>Every entity with an engine block, by index; only those of <paramref name="className"/> when it is not empty.</summary>
    IReadOnlyList<DebugClientEntity> List(string className);

    /// <summary>One entity by index, or null when no engine block has that index.</summary>
    DebugClientEntityState? State(int index);
}
