using System.Collections.Generic;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_server_native_v2 T4d: a map decal's precache through IVEngineServer's
/// forwarders, where the IShimPrecache::PrecacheDecal slot ran
/// SourceSharp_PrecacheDecal and stock's UTIL_PrecacheDecal
/// (server_util_library.cpp, util.cpp in stock):
///
/// <code>
/// if ( !pName || !pName[0] ) return -1;              // the slot's guard
/// if ( !CBaseEntity::IsPrecacheAllowed() )            // UTIL_PrecacheDecal
///     if ( !engine->IsDecalPrecached( name ) )
///         Warning( "Late precache of %s\n", name );
/// return engine->PrecacheDecal( name, true );         // preload, as the slot passed
/// </code>
/// </summary>
public sealed class DecalPrecacheFoldTests
{
    private sealed class Engine : IDecalPrecacheEngine
    {
        public readonly List<string> Calls = new();
        public bool Allowed = true;
        public bool Precached;
        public int Index = 17;

        public bool PrecacheAllowed()
        {
            Calls.Add("allowed?");
            return Allowed;
        }

        public bool IsDecalPrecached(string name)
        {
            Calls.Add($"precached? {name}");
            return Precached;
        }

        public int PrecacheDecal(string name, bool preload)
        {
            Calls.Add($"precache {name} {preload}");
            return Index;
        }

        public void Warning(string text) => Calls.Add($"warn {text}");
    }

    private readonly Engine _engine = new();

    [Fact]
    public void InTheWindowTheDecalIsPrecachedPreloadedAndItsIndexAnswered()
    {
        Assert.Equal(17, DecalPrecache.Precache(_engine, "decals/scorch1"));
        Assert.Equal(new[] { "allowed?", "precache decals/scorch1 True" }, _engine.Calls);
    }

    [Fact]
    public void ALatePrecacheOfANewDecalWarnsAndStillPrecaches()
    {
        _engine.Allowed = false;

        Assert.Equal(17, DecalPrecache.Precache(_engine, "decals/new"));
        Assert.Equal(new[]
        {
            "allowed?", "precached? decals/new", "warn Late precache of decals/new\n",
            "precache decals/new True",
        }, _engine.Calls);
    }

    [Fact]
    public void ALateAskForADecalAlreadyPrecachedIsQuiet()
    {
        _engine.Allowed = false;
        _engine.Precached = true;

        DecalPrecache.Precache(_engine, "decals/old");

        Assert.DoesNotContain(_engine.Calls, c => c.StartsWith("warn", System.StringComparison.Ordinal));
        Assert.Contains("precache decals/old True", _engine.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoNameIsMinusOneAndAsksTheEngineNothing(string? name)
    {
        Assert.Equal(-1, DecalPrecache.Precache(_engine, name));
        Assert.Empty(_engine.Calls);
    }

    [Fact]
    public void AMaterialThatWouldNotLoadIsTheEnginesMinusOne()
    {
        _engine.Index = -1;

        Assert.Equal(-1, DecalPrecache.Precache(_engine, "decals/typo"));
    }
}
