using GitApp.Domain;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinAutomation = Microsoft.UI.Xaml.Automation.AutomationProperties;

namespace GitApp.Platforms.Windows;

/// <summary>
/// Maps <see cref="MarkdownDocumentView"/> to a read-only RichEditBox.
///
/// Labels are not a document: they are not in the tab order and arrow keys
/// cannot move through them. A WebView would give browse-mode reading, and
/// would also eat F6 because WebView2 is an HWND island outside the XAML
/// preview-key tunnel. RichEditBox is in that tree, so F6 still cycles
/// panes, and it is the same caret-reading shape as Notepad. Hyperlinks
/// stay in the sentence via <c>ITextRange.Link</c>; they are not buttons.
///
/// Setting <c>Link</c> on a relative URL or a zero-length range is a
/// known WinUI crash. Ranges are clamped, relative destinations are
/// resolved, and a failure to format one run must not take down the page.
/// </summary>
public sealed class MarkdownDocumentViewHandler
    : ViewHandler<MarkdownDocumentView, RichEditBox>
{
    public static IPropertyMapper<MarkdownDocumentView, MarkdownDocumentViewHandler> Mapper =
        new PropertyMapper<MarkdownDocumentView, MarkdownDocumentViewHandler>(ViewMapper)
        {
            [nameof(MarkdownDocumentView.Blocks)] = MapDocument,
            [nameof(MarkdownDocumentView.Title)] = MapDocument,
            [nameof(MarkdownDocumentView.HeadingOffset)] = MapDocument,
            [nameof(MarkdownDocumentView.ShowTitleInDocument)] = MapDocument,
        };

    public MarkdownDocumentViewHandler() : base(Mapper)
    {
    }

    protected override RichEditBox CreatePlatformView()
    {
        var box = new RichEditBox
        {
            IsReadOnly = true,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            AcceptsReturn = true,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            BorderThickness = new Microsoft.UI.Xaml.Thickness(0),
            Padding = new Microsoft.UI.Xaml.Thickness(0),
        };

        box.PreviewKeyDown += OnPreviewKeyDown;
        return box;
    }

    protected override void ConnectHandler(RichEditBox platformView)
    {
        base.ConnectHandler(platformView);
        MapDocument(this, VirtualView);
    }

    protected override void DisconnectHandler(RichEditBox platformView)
    {
        platformView.PreviewKeyDown -= OnPreviewKeyDown;
        base.DisconnectHandler(platformView);
    }

    private static void MapDocument(MarkdownDocumentViewHandler handler, MarkdownDocumentView view)
    {
        if (handler.PlatformView is not { } box)
        {
            return;
        }

        try
        {
            WriteDocument(box, view);
        }
        catch
        {
            // A formatting failure must not close the repository. The
            // plain text is already in the control if SetText succeeded.
        }
    }

    private static void WriteDocument(RichEditBox box, MarkdownDocumentView view)
    {
        var layout = MarkdownDocument.Layout(
            view.Blocks,
            view.ShowTitleInDocument ? view.Title : null,
            view.HeadingOffset);
        var name = string.IsNullOrWhiteSpace(view.Title) ? "Document" : view.Title!;
        WinAutomation.SetName(box, name);

        box.IsReadOnly = false;
        box.Document.SetText(TextSetOptions.None, layout.Text ?? string.Empty);
        box.Document.GetText(TextGetOptions.None, out var story);
        var storyEnd = Math.Max(0, story.Length);

        foreach (var range in layout.Ranges)
        {
            ApplyRange(box, range, view.BaseUri, storyEnd, links: false);
        }

        foreach (var range in layout.Ranges)
        {
            ApplyRange(box, range, view.BaseUri, storyEnd, links: true);
        }

        box.Document.Selection.SetRange(0, 0);
        box.IsReadOnly = true;
    }

    private static void ApplyRange(
        RichEditBox box,
        MarkdownRange range,
        string? baseUri,
        int storyEnd,
        bool links)
    {
        var isLink = range.Kind == MarkdownRangeKind.Link;
        if (isLink != links)
        {
            return;
        }

        var start = range.Start;
        var end = range.Start + range.Length;
        if (start < 0 || end <= start || start >= storyEnd)
        {
            return;
        }

        end = Math.Min(end, storyEnd);

        try
        {
            var run = box.Document.GetRange(start, end);
            if (run.StartPosition >= run.EndPosition)
            {
                return;
            }

            switch (range.Kind)
            {
                case MarkdownRangeKind.Heading:
                    run.CharacterFormat.Bold = FormatEffect.On;
                    run.CharacterFormat.Size = HeadingSize(range.Level);
                    break;

                case MarkdownRangeKind.Code:
                    run.CharacterFormat.Name = "Consolas";
                    run.CharacterFormat.Size = 13;
                    break;

                case MarkdownRangeKind.Link:
                    if (MarkdownDocument.TryHyperlink(range.Url, baseUri, out var url))
                    {
                        run.Link = "\"" + url.Replace("\"", string.Empty) + "\"";
                    }

                    break;
            }
        }
        catch
        {
            // One bad run is not worth the repository screen.
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || VirtualView is null || PlatformView is null)
        {
            return;
        }

        try
        {
            var link = PlatformView.Document.Selection.Link;
            if (string.IsNullOrEmpty(link))
            {
                return;
            }

            e.Handled = true;
            VirtualView.RaiseLink(link.Trim().Trim('"'));
        }
        catch
        {
            // Reading the caret's link is best-effort.
        }
    }

    private static float HeadingSize(int level) => level switch
    {
        1 => 22,
        2 => 18,
        3 => 16,
        _ => 14,
    };
}
