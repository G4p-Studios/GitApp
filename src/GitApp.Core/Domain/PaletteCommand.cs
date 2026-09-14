namespace GitApp.Domain;

/// <summary>
/// One row in the command palette. Title first, then where it lives, so a
/// listener scanning "Clone" does not wait through "GitHub" or "Local" on
/// every line (ARCHITECTURE 3.3).
/// </summary>
public sealed record PaletteCommand(string Title, string Category)
{
    public string AccessibleName => $"{Title}, {Category}";
}

/// <summary>
/// Narrow a command list the way VS Code and Quill do: a case-insensitive
/// substring of the title or the category. An empty query is the whole list,
/// so opening the palette is immediately arrowable.
/// </summary>
public static class PaletteFilter
{
    public static IReadOnlyList<PaletteCommand> Match(
        IReadOnlyList<PaletteCommand> commands, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return commands;
        }

        var needle = query.Trim();
        return commands
            .Where(c =>
                c.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || c.Category.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
