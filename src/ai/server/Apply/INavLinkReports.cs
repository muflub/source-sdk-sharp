namespace SourceSharp.Ai.Server;

/// <summary>
/// Where the main thread hands what each bot's follower says of its route's next link (lane G5c-2b), as it takes the
/// bot's drive step back: the game's brains read it at their next decide. The step is stamped with the tick it was
/// handed on (its command's <see cref="IntentCommand.FromTick"/>), so a decide of tick D that reads the newest report
/// stamped before D reads the same report however the workers ran (the senses hand tick D's decides out after the
/// drives of tick D are taken). The binding of a game that has links registers one; none, and nothing is handed.
/// </summary>
public interface INavLinkReports
{
    /// <summary><paramref name="bot"/>'s follower said <paramref name="step"/> in the drive step handed on <paramref name="stamp"/>. Main thread.</summary>
    void Note(ActorId bot, Tick stamp, in NavLinkStep step);
}
