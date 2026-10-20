//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Client;

namespace SourceSharp.Client.Render;

/// <summary>
/// One screen-space post-processing effect (ScreenSpaceEffects.h's
/// IScreenSpaceEffect). <see cref="Render"/> draws into the frame's plan at the
/// point stock's RenderEffects reached it: the ops it appends run there.
/// </summary>
public interface IScreenSpaceEffect
{
    void Init();

    void Shutdown();

    void SetParameters(KeyValuesNode? parameters);

    void Render(int x, int y, int w, int h, RenderPlanBuilder plan);

    void Enable(bool enable);

    bool IsEnabled();
}

/// <summary>
/// ScreenSpaceEffects.cpp's CScreenSpaceEffectManager: the registration list
/// (each registration goes on the head, as CScreenSpaceEffectRegistration's
/// constructor pushes it, so the newest is walked first), and the manager's
/// calls over it. Held to the pristine file by
/// ScreenSpaceEffectManagerOracleTests over Fixtures/screenspaceeffects-stock.tsv.
///
/// <para>
/// Stock's one registration is <see cref="ExampleScreenSpaceEffect"/>, made
/// here as its ADD_SCREENSPACE_EFFECT made it at static init.
/// <see cref="Init"/> and <see cref="Shutdown"/> run at the view's level start
/// and end (IManagedRenderClient::ScreenSpaceEffectsStage, from
/// shell_viewrender_client.cpp's LevelInit and LevelShutdown, where stock's
/// CViewRender called them); <see cref="RenderEffects"/> runs from the render
/// plan at stock's place in CViewRender::RenderView.
/// </para>
///
/// <para>
/// ONE DIVERGENCE: with <c>-filmgrain</c> on the command line and no effect
/// named filmgrain (this build registers none), stock's Init dereferences the
/// lookup's NULL and the client crashes; here the lookup's warning stands and
/// Init goes on.
/// </para>
///
/// <para>
/// A managed reload carries each registration's enabled flag: the new build's
/// effects are initialised, as the level start initialised the old build's,
/// and given back their flags.
/// </para>
/// </summary>
public sealed class ScreenSpaceEffectManager : IReloadParticipant
{
    private readonly List<(string Name, IScreenSpaceEffect? Effect)> _list = [];
    private readonly Action<string> _warning;
    private readonly Func<string, bool> _commandLineHas;

    /// <param name="warning">Where stock's Warning goes (the console).</param>
    /// <param name="commandLineHas">CommandLine()->FindParm, as a yes or no.</param>
    public ScreenSpaceEffectManager(Action<string> warning, Func<string, bool> commandLineHas)
    {
        _warning = warning;
        _commandLineHas = commandLineHas;
    }

    /// <summary>The game's: stock's one registration, CExampleEffect as "exampleeffect".</summary>
    public static ScreenSpaceEffectManager WithStockEffects(Action<string> warning, Func<string, bool> commandLineHas)
    {
        var m = new ScreenSpaceEffectManager(warning, commandLineHas);
        m.Register("exampleeffect", new ExampleScreenSpaceEffect());
        return m;
    }

    /// <summary>The registrations, the order the manager walks them (the newest first).</summary>
    public IReadOnlyList<(string Name, IScreenSpaceEffect? Effect)> Registrations => _list;

    /// <summary>CScreenSpaceEffectRegistration: onto the head of the list.</summary>
    public void Register(string name, IScreenSpaceEffect? effect) => _list.Insert(0, (name, effect));

    /// <summary>InitScreenSpaceEffects.</summary>
    public void Init()
    {
        if (_commandLineHas("-filmgrain"))
            Get("filmgrain")?.Enable(true);

        foreach ((string _, IScreenSpaceEffect? effect) in _list)
        {
            if (effect is null)
                continue;
            bool enabled = effect.IsEnabled();
            effect.Init();
            effect.Enable(enabled);
        }
    }

    /// <summary>ShutdownScreenSpaceEffects.</summary>
    public void Shutdown()
    {
        foreach ((string _, IScreenSpaceEffect? effect) in _list)
            effect?.Shutdown();
    }

    /// <summary>GetScreenSpaceEffect: the first registration of the name, case aside; a warning for none.</summary>
    public IScreenSpaceEffect? Get(string name)
    {
        foreach ((string n, IScreenSpaceEffect? effect) in _list)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return effect;
        }
        _warning("Could not find screen space effect " + name + "\n");
        return null;
    }

    /// <summary>SetScreenSpaceEffectParams by name.</summary>
    public void SetParameters(string name, KeyValuesNode? parameters)
    {
        if (Get(name) is { } effect)
            SetParameters(effect, parameters);
    }

    /// <summary>SetScreenSpaceEffectParams by effect.</summary>
    public static void SetParameters(IScreenSpaceEffect? effect, KeyValuesNode? parameters) => effect?.SetParameters(parameters);

    /// <summary>EnableScreenSpaceEffect by name.</summary>
    public void Enable(string name)
    {
        if (Get(name) is { } effect)
            Enable(effect);
    }

    /// <summary>EnableScreenSpaceEffect by effect.</summary>
    public static void Enable(IScreenSpaceEffect? effect) => effect?.Enable(true);

    /// <summary>DisableScreenSpaceEffect by name.</summary>
    public void Disable(string name)
    {
        if (Get(name) is { } effect)
            Disable(effect);
    }

    /// <summary>DisableScreenSpaceEffect by effect.</summary>
    public static void Disable(IScreenSpaceEffect? effect) => effect?.Enable(false);

    /// <summary>DisableAllScreenSpaceEffects.</summary>
    public void DisableAll()
    {
        foreach ((string _, IScreenSpaceEffect? effect) in _list)
            effect?.Enable(false);
    }

    /// <summary>RenderEffects: every registered effect, enabled or not (each decides for itself), into the plan.</summary>
    public void RenderEffects(int x, int y, int w, int h, RenderPlanBuilder plan)
    {
        foreach ((string _, IScreenSpaceEffect? effect) in _list)
            effect?.Render(x, y, w, h, plan);
    }

    void IReloadParticipant.Save(ReloadWriter writer)
    {
        writer.Write(_list.Count);
        foreach ((string name, IScreenSpaceEffect? effect) in _list)
        {
            writer.Write(name);
            writer.Write(effect?.IsEnabled() ?? false);
        }
    }

    void IReloadParticipant.Restore(ReloadReader reader)
    {
        int n = reader.ReadInt();
        var enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < n; i++)
        {
            string name = reader.ReadString();
            bool on = reader.ReadBool();
            enabled.TryAdd(name, on);
        }
        foreach ((string name, IScreenSpaceEffect? effect) in _list)
        {
            if (effect is null || !enabled.TryGetValue(name, out bool on))
                continue;
            effect.Init();
            effect.Enable(on);
        }
    }
}

/// <summary>
/// ScreenSpaceEffects.cpp's CExampleEffect, "exampleeffect": stock's example
/// of an effect, registered in every build. Its Init leaves it disabled and its
/// Enable does nothing ("This is just example code, don't enable it"), so it
/// never renders. Its draw arm (UpdateScreenEffectTexture and a screen-space
/// rectangle over a material its Init never finds) is unreachable in stock and
/// is not carried.
/// </summary>
public sealed class ExampleScreenSpaceEffect : IScreenSpaceEffect
{
    private bool _enable;

    public void Init() => _enable = false;

    // m_Material.Shutdown(): the material was never found, so there is nothing to release.
    public void Shutdown() { }

    public void SetParameters(KeyValuesNode? parameters)
    {
        // Stock reads example_param's type and does nothing with it ("// ...").
        _ = parameters?.Children.FirstOrDefault(c => string.Equals(c.NameText, "example_param", StringComparison.OrdinalIgnoreCase))?.Type
            == KeyValuesType.String;
    }

    public void Render(int x, int y, int w, int h, RenderPlanBuilder plan)
    {
        if (!IsEnabled())
            return;
    }

    // "This is just example code, don't enable it."
    public void Enable(bool enable) { }

    public bool IsEnabled() => _enable;
}
