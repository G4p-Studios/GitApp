using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class ReleasesPage : ContentPage
{
    private readonly GitHubRepository _listed;
    private readonly ReleasesViewModel _vm;
    private bool _initialised;

    public ReleasesPage(GitHubSession session, GitHubRepository listed)
    {
        InitializeComponent();

        _listed = listed;
        _vm = new ReleasesViewModel(session, listed);
        BindingContext = _vm;

        ListPane.EntryControl = ReleaseList;
        DetailsPane.EntryControl = DetailsTitleLabel;
        FormPane.EntryControl = TagEntry;

        PlatformFocus.TrackFocusWithin(ReleaseList, "releases-list");
        PlatformFocus.TrackFocusWithin(TagEntry, "releases-new");
        PlatformFocus.TrackFocusWithin(NotesEditor, "releases-new");
        PlatformFocus.DescribeEmptyView(ReleaseList, "No releases yet");

        NotesDocument.LinkActivated += OnLink;
        _vm.SelectionChanged += (_, _) => Dispatcher.Dispatch(RenderDetails);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = HandleBack;
        PaneNavigation.SubmitHandler = HandleSubmit;

        PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);

        if (_initialised)
        {
            return;
        }

        _initialised = true;
        _ = _vm.InitialiseAsync();
    }

    protected override void OnDisappearing()
    {
        Announcer.Current.StatusChanged -= OnStatusChanged;
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        HandleBack();
        return true;
    }

    private void OnBack(object? sender, EventArgs e) => HandleBack();

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    /// <summary>
    /// Control+Enter publishes from inside the form. Only from the notes
    /// editor and the tag field: from the list it would publish something
    /// the user is not looking at.
    /// </summary>
    private bool HandleSubmit()
    {
        if (!NotesEditor.IsFocused && !TagEntry.IsFocused)
        {
            return false;
        }

        _vm.PublishCommand.Execute(null);
        return true;
    }

    private bool HandleBack()
    {
        // A half-written release is not thrown away on Escape without asking.
        if (_vm.HasUnsavedForm)
        {
            _ = ConfirmLeaveAsync();
            return true;
        }

        return GoBackToRepository();
    }

    private async Task ConfirmLeaveAsync()
    {
        var leave = await DisplayAlertAsync(
            "Unfinished release",
            "The new release has not been published. Leave without publishing it?",
            "Leave",
            "Stay");

        if (leave)
        {
            GoBackToRepository();
        }
    }

    private bool GoBackToRepository()
    {
        if (AppNavigator.GoBack())
        {
            Announcer.Current.Announce(_listed.FullName);
            return true;
        }

        return false;
    }

    private void RenderDetails()
    {
        AssetsHost.Children.Clear();

        if (_vm.HasNotes)
        {
            // Named after the release, not shown in the text: the heading
            // above the document already says it once.
            NotesDocument.Title = $"{_vm.DetailsTitle} release notes";
            NotesDocument.ShowTitleInDocument = false;
            NotesDocument.HeadingOffset = 1;
            NotesDocument.BaseUri = _vm.DocumentBaseUri;
            NotesDocument.Blocks = _vm.NotesBlocks;
        }

        if (_vm.SelectedRelease is { } release)
        {
            foreach (var asset in release.Assets)
            {
                var label = new Label { Text = asset.AccessibleName, FontSize = 14 };
                SemanticProperties.SetDescription(label, asset.AccessibleName);
                AssetsHost.Children.Add(label);
            }
        }
    }

    private async void OnLink(object? sender, string url) =>
        await MarkdownRenderer.OpenLinkAsync(url);
}
