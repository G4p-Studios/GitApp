using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class FilePage : ContentPage
{
    private readonly FileViewModel _vm;
    private bool _initialised;

    public FilePage(
        GitHubSession session,
        GitHubRepository listed,
        GitHubTreeEntry entry,
        string branch)
    {
        InitializeComponent();

        _vm = new FileViewModel(session, listed, entry, branch);
        BindingContext = _vm;
        Title = entry.Name;

        FilePane.EntryControl = TitleLabel;
        AboutPane.EntryControl = AboutHeading;

        PlatformFocus.TrackFocusWithin(LineList, "file-contents");
        PlatformFocus.DescribeEmptyView(LineList, "This file is empty");

        _vm.ContentsChanged += (_, _) => Dispatcher.Dispatch(Render);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = HandleBack;

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

    private bool HandleBack()
    {
        if (AppNavigator.GoBack())
        {
            Announcer.Current.Announce("Files");
            return true;
        }

        return false;
    }

    private void Render()
    {
        DocumentHost.Children.Clear();
        AboutFactsHost.Children.Clear();

        if (_vm.ShowDocument)
        {
            if (_vm.BodyBlocks.Count == 0)
            {
                var empty = new Label
                {
                    Text = "This file is empty.",
                    FontSize = 14,
                    Opacity = 0.7,
                };
                SemanticProperties.SetDescription(empty, "This file is empty.");
                DocumentHost.Children.Add(empty);
            }
            else
            {
                MarkdownRenderer.AddBlocks(DocumentHost, _vm.BodyBlocks, OnLink, headingOffset: 1);
            }
        }

        foreach (var fact in _vm.AboutFacts)
        {
            var label = new Label { Text = fact, FontSize = 14 };
            SemanticProperties.SetDescription(label, fact);
            AboutFactsHost.Children.Add(label);
        }

        FilePane.EntryControl = _vm.ShowUnavailable ? UnavailableLabel : TitleLabel;
    }

    private async void OnLink(object? sender, EventArgs e) =>
        await MarkdownRenderer.OpenLinkAsync(sender);
}
