using GitApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GitApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();

		// Register the toast channel once, at startup. A no-op off Windows.
		// Activation routing (a toast to the inbox) is wired by MainPage,
		// which owns the notification service.
		ToastModule.Register();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No Shell: GitApp is a desktop app with panes, not a mobile
		// navigation stack, and Shell adds chrome that only clutters the
		// UI Automation tree.
		return new Window(new MainPage()) { Title = "GitApp" };
	}
}