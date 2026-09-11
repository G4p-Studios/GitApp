namespace GitApp.Services;

/// <summary>
/// Moving between screens, without a Shell or a NavigationPage.
///
/// Both of those add chrome to the UI Automation tree that a screen reader
/// then has to be walked past, and both impose a mobile back-stack model on
/// what is a desktop app. Swapping the window's page keeps the tree to what
/// the screen actually contains.
///
/// What this has to get right is focus. A screen change that leaves focus on
/// the window root strands a screen reader user with nothing to read and no
/// obvious way forward, and it is the single most common way an app like
/// this becomes unusable. So every page arriving here is expected to place
/// focus itself on appearing, and returning restores the pane the user left
/// from.
/// </summary>
public static class AppNavigator
{
    private static readonly Stack<Page> Back = new();

    public static bool CanGoBack => Back.Count > 0;

    public static void Show(Page page)
    {
        if (Window is not { } window || window.Page is not { } current)
        {
            return;
        }

        Back.Push(current);
        window.Page = page;
    }

    public static bool GoBack()
    {
        if (Window is not { } window || Back.Count == 0)
        {
            return false;
        }

        window.Page = Back.Pop();
        return true;
    }

    private static Window? Window =>
        Application.Current?.Windows.Count > 0 ? Application.Current.Windows[0] : null;
}
