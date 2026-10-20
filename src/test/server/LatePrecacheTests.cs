using System.Collections.Generic;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Stock's late-precache warning for a model (CBaseEntity::PrecacheModel):
/// <c>if ( !IsPrecacheAllowed() ) { if ( !engine-&gt;IsModelPrecached( name ) )
/// DevMsg( "Late precache of %s -- not necessarily a bug now that we allow
/// ~everything to be dynamically loaded.\n", name ); }</c>. Managed's since
/// plan_server_native_v2 T4e.
/// </summary>
public class LatePrecacheTests
{
    private readonly List<string> _asked = [];
    private readonly List<string> _said = [];
    private bool _precached;

    private LatePrecache Rule() => new(name => { _asked.Add(name); return _precached; }, _said.Add);

    [Fact]
    public void ALateModelTheEngineDoesNotHaveIsReportedInStocksWords()
    {
        Rule().CheckModel("models/props/crate.mdl", allowed: false);

        Assert.Equal(["Late precache of models/props/crate.mdl -- not necessarily a bug now that we allow "
                      + "~everything to be dynamically loaded.\n"], _said);
    }

    [Fact]
    public void ALateModelTheEngineAlreadyHasIsNotReported()
    {
        _precached = true;

        Rule().CheckModel("models/props/crate.mdl", allowed: false);

        Assert.Equal(["models/props/crate.mdl"], _asked);
        Assert.Empty(_said);
    }

    /// <summary>Inside the window stock asks the engine nothing.</summary>
    [Fact]
    public void InsideTheWindowTheEngineIsNotAskedAndNothingIsSaid()
    {
        Rule().CheckModel("models/props/crate.mdl", allowed: true);

        Assert.Empty(_asked);
        Assert.Empty(_said);
    }

    /// <summary>Stock returns before the check for no name.</summary>
    [Fact]
    public void NoNameIsNotChecked()
    {
        Rule().CheckModel("", allowed: false);

        Assert.Empty(_asked);
        Assert.Empty(_said);
    }
}
