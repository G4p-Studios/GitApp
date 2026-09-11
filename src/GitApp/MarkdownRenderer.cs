using GitApp.Domain;

namespace GitApp;

/// <summary>
/// Native labels and buttons for a Markdown document.
///
/// Shared by the README, markdown files, issue bodies, and comments so a
/// heading in one place is a heading in all of them, and so a code block
/// is never split into one element per character. See
/// docs/REPOSITORY-VIEW.md and docs/ISSUES.md.
/// </summary>
public static class MarkdownRenderer
{
    public static void AddBlocks(
        Layout host,
        IEnumerable<ReadmeBlock> blocks,
        EventHandler? onLink,
        int headingOffset = 0)
    {
        foreach (var block in blocks)
        {
            host.Children.Add(CreateBlock(block, headingOffset));

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
                if (onLink is not null)
                {
                    button.Clicked += onLink;
                }

                host.Children.Add(button);
            }
        }
    }

    public static async Task OpenLinkAsync(object? sender)
    {
        if (sender is not Button { CommandParameter: string url } || string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            await Launcher.Default.OpenAsync(url);
            Accessibility.Announcer.Current.Announce($"Opened {url} in your browser");
        }
        catch (Exception)
        {
            Accessibility.Announcer.Current.Announce("Could not open your browser.");
        }
    }

    private static View CreateBlock(ReadmeBlock block, int headingOffset)
    {
        var label = new Label
        {
            Text = block.Text,
            FontSize = block.Kind == ReadmeBlockKind.Heading
                ? HeadingSize(block.Level)
                : 14,
        };

        SemanticProperties.SetDescription(label, block.AccessibleName);

        if (block.Kind == ReadmeBlockKind.Heading)
        {
            SemanticProperties.SetHeadingLevel(
                label,
                HeadingLevel(Math.Clamp(block.Level + headingOffset, 1, 6)));
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
}
