using Debug.Api.Client.Interfaces;
using SourceSharp.Client.Ui;
using SourceSharp.Client.Ui.Toolkit;
using SourceSharp.Client.Vgui;

namespace SourceSharp.Client;

/// <summary>
/// <see cref="IDebugClientUi"/> over the UI host (plan_client_ui U2): the last
/// frame's ledger and render list, read on the game thread the channel answers on.
/// </summary>
public sealed class ClientDebugUi(UiHost host) : IDebugClientUi
{
    public DebugClientUi Read(bool svg) => Report(host.HudLedger, host.Layer, host.Screen, host.Cost, svg, null);

    public DebugClientUi ReadMenu(bool svg)
        => Report(host.MenuLedger, host.Menu, host.MenuScreen, host.MenuCost, svg,
                  host.Menu.OpenScreens().ToArray());

    public string Event(string argument)
    {
        var f = argument.Split(',');
        if (f.Length < 3 || !int.TryParse(f[0], out int kind) || !int.TryParse(f[1], out int x) || !int.TryParse(f[2], out int y))
            return "{\"error\":\"want kind,x,y[,code,char,delta]\"}";
        int code = f.Length > 3 && int.TryParse(f[3], out int c) ? c : 0;
        int ch = f.Length > 4 && int.TryParse(f[4], out int h) ? h : 0;
        float delta = f.Length > 5 && float.TryParse(f[5], System.Globalization.CultureInfo.InvariantCulture, out float d) ? d : 0f;
        host.Input(kind, x, y, code, ch, delta);
        return $"{{\"ok\":true,\"inputs\":{host.Inputs},\"menuInput\":{(host.MenuInput ? "true" : "false")}}}";
    }

    private DebugClientUi Report(UiDrawLedger ledger, UiLayer layer, ScreenInfo s, UiFrameCost c, bool svg, IReadOnlyList<string>? open)
    {
        var draws = new List<DebugClientUiDraw>(ledger.Count);
        var kinds = new int[Enum.GetValues<UiLedgerKind>().Length];
        for (int i = 0; i < ledger.Count; i++)
        {
            var e = ledger[i];
            kinds[(int)e.Kind]++;
            draws.Add(new DebugClientUiDraw(e.Kind.ToString(), e.X0, e.Y0, e.X1, e.Y1,
                $"#{e.Color.R:x2}{e.Color.G:x2}{e.Color.B:x2}{e.Color.A:x2}", e.Font, new string(ledger.TextOf(e)), e.Element));
        }

        string? picture = null;
        if (svg && c.Frames > 0)
        {
            var backend = new SvgBackend(host.Backend);
            backend.Draw(layer.Context.List.AsDrawList());
            picture = backend.Svg;
        }

        return new DebugClientUi(c.Frames > 0, s.W, s.H, s.Scale, c.Frames, c.MeanMicroseconds, c.LastMicroseconds,
            c.MaxMicroseconds,
            Enum.GetValues<UiLedgerKind>().Select(k => new DebugCount(k.ToString(), kinds[(int)k])).ToArray(),
            draws, picture, open, host.MenuInput);
    }

    public IReadOnlyList<DebugClientUiFont> Fonts(bool sweep)
    {
        var b = host.Backend;
        if (sweep)
        {
            var theme = host.Layer.Context.Theme;
            foreach (var size in UiSamples.Sizes())
            {
                float scale = size.Screen.Scale;
                foreach (var role in Enum.GetValues<Text>())
                {
                    var t = theme.Type(role);
                    b.FontHandle(new UiFontSpec(t.Family, Math.Max(1, (int)MathF.Round(t.SizeDu * scale)), t.Weight, t.Flags));
                }
            }
        }

        return b.Fonts.OrderBy(f => f.Family, StringComparer.Ordinal).ThenBy(f => f.PxHeight).ThenBy(f => f.Weight).Select(f =>
        {
            var m = b.Metrics(f);
            var advances = new int[95];
            for (int i = 0; i < advances.Length; i++)
                advances[i] = (int)MathF.Round(b.Advance(f, (char)(32 + i)));
            return new DebugClientUiFont(f.Family, f.PxHeight, f.Weight, (int)f.Flags, b.FontHandle(f),
                (int)MathF.Round(m.LineHeight), (int)MathF.Round(m.Ascent), 32, advances);
        }).ToArray();
    }
}
