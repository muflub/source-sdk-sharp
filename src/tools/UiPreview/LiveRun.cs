using SourceSharp.Client.Ui.Toolkit;

namespace SourceSharp.UiPreview;

/// <summary>
/// --live: the live mock-ups (<see cref="UiLive"/>) over the preview's
/// assemblies, theme, fonts and copy, answering one JSON request per line of
/// <c>input</c> with one line of <c>output</c>, until the input closes.
/// tools/ui-preview --watch runs it and serves its answers on /preview/live.
/// </summary>
public static class LiveRun
{
    public static int Serve(PreviewOptions options, TextReader input, TextWriter output)
    {
        var live = Create(options);
        output.WriteLine($"{{\"ready\":true,\"segments\":{live.Segments.Count}}}");
        output.Flush();
        while (input.ReadLine() is { } line)
        {
            if (line.Length == 0)
                continue;
            output.WriteLine(live.Answer(line));
            output.Flush();
        }
        return 0;
    }

    public static UiLive Create(PreviewOptions options)
    {
        var run = new PreviewRun(options);
        return new UiLive(UiLive.Discover(PreviewRun.ScreenAssemblies()), run.Theme(), run.Metrics,
                          UiLanguageFile.Chain(options.LanguageFiles()), run.Fonts.Faces, run.HudLibrary());
    }
}
