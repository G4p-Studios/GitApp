using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GitApp.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base.
///
/// Hand-written rather than taking a dependency on CommunityToolkit.Mvvm:
/// the app needs change notification and nothing else the toolkit offers,
/// and a source generator is a poor trade for twenty lines.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Assign and notify, but only when the value actually changed.
    ///
    /// The guard is an accessibility concern, not a performance one. A
    /// needless notification rebuilds bound controls, which can move focus
    /// and make a screen reader re-announce content the user already heard.
    /// See docs/ARCHITECTURE.md 4.6.
    /// </summary>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Raise(propertyName);
        return true;
    }
}
