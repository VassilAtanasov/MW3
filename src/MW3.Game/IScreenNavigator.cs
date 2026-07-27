namespace MW3.Game;

internal interface IScreenNavigator
{
    void Push(IScreen screen);

    /// <summary>
    /// Pops the current screen, returning to whichever is beneath it - used by a screen dismissing
    /// itself (e.g. FR-7's ending screen) rather than in response to a back request, which
    /// <see cref="ScreenManager"/> already handles centrally before a screen's own <c>Update</c> runs.
    /// </summary>
    void Pop();
}
