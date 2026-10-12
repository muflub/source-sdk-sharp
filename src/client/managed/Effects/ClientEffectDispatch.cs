//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's effect dispatch: <c>c_te_effect_dispatch.cpp</c>'s
/// <c>DispatchEffectToCallback</c> in C#. A name arrives (from the networked
/// <c>CTEEffectDispatch</c>, or from native code dispatching for itself), the
/// <see cref="EffectDispatchTable"/> decides which registration it runs, and
/// native runs that registration by walk index.
///
/// <para>
/// The table is filled from the native registration list the first time it is
/// needed. That list is built at static init and never changes afterwards, so
/// one read per load is the whole of its upkeep, and a managed reload re-reads
/// the same list: nothing to carry across one.
/// </para>
/// </summary>
public sealed class ClientEffectDispatch : IClientEffectDispatch
{
    private readonly IClientEffectRegistrations _natives;
    private readonly IEngineConsole _console;
    private readonly EffectDispatchTable _table = new();
    private readonly Dictionary<string, int> _runsByName = new(StringComparer.Ordinal);
    private bool _loaded;

    private readonly IClientEffectHandlers? _handlers;

    /// <param name="handlers">The managed client effects (a DECLARE_CLIENT_EFFECT
    /// that moved with its subsystem, such as the ropes' ShakeRopes), asked
    /// before the native registrations; none in a container that registers none.</param>
    public ClientEffectDispatch(IClientEffectRegistrations natives, IEngineConsole console, IClientEffectHandlers? handlers = null)
    {
        _natives = natives;
        _console = console;
        _handlers = handlers;
    }

    /// <summary>The table, filled.</summary>
    public EffectDispatchTable Table
    {
        get
        {
            EnsureLoaded();
            return _table;
        }
    }

    /// <summary>Dispatches that resolved and ran.</summary>
    public int Ran { get; private set; }

    /// <summary>Dispatches whose name matched no registration.</summary>
    public int Unclaimed { get; private set; }

    /// <summary>The name the last dispatch asked for, resolved or not.</summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>Runs per registered name, for the effects route and its gate.</summary>
    public IReadOnlyDictionary<string, int> RunsByName => _runsByName;

    public void Dispatch(string name, in EffectData data)
    {
        EnsureLoaded();
        LastName = name;

        if (_handlers is not null && _handlers.TryDispatch(name, in data))
        {
            Ran++;
            _runsByName[name] = _runsByName.TryGetValue(name, out int m) ? m + 1 : 1;
            return;
        }

        int index = _table.Lookup(name);
        if (index < 0)
        {
            Unclaimed++;

            // Stock's DevMsg: printed at developer 1 and above, silent below.
            if (int.TryParse(_console.FindConVarValue("developer"), out int level) && level >= 1)
                _console.Msg($"DispatchEffect: effect '{name}' not found on client\n");
            return;
        }

        Ran++;
        string registered = _table.Names[index];
        _runsByName[registered] = _runsByName.TryGetValue(registered, out int n) ? n + 1 : 1;
        _natives.Run(index, in data);
    }

    private void EnsureLoaded()
    {
        if (_loaded)
            return;

        int count = _natives.Count;
        for (int i = 0; i < count; i++)
        {
            string? name = _natives.Name(i);
            if (name is null)
                break;
            _table.Add(name);
        }

        // A client with no shim (a fact, a host without the client DLL) has no
        // registrations, and reads again next time rather than caching nothing.
        _loaded = count > 0;
    }
}
