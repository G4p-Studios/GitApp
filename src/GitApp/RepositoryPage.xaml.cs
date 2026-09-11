using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class RepositoryPage : ContentPage
{
    private readonly GitHubSession _session;
    private readonly GitHubRepository _listed;
    private readonly RepositoryViewModel _vm;
    private WorkListPage? _issuesPage;
    private WorkListPage? _pullsPage;
    private ReleasesPage? _releasesPage;
    private bool _initialised;

    public RepositoryPage(GitHubSession session, GitHubRepository listed)
    {
        InitializeComponent();

        _session = session;
        _listed = listed;
        _vm = new RepositoryViewModel(session, listed);
        BindingContext = _vm;

        FilesPane.EntryControl = FileList;
        ReadmePane.EntryControl = ReadmeHeadingLabel;
        AboutPane.EntryControl = AboutHeading;

        PlatformFocus.TrackFocusWithin(FileList, "repo-files");
        PlatformFocus.TrackFocusWithin(BranchPicker, "repo-files");
        PlatformFocus.DescribeEmptyView(FileList, "This folder is empty");

        ReadmeDocument.LinkActivated += OnReadmeLink;

        _vm.PickFolder = FolderPicker.PickAsync;
        _vm.ReadmeChanged += (_, _) => Dispatcher.Dispatch(() =>
        {
            RenderReadme();
            RenderAbout();

            // Folder navigation rebuilds the list. Put focus on a real row
            // if the user was already in Files; otherwise leave them where
            // they F6'd to while it loaded.
            if (FocusManager.Current.ActivePane?.Id is null or "repo-files")
            {
                PlatformFocus.TryFocusSelectedItem(FileList);
            }
        });
        _vm.Cloned += (_, path) => Cloned?.Invoke(this, path);
        _vm.OpenFileRequested += (_, entry) =>
            AppNavigator.Show(new FilePage(_session, _listed, entry, _vm.CurrentBranch));
    }

    /// <summary>Raised so a clone started here lands on the local list.</summary>
    public event EventHandler<string>? Cloned;

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = HandleBack;
        PaneNavigation.ActivateHandler = TryActivate;
        PaneNavigation.RowExpander = HandleTreeKey;

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
    /// Escape backs out one level: a folder, then the repository list.
    /// </summary>
    private bool HandleBack()
    {
        if (_vm.TryGoUp())
        {
            return true;
        }

        if (AppNavigator.GoBack())
        {
            Announcer.Current.Announce("GitHub repositories");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Enter or Space opens the selected file-table row — a folder, a
    /// file, or a spoken note for a submodule — but only when the file
    /// list itself has focus. The branch picker and the other panes keep
    /// the key.
    /// </summary>
    private bool TryActivate() =>
        KeysAreForFileList() && _vm.TryOpenSelected();

    /// <summary>
    /// Right opens a folder, Left goes up, matching a tree. Enter and Space
    /// are handled by <see cref="TryActivate"/> so they do not also fold.
    /// </summary>
    private bool HandleTreeKey(bool? open)
    {
        if (!KeysAreForFileList())
        {
            return false;
        }

        if (open is false)
        {
            return _vm.TryGoUp();
        }

        if (open is true)
        {
            return _vm.SelectedEntry?.IsFolder == true && _vm.TryOpenSelected();
        }

        return false;
    }

    private bool KeysAreForFileList() =>
        FocusManager.Current.ActivePane?.Id == "repo-files"
        && !BranchPicker.IsFocused
        && !BackButton.IsFocused
        && !CloneButton.IsFocused
        && !OpenGitHubButton.IsFocused
        && !IssuesButton.IsFocused
        && !PullsButton.IsFocused;

    /// <summary>
    /// One native document, not a stack of labels and buttons. Arrow keys
    /// move a caret; links stay in the sentence as hyperlinks. See
    /// docs/REPOSITORY-VIEW.md.
    /// </summary>
    private void RenderReadme()
    {
        if (_vm.NoReadme)
        {
            ReadmePane.EntryControl = ReadmeHeadingLabel;
            return;
        }

        ReadmeDocument.Title = _vm.ReadmeHeading;
        ReadmeDocument.HeadingOffset = 0;
        ReadmeDocument.BaseUri = $"{_listed.HtmlUrl.TrimEnd('/')}/blob/{_vm.CurrentBranch}/";
        ReadmeDocument.Blocks = _vm.ReadmeBlocks;
        ReadmePane.EntryControl = ReadmeDocument;
    }

    /// <summary>
    /// One control per fact. The releases fact is a button, because on
    /// github.com the sidebar's Releases section is where you go to the
    /// releases, and a toolbar button would put the action away from the
    /// count it belongs to. See docs/RELEASES.md.
    /// </summary>
    private void RenderAbout()
    {
        AboutFactsHost.Children.Clear();

        foreach (var fact in _vm.AboutFacts)
        {
            if (fact == _vm.ReleasesFact)
            {
                var button = new Button
                {
                    Text = fact,
                    FontSize = 14,
                    MinimumHeightRequest = 32,
                    HorizontalOptions = LayoutOptions.Start,
                };
                SemanticProperties.SetHint(button, "Opens the releases list");
                button.Clicked += OnOpenReleases;
                AboutFactsHost.Children.Add(button);
                continue;
            }

            var label = new Label { Text = fact, FontSize = 14 };
            SemanticProperties.SetDescription(label, fact);
            AboutFactsHost.Children.Add(label);
        }
    }

    private void OnOpenReleases(object? sender, EventArgs e)
    {
        _releasesPage ??= new ReleasesPage(_session, _listed);
        AppNavigator.Show(_releasesPage);
    }

    private void OnOpenIssues(object? sender, EventArgs e) =>
        ShowWork(GitHubWorkKind.Issue, ref _issuesPage);

    private void OnOpenPulls(object? sender, EventArgs e) =>
        ShowWork(GitHubWorkKind.PullRequest, ref _pullsPage);

    private void ShowWork(GitHubWorkKind kind, ref WorkListPage? page)
    {
        page ??= new WorkListPage(_session, _listed, kind);
        AppNavigator.Show(page);
    }

    private async void OnReadmeLink(object? sender, string url) =>
        await MarkdownRenderer.OpenLinkAsync(url);
}
