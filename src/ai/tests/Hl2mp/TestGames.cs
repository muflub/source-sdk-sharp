using SourceSharp.Ai.Hl2mp;
using SourceSharp.Shared;
using SourceSharp.Server;
using SourceSharp.Ai.Server;

namespace SourceSharp.Ai.Tests;

/// <summary>The game a fact's server binding plays: HL2MP's, over the stock weapon catalog.</summary>
public static class TestGames
{
    public static IBotGame Hl2mp()
        => new Hl2mpBotGame(WeaponCatalog.Stock(), null!, DeclaredConVars.Fresh<WeaponRespawnTimeConVar>(), DeclaredConVars.Fresh<ItemRespawnTimeConVar>());
}
