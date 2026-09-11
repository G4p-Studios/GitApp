using System.Windows.Input;
using GitApp.Accessibility;
using GitApp.Services;

namespace GitApp.ViewModels;

/// <summary>
/// An ICommand over an async operation, with re-entrancy guarded.
///
/// The guard matters here beyond the usual reason: a second Commit while the
/// first is still running would announce two overlapping start messages and
/// leave the user unable to tell which result belonged to which action.
/// </summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute();
        }
        catch (Exception ex)
        {
            CommandFailure.Report(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// What to do when a command throws.
///
/// Before this existed, an unexpected exception left the app completely
/// silent: the start of the operation had been announced, nothing followed,
/// and there was no way to tell a failure from a slow network or a hang.
/// Silence is the one outcome a screen reader user cannot interpret, so
/// every command now ends in something spoken, even the paths nobody
/// anticipated.
/// </summary>
public static class CommandFailure
{
    /// <summary>Test seam.</summary>
    public static Action<Exception>? Handler { get; set; }

    public static void Report(Exception ex)
    {
        if (Handler is not null)
        {
            Handler(ex);
            return;
        }

        // Redacted, because this is the generic path: whatever the
        // exception is carrying ends up spoken aloud, and a credential
        // could be in it.
        Announcer.Current.Announce(
            $"Something went wrong: {Redaction.Apply(ex.Message)}", Urgency.Assertive);

        Announcer.Current.SetStatus(
            $"{ex.GetType().Name}: {Redaction.Apply(ex.Message)}");
    }
}

/// <summary>Typed variant, for per-row commands.</summary>
public sealed class AsyncCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    private bool _running;

    public AsyncCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) =>
        !_running && (_canExecute?.Invoke(Cast(parameter)) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _running = true;
        RaiseCanExecuteChanged();

        try
        {
            await _execute(Cast(parameter));
        }
        catch (Exception ex)
        {
            CommandFailure.Report(ex);
        }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() =>
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    private static T? Cast(object? parameter) => parameter is T typed ? typed : default;
}
