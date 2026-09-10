using Microsoft.Extensions.DependencyInjection;

namespace GitApp;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// No Shell: GitApp is a desktop app with panes, not a mobile
		// navigation stack, and Shell adds chrome that only clutters the
		// UI Automation tree.
		return new Window(new MainPage()) { Title = "GitApp" };
	}
}