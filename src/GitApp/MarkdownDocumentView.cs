using GitApp.Domain;

namespace GitApp;

/// <summary>
/// A Markdown body as one native document: arrow keys move a caret, and
/// links are hyperlinks in the sentence they belong to.
///
/// MAUI has no portable document control. On Windows this maps to a
/// read-only RichEditBox, which is the same shape as Notepad or WordPad:
/// one tab stop, caret reading, F6 still reaches the page because it is
/// in the XAML tree rather than an HWND island. See
/// docs/REPOSITORY-VIEW.md.
/// </summary>
public class MarkdownDocumentView : View
{
    public static readonly BindableProperty BlocksProperty =
        BindableProperty.Create(
            nameof(Blocks),
            typeof(IReadOnlyList<ReadmeBlock>),
            typeof(MarkdownDocumentView),
            Array.Empty<ReadmeBlock>(),
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(
            nameof(Title),
            typeof(string),
            typeof(MarkdownDocumentView),
            default(string),
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty HeadingOffsetProperty =
        BindableProperty.Create(
            nameof(HeadingOffset),
            typeof(int),
            typeof(MarkdownDocumentView),
            0,
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty BaseUriProperty =
        BindableProperty.Create(
            nameof(BaseUri),
            typeof(string),
            typeof(MarkdownDocumentView),
            default(string));

    public static readonly BindableProperty ShowTitleInDocumentProperty =
        BindableProperty.Create(
            nameof(ShowTitleInDocument),
            typeof(bool),
            typeof(MarkdownDocumentView),
            false,
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty FillPaneProperty =
        BindableProperty.Create(
            nameof(FillPane),
            typeof(bool),
            typeof(MarkdownDocumentView),
            true,
            propertyChanged: OnDocumentChanged);

    public IReadOnlyList<ReadmeBlock> Blocks
    {
        get => (IReadOnlyList<ReadmeBlock>)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int HeadingOffset
    {
        get => (int)GetValue(HeadingOffsetProperty);
        set => SetValue(HeadingOffsetProperty, value);
    }

    public string? BaseUri
    {
        get => (string?)GetValue(BaseUriProperty);
        set => SetValue(BaseUriProperty, value);
    }

    /// <summary>
    /// When true, <see cref="Title"/> is also the first heading in the
    /// document so F6 lands inside it and arrows start at the name.
    /// When false, Title is only the control's accessible name, because
    /// a heading already sits above the document.
    /// </summary>
    public bool ShowTitleInDocument
    {
        get => (bool)GetValue(ShowTitleInDocumentProperty);
        set => SetValue(ShowTitleInDocumentProperty, value);
    }

    /// <summary>
    /// True when this is the pane's filling document (readme, markdown
    /// file). False when it sits in a stack of comments and should size
    /// to its text rather than steal the page's scrollbar.
    /// </summary>
    public bool FillPane
    {
        get => (bool)GetValue(FillPaneProperty);
        set => SetValue(FillPaneProperty, value);
    }

    public event EventHandler<string>? LinkActivated;

    public MarkdownDocumentLayout Document { get; private set; } =
        new(string.Empty, Array.Empty<MarkdownRange>());

    internal void RaiseLink(string url)
    {
        var resolved = MarkdownDocument.ResolveUrl(url, BaseUri);
        if (string.IsNullOrEmpty(resolved))
        {
            return;
        }

        LinkActivated?.Invoke(this, resolved);
    }

    private static void OnDocumentChanged(BindableObject bindable, object? old, object? value)
    {
        if (bindable is MarkdownDocumentView view)
        {
            view.Rebuild();
        }
    }

    private void Rebuild()
    {
        Document = MarkdownDocument.Layout(
            Blocks,
            ShowTitleInDocument ? Title : null,
            HeadingOffset);

        if (FillPane)
        {
            HeightRequest = -1;
            VerticalOptions = LayoutOptions.Fill;
            return;
        }

        var lines = 1;
        foreach (var c in Document.Text)
        {
            if (c == '\r')
            {
                lines++;
            }
        }

        HeightRequest = Math.Clamp((lines * 22) + 12, 72, 640);
        VerticalOptions = LayoutOptions.Start;
    }
}
