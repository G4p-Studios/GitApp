using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.GitHub;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class WorkDetailPage : ContentPage
{
    private readonly WorkDetailViewModel _vm;
    private bool _initialised;

    public WorkDetailPage(GitHubSession session, GitHubRepository listed, GitHubWorkItem item)
    {
        InitializeComponent();

        _vm = new WorkDetailViewModel(session, listed, item);
        BindingContext = _vm;
        Title = $"{item.KindWord} {item.Number}";

        ConversationPane.EntryControl = TitleLabel;
        AboutPane.EntryControl = AboutHeading;

        _vm.ConversationChanged += (_, _) => Dispatcher.Dispatch(Render);
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
            Announcer.Current.Announce(_vm.Kind == GitHubWorkKind.PullRequest
                ? "Pull requests"
                : "Issues");
            return true;
        }

        return false;
    }

    private void Render()
    {
        BodyHost.Children.Clear();
        CommentsHost.Children.Clear();
        AboutFactsHost.Children.Clear();

        if (_vm.BodyBlocks.Count == 0)
        {
            var empty = new Label
            {
                Text = "No description.",
                FontSize = 14,
                Opacity = 0.7,
            };
            SemanticProperties.SetDescription(empty, "No description.");
            BodyHost.Children.Add(empty);
        }
        else
        {
            BodyHost.Children.Add(CreateDocument(_vm.BodyBlocks, headingOffset: 1));
        }

        if (_vm.Detail is { } detail)
        {
            foreach (var comment in detail.Comments)
            {
                var heading = new Label
                {
                    Text = comment.Heading,
                    FontSize = 16,
                };
                SemanticProperties.SetDescription(heading, comment.Heading);
                SemanticProperties.SetHeadingLevel(heading, SemanticHeadingLevel.Level3);
                CommentsHost.Children.Add(heading);

                var blocks = ReadmeDocument.Parse(comment.BodyMarkdown);
                if (blocks.Count == 0)
                {
                    var blank = new Label { Text = "Empty comment.", FontSize = 14, Opacity = 0.7 };
                    SemanticProperties.SetDescription(blank, "Empty comment.");
                    CommentsHost.Children.Add(blank);
                }
                else
                {
                    CommentsHost.Children.Add(CreateDocument(blocks, headingOffset: 2));
                }
            }

            foreach (var fact in detail.AboutFacts)
            {
                var label = new Label { Text = fact, FontSize = 14 };
                SemanticProperties.SetDescription(label, fact);
                AboutFactsHost.Children.Add(label);
            }
        }
    }

    private MarkdownDocumentView CreateDocument(IReadOnlyList<ReadmeBlock> blocks, int headingOffset)
    {
        var view = new MarkdownDocumentView
        {
            FillPane = false,
            HeadingOffset = headingOffset,
            BaseUri = _vm.DocumentBaseUri,
            Blocks = blocks,
        };
        view.LinkActivated += OnLink;
        return view;
    }

    private async void OnLink(object? sender, string url) =>
        await MarkdownRenderer.OpenLinkAsync(url);
}
