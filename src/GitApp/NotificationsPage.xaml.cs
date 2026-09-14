using GitApp.Accessibility;
using GitApp.Services;
using GitApp.ViewModels;

namespace GitApp;

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationsViewModel _vm;

    public NotificationsPage(NotificationService service)
    {
        InitializeComponent();

        _vm = new NotificationsViewModel(service);
        BindingContext = _vm;

        InboxPane.EntryControl = InboxList;

        PlatformFocus.TrackFocusWithin(InboxList, "notifications-inbox");
        PlatformFocus.DescribeEmptyView(InboxList, "No unread notifications");
        PlatformFocus.SetLocalizedRole(InboxHeading, "heading");

        _vm.ParkFocus += (_, _) => PlatformFocus.TryFocusElement(InboxHeading);
        _vm.FocusNeeded += (_, _) => RestoreListFocus();
    }

    private void RestoreListFocus()
    {
        // Push the new selection into the CollectionView before focusing it.
        // The two-way binding can lag a tick, and TryFocusSelectedItem looks
        // at the platform IsSelected flag, not the view-model property.
        InboxList.SelectedItem = _vm.Selected;

        if (_vm.Selected is not null)
        {
            PlatformFocus.TryFocusSelectedItem(InboxList);
            return;
        }

        PlatformFocus.TryFocusFirstDescendant(InboxList);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        Announcer.Current.StatusChanged -= OnStatusChanged;
        Announcer.Current.StatusChanged += OnStatusChanged;

        PaneNavigation.Attach(this);
        PaneNavigation.BackHandler = HandleBack;

        PaneNavigation.FocusFirstPaneWhenReady(this, announce: true);
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

    /// <summary>
    /// Select and focus the row for a thread, so activating a toast lands the
    /// user on the notification it was about rather than on the list top.
    /// </summary>
    public void FocusThread(string threadId)
    {
        if (_vm.Find(threadId) is not { } notification)
        {
            return;
        }

        _vm.Selected = notification;
        Dispatcher.Dispatch(() => PlatformFocus.TryFocusSelectedItem(InboxList));
    }

    private void OnBack(object? sender, EventArgs e) => HandleBack();

    private void OnStatusChanged(object? sender, string text) =>
        Dispatcher.Dispatch(() => StatusLabel.Text = text);

    private bool HandleBack()
    {
        return AppNavigator.GoBack();
    }
}
