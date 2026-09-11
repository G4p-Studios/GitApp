using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class RepositoryPage : ContentPage
{
    private readonly RepositoryViewModel _vm;
    private bool _initialised;

    public RepositoryPage(GitHubSession session, GitHubRepository listed)
    {
        InitializeComponent();

        _vm = new RepositoryViewModel(session, listed);
        BindingContext = _vm;

        FilesPane.EntryControl = FileList;
        ReadmePane.EntryControl = ReadmeHeadingLabel;
        AboutPane.EntryControl = AboutHeading;

        PlatformFocus.TrackFocusWithin(FileList, "repo-files");
        PlatformFocus.TrackFocusWithin(BranchPicker, "repo-files");
        PlatformFocus.DescribeEmptyView(FileList, "This folder is empty");

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
    /// Enter or Space opens the selected file-table row, but only when the
    /// file list itself has focus. The branch picker and the other panes
    /// keep the key.
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
        && !OpenGitHubButton.IsFocused;

    /// <summary>
    /// Native labels and buttons, not a web view and not a list. Headings
    /// get a heading level so a screen reader can jump; links become
    /// buttons because MAUI has no portable hyperlink; a code block is one
    /// label so punctuation is not spelled one character at a time.
    /// </summary>
    private void RenderReadme()
    {
        var keepEmpty = ReadmeEmptyLabel;
        ReadmeContent.Children.Clear();

        if (_vm.NoReadme)
        {
            ReadmeContent.Children.Add(keepEmpty);
            keepEmpty.IsVisible = true;
            return;
        }

        keepEmpty.IsVisible = false;

        foreach (var block in _vm.ReadmeBlocks)
        {
            ReadmeContent.Children.Add(CreateBlock(block));

            foreach (var link in block.Links)
            {
                var button = new Button
                {
                    Text = link.Text,
                    FontSize = 13,
                    Padding = new Thickness(0),
                    MinimumHeightRequest = 28,
                    HorizontalOptions = LayoutOptions.Start,
                    CommandParameter = link.Url,
                };
                SemanticProperties.SetDescription(button, link.AccessibleName);
                SemanticProperties.SetHint(button, $"Opens {link.Url}");
                button.Clicked += OnReadmeLink;
                ReadmeContent.Children.Add(button);
            }
        }
    }

    private void RenderAbout()
    {
        AboutFactsHost.Children.Clear();

        foreach (var fact in _vm.AboutFacts)
        {
            var label = new Label { Text = fact, FontSize = 14 };
            SemanticProperties.SetDescription(label, fact);
            AboutFactsHost.Children.Add(label);
        }
    }

    private static View CreateBlock(ReadmeBlock block)
    {
        var label = new Label
        {
            Text = block.Text,
            FontSize = block.Kind == ReadmeBlockKind.Heading ? HeadingSize(block.Level) : 14,
        };

        SemanticProperties.SetDescription(label, block.AccessibleName);

        if (block.Kind == ReadmeBlockKind.Heading)
        {
            SemanticProperties.SetHeadingLevel(label, HeadingLevel(block.Level));
        }

        if (block.Kind == ReadmeBlockKind.Code)
        {
            label.FontFamily = "Consolas";
            label.FontSize = 13;
        }

        return label;
    }

    private static double HeadingSize(int level) => level switch
    {
        1 => 22,
        2 => 18,
        3 => 16,
        _ => 14,
    };

    private static SemanticHeadingLevel HeadingLevel(int level) => level switch
    {
        1 => SemanticHeadingLevel.Level1,
        2 => SemanticHeadingLevel.Level2,
        3 => SemanticHeadingLevel.Level3,
        4 => SemanticHeadingLevel.Level4,
        5 => SemanticHeadingLevel.Level5,
        _ => SemanticHeadingLevel.Level6,
    };

    private async void OnReadmeLink(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string url } || string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            Announcer.Current.Announce($"Opened {url} in your browser");
        }
        catch (Exception)
        {
            Announcer.Current.Announce("Could not open your browser.");
        }
    }
}
