using GitApp.Accessibility;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class CommandPalettePage : ContentPage
{
    private readonly CommandPaletteViewModel _vm = new();

    public CommandPalettePage()
    {
        InitializeComponent();
        BindingContext = _vm;

        CommandsPane.EntryControl = SearchEntry;
        PlatformFocus.TrackFocusWithin(CommandList, "commands");
        PlatformFocus.DescribeEmptyView(CommandList, "No matching commands");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = Close;
        PaneNavigation.ActivateHandler = TryRunFromList;
        PaneNavigation.SubmitHandler = () =>
        {
            _ = _vm.RunSelectedAsync();
            return true;
        };

        SearchEntry.Completed -= OnSearchCompleted;
        SearchEntry.Completed += OnSearchCompleted;

        PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);
    }

    protected override void OnDisappearing()
    {
        Announcer.Current.StatusChanged -= OnStatusChanged;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        Close();
        return true;
    }

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    private void OnSearchCompleted(object? sender, EventArgs e) =>
        _ = _vm.RunSelectedAsync();

    /// <summary>
    /// Enter and Space on the list run the command. Space in the search
    /// field has to stay a space, so this declines while the entry has
    /// focus; Enter from the entry is SearchEntry.Completed.
    /// </summary>
    private bool TryRunFromList()
    {
        if (SearchEntry.IsFocused)
        {
            return false;
        }

        _ = _vm.RunSelectedAsync();
        return true;
    }

    private bool Close()
    {
        if (AppNavigator.GoBack())
        {
            return true;
        }

        return false;
    }
}
