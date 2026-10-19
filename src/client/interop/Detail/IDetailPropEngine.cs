using SourceSharp.Abi;

namespace SourceSharp.Client.Detail;

/// <summary>What the detail props ask the engine (IVEngineClient's and IVModelInfo's detail calls).</summary>
public interface IDetailPropEngine
{
    int GameLumpVersion(int lumpId);

    int GameLumpSize(int lumpId);

    /// <summary><c>LoadGameLump( id, buffer, buffer.Length )</c>.</summary>
    bool LoadGameLump(int lumpId, Span<byte> buffer);

    /// <summary><c>LoadModel</c>: the engine's model, as a handle native code hands back.</summary>
    nint LoadModel(string name, bool prop);

    bool IsModelVertexLit(nint model);

    bool ModelHasMaterialProxy(nint model);

    /// <summary><c>LinearToGamma( linear, gamma )</c>, three channels.</summary>
    Vector LinearToGamma(Vector linear);

    float LightStyleValue(int style);

    /// <summary><c>ComputeDynamicLighting( point, &amp;normal, color )</c>.</summary>
    Vector ComputeDynamicLighting(Vector point, Vector normal);

    /// <summary>tier0's <c>Warning</c>.</summary>
    void Warning(string text);
}
