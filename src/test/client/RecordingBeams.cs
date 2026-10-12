using SourceSharp.Abi;
using SourceSharp.Client.Beams;

namespace SourceSharp.Tests;

/// <summary>
/// An <see cref="IClientBeams"/> that records each call into a
/// <see cref="RecordingEngine"/>'s ordered log as <c>Beams.&lt;Method&gt;</c>,
/// every argument in its declared position and type, so a temp entity
/// player's call reads like the engine calls around it.
/// </summary>
internal sealed class RecordingBeams(RecordingEngine engine) : IClientBeams
{
    public BeamCounters Counters => default;

    public void CreateBeamPoints(in Vector start, in Vector end, int modelIndex, int haloIndex, float haloScale,
                                 float life, float width, float endWidth, float fadeLength, float amplitude,
                                 float brightness, float speed, int startFrame, float frameRate, float r, float g,
                                 float b)
        => engine.RecordCall("Beams.CreateBeamPoints", start, end, modelIndex, haloIndex, haloScale, life, width,
                             endWidth, fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b);

    public void CreateBeamEnts(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                               float width, float endWidth, float fadeLength, float amplitude, float brightness,
                               float speed, int startFrame, float frameRate, float r, float g, float b, int type)
        => engine.RecordCall("Beams.CreateBeamEnts", startEnt, endEnt, modelIndex, haloIndex, haloScale, life, width,
                             endWidth, fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b, type);

    public void CreateBeamEntPoint(int startEnt, Vector? start, int endEnt, Vector? end, int modelIndex,
                                   int haloIndex, float haloScale, float life, float width, float endWidth,
                                   float fadeLength, float amplitude, float brightness, float speed, int startFrame,
                                   float frameRate, float r, float g, float b)
        => engine.RecordCall("Beams.CreateBeamEntPoint", startEnt, start, endEnt, end, modelIndex, haloIndex,
                             haloScale, life, width, endWidth, fadeLength, amplitude, brightness, speed, startFrame,
                             frameRate, r, g, b);

    public void CreateBeamRing(int startEnt, int endEnt, int modelIndex, int haloIndex, float haloScale, float life,
                               float width, float endWidth, float fadeLength, float amplitude, float brightness,
                               float speed, int startFrame, float frameRate, float r, float g, float b, int flags)
        => engine.RecordCall("Beams.CreateBeamRing", startEnt, endEnt, modelIndex, haloIndex, haloScale, life, width,
                             endWidth, fadeLength, amplitude, brightness, speed, startFrame, frameRate, r, g, b, flags);

    public void CreateBeamRingPoint(in Vector center, float startRadius, float endRadius, int modelIndex,
                                    int haloIndex, float haloScale, float life, float width, float endWidth,
                                    float fadeLength, float amplitude, float brightness, float speed,
                                    int startFrame, float frameRate, float r, float g, float b, int flags)
        => engine.RecordCall("Beams.CreateBeamRingPoint", center, startRadius, endRadius, modelIndex, haloIndex,
                             haloScale, life, width, endWidth, fadeLength, amplitude, brightness, speed, startFrame,
                             frameRate, r, g, b, flags);

    public void CreateBeamFollow(int startEnt, int modelIndex, int haloIndex, float haloScale, float life,
                                 float width, float endWidth, float fadeLength, float r, float g, float b,
                                 float brightness)
        => engine.RecordCall("Beams.CreateBeamFollow", startEnt, modelIndex, haloIndex, haloScale, life, width,
                             endWidth, fadeLength, r, g, b, brightness);

    public void CreateBeamPoints(BeamInfo info) => engine.RecordCall("Beams.CreateBeamPoints(BeamInfo)", info);

    public void DrawQuadratic(in Vector start, in Vector control, in Vector end, float width, in Vector color,
                              float scrollOffset, int spriteIndex, int renderMode)
        => engine.RecordCall("Beams.DrawQuadratic", start, control, end, width, color, scrollOffset, spriteIndex,
                             renderMode);
}
