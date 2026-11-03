namespace SourceSharp.Client.Vgui;

/// <summary>
/// One level load as the GameUI saw it: whether the engine asked for the
/// progress dialog, how many progress updates came, how many answered yes
/// (the engine paints inside its load loop only then), and how many menu
/// frames were drawn between the load's start and its end.
/// </summary>
public readonly record struct GameUiLoad(bool Dialog, int Updates, int Repaints, long Frames);

/// <summary>
/// The loads' paint count (plan_gameui_managed.md §4): the GameUI host marks
/// each load's start, updates and end. Owned by the client module, so a UI
/// reload keeps the last load.
/// </summary>
public sealed class GameUiLoadMeter
{
    private bool _dialog;
    private int _updates;
    private int _repaints;
    private long _startFrames;

    /// <summary>The loads finished so far.</summary>
    public int Loads { get; private set; }

    /// <summary>The last finished load; null before the first.</summary>
    public GameUiLoad? Last { get; private set; }

    public void Started(bool dialog, long menuFrames)
    {
        _dialog = dialog;
        _updates = 0;
        _repaints = 0;
        _startFrames = menuFrames;
    }

    public void Updated(bool repaint)
    {
        _updates++;
        if (repaint)
            _repaints++;
    }

    public void Finished(long menuFrames)
    {
        Last = new GameUiLoad(_dialog, _updates, _repaints, menuFrames - _startFrames);
        Loads++;
    }
}
