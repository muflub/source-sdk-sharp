//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Effects;

/// <summary>
/// What the particle query asks the engine: whether a level is up, and the
/// world's light at a point (<c>engine-&gt;IsInGame</c>,
/// <c>engine-&gt;GetLightForPoint</c>).
/// </summary>
public interface IParticleQueryEngine
{
    bool IsInGame();

    Vector LightForPoint(Vector position, bool clamp);
}

/// <summary>The real one, over IVEngineClient, acquired on first use. Not in game, and black, with no engine.</summary>
public sealed class ParticleQueryEngine(Func<IVtableEngineClient?> acquire) : IParticleQueryEngine
{
    private IVtableEngineClient? _engine;

    public bool IsInGame()
    {
        _engine ??= acquire();
        return _engine is not null && _engine.IsInGame();
    }

    public Vector LightForPoint(Vector position, bool clamp)
    {
        _engine ??= acquire();
        return _engine is not null && _engine.TryLightForPoint(position, clamp, out Vector light) ? light : default;
    }
}

/// <summary>
/// <c>CParticleSystemQuery</c> (particlesystemquery.cpp): the answers
/// particles.a asks the game for, decided here; the native thunk
/// (shell_managed_particles.cpp) carries them out. plan_client_novalve §3.4,
/// lane c5-particles. The library calls five methods (C0 probe 1); these are
/// those five, replayed against <c>Fixtures/particlequery-stock.tsv.gz</c>.
/// </summary>
public sealed class ParticleSystemQuery(IParticleQueryEngine engine) : Interop.IClientParticleQuery
{
    /// <summary>
    /// <c>GetCollisionGroupFromName</c>: <c>s_NameMap</c>, without its
    /// <c>TF_CLIENT_DLL</c> row, matched as <c>stricmp</c> matches (ASCII case
    /// folded); a name not in it is <c>COLLISION_GROUP_NONE</c>.
    /// </summary>
    public int CollisionGroupFromName(string name) => (int)(AsciiUpper(name) switch
    {
        "NONE" => EngineCollisionGroup.None,
        "DEBRIS" => EngineCollisionGroup.Debris,
        "INTERACTIVE" => EngineCollisionGroup.Interactive,
        "NPC" => EngineCollisionGroup.Npc,
        "ACTOR" => EngineCollisionGroup.NpcActor,
        "PASSABLE" => EngineCollisionGroup.PassableDoor,
        _ => EngineCollisionGroup.None,
    });

    // stricmp folds ASCII letters only; this does the same whatever the
    // runtime's globalization mode does with the rest.
    private static string AsciiUpper(string s) => string.Create(s.Length, s, (span, src) =>
    {
        for (int i = 0; i < src.Length; i++)
            span[i] = src[i] is >= 'a' and <= 'z' ? (char)(src[i] - 32) : src[i];
    });

    /// <summary>
    /// <c>GetLightingAtPoint</c>: in game, the engine's clamped light scaled
    /// to bytes with an alpha of 0 (<c>Color::SetColor( x*255, y*255, z*255,
    /// 0 )</c>: the float product truncated to an int, then to a byte); out of
    /// game, white with an alpha of 255.
    /// </summary>
    public (byte R, byte G, byte B, byte A) LightingAtPoint(Vector origin)
    {
        if (!engine.IsInGame())
            return (255, 255, 255, 255);

        Vector light = engine.LightForPoint(origin, clamp: true);
        return (ToByte(light.X * 255), ToByte(light.Y * 255), ToByte(light.Z * 255), 0);
    }

    // SetColor takes ints: the float converts by truncation, and the int is
    // stored as an unsigned char.
    private static byte ToByte(float scaled) => unchecked((byte)(int)scaled);

    /// <summary>
    /// <c>TraceLine</c>'s decision: in game, trace exactly as the library
    /// asked (its mask, its ignore entity, its collision group) and hand back
    /// the whole trace; out of game, no trace, and the answer is
    /// <c>startsolid = 0, fraction = 1</c> with every other field left as the
    /// caller had it.
    /// </summary>
    public bool ShouldTrace() => engine.IsInGame();

    /// <summary>
    /// <c>GetRandomPointsOnControllingObjectHitBox</c>. Every point is the
    /// control point's own position, hit box 0 and a zero relative coordinate
    /// where the library asked for them: the "don't have a model" fallback,
    /// which is this build's whole answer whether or not an object is bound
    /// (the model arm asked a <c>C_BaseAnimating</c> for its hit boxes, and no
    /// managed entity is one; see the lane report on porting that arm).
    /// </summary>
    public void RandomPointsOnControllingObjectHitBox(Vector controlPoint, bool hasObject, Span<Vector> points,
                                                      Span<int> hitBoxIndices, Span<Vector> relativeCoords)
    {
        _ = hasObject;
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = controlPoint;
            if (i < hitBoxIndices.Length)
                hitBoxIndices[i] = 0;
            if (i < relativeCoords.Length)
                relativeCoords[i] = default;
        }
    }

    /// <summary><c>GetControllingObjectHitBoxInfo</c>: no hit boxes, for the reason above.</summary>
    public int ControllingObjectHitBoxInfo(bool hasObject)
    {
        _ = hasObject;
        return 0;
    }
}
