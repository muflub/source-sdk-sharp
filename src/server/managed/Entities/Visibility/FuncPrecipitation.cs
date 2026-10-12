//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// func_precipitation: a volume it rains, snows or ashes inside.
///
/// <para>
/// The server entity is a brush and one key. Stock's <c>CPrecipitation::Spawn</c>
/// precaches four materials, binds the model, makes it non-solid, defaults the
/// type to rain and sets a render mode — and that is the whole class. Everything
/// that makes weather look like weather is on the client, which is why the pair
/// is worth reading together with <see cref="SourceSharp.Client.Standalone.ClientPrecipitation"/>.
/// </para>
///
/// <para>
/// The density is the interesting key. Stock does not network it: it travels as
/// the entity's render-colour alpha and the client remaps it into a rate. That
/// is not a place anyone would choose, and it IS the place a mapper types it —
/// the key is "renderamt" — so it is read here under that name, carried on the
/// wire as its own field, and put back into the alpha the renderer reads.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncPrecipitation, ScriptedEntityKind.Brush)]
public sealed class FuncPrecipitation : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="FuncPrecipitationVars"/>.</summary>
    [ScriptVars] public FuncPrecipitationVars Vars = new();

    [NetState] public PrecipitationNetState State = new();

    public override void OnSpawn()
    {
        // Nothing collides with rain and nothing draws the brush. The volume
        // still matters: it is where the weather is, and the client emits
        // across the top of it.
        SetSolidity(BrushSolidity.None);
        SetVisible(false);

        // Rain for anything the map did not say, which is stock's default and
        // its guard against a map writing a type this build does not have.
        State.PrecipType = Vars.PrecipType is >= 0 and < 4 ? Vars.PrecipType : (int)PrecipitationType.Rain;
        State.Density    = (byte)Math.Clamp(Vars.Density, 0, 255);
    }
}

/// <summary>
/// The map keys <see cref="FuncPrecipitation"/> declares.
/// </summary>
[ScriptVars]
public partial class FuncPrecipitationVars
{
    /// <summary>0 rain, 1 snow, 2 ash, 3 snowfall.</summary>
    public int PrecipType { get; set; }

    /// <summary>How heavy. Stock reads this as the render alpha and calls it nothing else.</summary>
    [VarName("renderamt")]
    public int Density { get; set; } = 255;
}
