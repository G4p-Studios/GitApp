using System.Collections.ObjectModel;
using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Domain;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// Control+Shift+P: find a command by typing, then Enter. The list is the
/// whole catalogue when the query is empty, so it is immediately arrowable
/// the way VS Code and Quill are.
/// </summary>
public sealed class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteItem> _all;
    private readonly Announcer _announcer;
    private string _query = string.Empty;
    private string _heading = "Commands";
    private PaletteItem? _selected;

    public CommandPaletteViewModel(Announcer? announcer = null)
    {
        _announcer = announcer ?? Announcer.Current;
        _all = Build();
        Items = new ObservableCollection<PaletteItem>(_all);
        _selected = Items.Count > 0 ? Items[0] : null;
        Heading = HeadingFor(Items.Count);
        RunCommand = new AsyncCommand<PaletteItem>(RunAsync);
    }

    public ObservableCollection<PaletteItem> Items { get; }

    public ICommand RunCommand { get; }

    public string Query
    {
        get => _query;
        set
        {
            if (!Set(ref _query, value))
            {
                return;
            }

            Apply();
        }
    }

    public string Heading
    {
        get => _heading;
        private set => Set(ref _heading, value);
    }

    public PaletteItem? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

    public async Task<bool> RunSelectedAsync()
    {
        var item = Selected ?? Items.FirstOrDefault();
        if (item is null)
        {
            _announcer.Announce("No matching commands");
            return true;
        }

        await RunAsync(item);
        return true;
    }

    private void Apply()
    {
        var matched = PaletteFilter.Match(
            _all.Select(i => i.Command).ToList(), Query);
        var allowed = new HashSet<string>(
            matched.Select(c => c.Title + "\n" + c.Category), StringComparer.Ordinal);

        Items.Clear();
        foreach (var item in _all)
        {
            if (allowed.Contains(item.Command.Title + "\n" + item.Command.Category))
            {
                Items.Add(item);
            }
        }

        Selected = Items.Count > 0 ? Items[0] : null;
        Heading = HeadingFor(Items.Count);
        // The heading is on screen. Announcing it on every letter stacks
        // on the character the search field already speaks.
    }

    private async Task RunAsync(PaletteItem? item)
    {
        if (item is null)
        {
            return;
        }

        AppHost.ToggleCommandPalette();
        await item.Run();
        AppHost.Home?.RefreshSession();
    }

    private static string HeadingFor(int count) => count switch
    {
        0 => "No matching commands",
        1 => "1 command",
        _ => $"{count} commands",
    };

    private static IReadOnlyList<PaletteItem> Build()
    {
        return new[]
        {
            Item("Home", "Go", () => { AppHost.ShowHome(); return Task.CompletedTask; }),
            Item("Local repositories", "Go", () => { AppHost.ShowLocal(); return Task.CompletedTask; }),
            Item("Your GitHub repositories", "Go", () => { AppHost.ShowGitHub(); return Task.CompletedTask; }),
            Item("Notifications", "Go", () => { AppHost.ShowNotifications(); return Task.CompletedTask; }),
            Item("Add a local repository", "Local", () => Invoke(AppHost.Main.AddRepositoryCommand)),
            Item("Clone a repository", "Local", () => Invoke(AppHost.Main.CloneCommand)),
            Item("Fetch", "Local", () => Invoke(AppHost.Main.FetchCommand)),
            Item("Pull", "Local", () => Invoke(AppHost.Main.PullCommand)),
            Item("Push", "Local", () => Invoke(AppHost.Main.PushCommand)),
            Item("Refresh the current repository", "Local", () => Invoke(AppHost.Main.RefreshCommand)),
            Item("Sign out of GitHub", "Account", async () =>
            {
                await AppHost.Session.SignOutAsync();
                Announcer.Current.Announce("Signed out of GitHub");
                AppHost.ShowHome();
            }),
        };
    }

    private static PaletteItem Item(string title, string category, Func<Task> run) =>
        new(new PaletteCommand(title, category), run);

    private static Task Invoke(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
        else
        {
            Announcer.Current.Announce("That command is not available right now");
        }

        return Task.CompletedTask;
    }
}

public sealed record PaletteItem(PaletteCommand Command, Func<Task> Run)
{
    public string Title => Command.Title;

    public string Category => Command.Category;

    public string AccessibleName => Command.AccessibleName;
}
