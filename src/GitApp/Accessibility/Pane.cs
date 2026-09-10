namespace GitApp.Accessibility;

/// <summary>
/// One region of a screen, reachable with F6.
///
/// MAUI exposes no landmark concept and Windows has no built-in region
/// navigation, so F6 cycling is ours to implement. Every screen in GitApp is
/// composed of panes; a screen without them is a bug.
///
/// The pane renders as a named group in the UI Automation tree, which is what
/// a screen reader reads on entry. Verified in docs/SPIKE-MAUI.md: the group
/// name is announced ahead of the focused control.
///
/// See docs/ARCHITECTURE.md section 3.4.
/// </summary>
public class Pane : ContentView
{
    public static readonly BindableProperty PaneIdProperty =
        BindableProperty.Create(nameof(PaneId), typeof(string), typeof(Pane), string.Empty);

    public static readonly BindableProperty PaneNameProperty =
        BindableProperty.Create(nameof(PaneName), typeof(string), typeof(Pane), string.Empty);

    public static readonly BindableProperty PaneOrderProperty =
        BindableProperty.Create(nameof(PaneOrder), typeof(int), typeof(Pane), 0);

    private IDisposable? _registration;

    public Pane()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Stable identifier, used by menu commands that jump to a pane.</summary>
    public string PaneId
    {
        get => (string)GetValue(PaneIdProperty);
        set => SetValue(PaneIdProperty, value);
    }

    /// <summary>Spoken on entry. A noun, not a sentence.</summary>
    public string PaneName
    {
        get => (string)GetValue(PaneNameProperty);
        set => SetValue(PaneNameProperty, value);
    }

    /// <summary>Cycle position. Leave gaps (10, 20, 30) for later insertions.</summary>
    public int PaneOrder
    {
        get => (int)GetValue(PaneOrderProperty);
        set => SetValue(PaneOrderProperty, value);
    }

    /// <summary>
    /// The control that should receive focus when the pane is entered.
    /// Set this to the pane's list or first meaningful control; otherwise
    /// entering the pane lands on the wrapper and the user has to Tab again.
    /// </summary>
    public VisualElement? EntryControl
    {
        get => _entry;
        set
        {
            _entry = value;
            if (!string.IsNullOrEmpty(PaneId))
            {
                FocusManager.Current.SetPaneEntry(PaneId, value);
            }
        }
    }

    private VisualElement? _entry;

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(PaneId))
        {
            throw new InvalidOperationException("Pane requires a PaneId.");
        }

        // The group name is what a screen reader announces on entry.
        SemanticProperties.SetDescription(this, PaneName);

        _registration = FocusManager.Current.RegisterPane(PaneId, PaneName, PaneOrder, this);

        if (_entry is not null)
        {
            FocusManager.Current.SetPaneEntry(PaneId, _entry);
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _registration?.Dispose();
        _registration = null;
    }
}
