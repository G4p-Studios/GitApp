// gitapp.cpp : Defines the entry point for the application.
//

#include "pch.h"
#include "gitapp.h"

#include "AutolinkedNativeModules.g.h"

#include "NativeModules.h"

#include <dwmapi.h>
#include <winrt/Windows.UI.ViewManagement.h>

#pragma comment(lib, "dwmapi.lib")

// True when the user's app theme is dark.
//
// UISettings reports the theme's background colour, which is near-black in
// dark mode and near-white in light mode. This is the same signal RNW's
// Appearance module uses, so the title bar and the JS theme cannot disagree.
static bool IsSystemDarkTheme() noexcept {
  try {
    winrt::Windows::UI::ViewManagement::UISettings settings;
    auto background{settings.GetColorValue(winrt::Windows::UI::ViewManagement::UIColorType::Background)};
    // Perceived luminance; anything below the midpoint is a dark theme.
    auto luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B);
    return luminance < 128.0;
  } catch (...) {
    return false;
  }
}

// Ask DWM to draw the standard title bar in its dark variant.
//
// Without this the window keeps a light title bar above a dark app, which is
// the single most obvious way a Windows app looks unfinished. Using the DWM
// attribute rather than custom title bar colours means we get exactly the
// same title bar as every other Windows 11 app, including its hover and
// inactive states.
static void ApplyTitleBarTheme(winrt::Microsoft::UI::Windowing::AppWindow const &appWindow) noexcept {
  auto hwnd{winrt::Microsoft::UI::GetWindowFromWindowId(appWindow.Id())};
  if (!hwnd) {
    return;
  }

  BOOL useDark = IsSystemDarkTheme() ? TRUE : FALSE;
  // DWMWA_USE_IMMERSIVE_DARK_MODE. Ignored on builds that predate it, so
  // there is no version check to keep in sync.
  DwmSetWindowAttribute(hwnd, 20, &useDark, sizeof(useDark));
}

// A PackageProvider containing any turbo modules you define within this app project
struct CompReactPackageProvider
    : winrt::implements<CompReactPackageProvider, winrt::Microsoft::ReactNative::IReactPackageProvider> {
 public: // IReactPackageProvider
  void CreatePackage(winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept {
    AddAttributedModules(packageBuilder, true);
  }
};

// The entry point of the Win32 application
_Use_decl_annotations_ int CALLBACK WinMain(HINSTANCE instance, HINSTANCE, PSTR /* commandLine */, int showCmd) {
  // Initialize WinRT
  winrt::init_apartment(winrt::apartment_type::single_threaded);

  // Enable per monitor DPI scaling
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

  // Find the path hosting the app exe file
  WCHAR appDirectory[MAX_PATH];
  GetModuleFileNameW(NULL, appDirectory, MAX_PATH);
  PathCchRemoveFileSpec(appDirectory, MAX_PATH);

  // Create a ReactNativeWin32App with the ReactNativeAppBuilder
  auto reactNativeWin32App{winrt::Microsoft::ReactNative::ReactNativeAppBuilder().Build()};

  // Configure the initial InstanceSettings for the app's ReactNativeHost
  auto settings{reactNativeWin32App.ReactNativeHost().InstanceSettings()};
  // Register any autolinked native modules
  RegisterAutolinkedNativeModulePackages(settings.PackageProviders());
  // Register any native modules defined within this app project
  settings.PackageProviders().Append(winrt::make<CompReactPackageProvider>());

#if BUNDLE
  // Load the JS bundle from a file (not Metro):
  // Set the path (on disk) where the .bundle file is located
  settings.BundleRootPath(std::wstring(L"file://").append(appDirectory).append(L"\\Bundle\\").c_str());
  // Set the name of the bundle file (without the .bundle extension)
  settings.JavaScriptBundleFile(L"index.windows");
  // Disable hot reload
  settings.UseFastRefresh(false);
#else
  // Load the JS bundle from Metro
  settings.JavaScriptBundleFile(L"index");
  // Enable hot reload
  settings.UseFastRefresh(true);
#endif
#if _DEBUG
  // For Debug builds
  // Enable Direct Debugging of JS
  settings.UseDirectDebugger(true);
  // Enable the Developer Menu
  settings.UseDeveloperSupport(true);
#else
  // For Release builds:
  // Disable Direct Debugging of JS
  settings.UseDirectDebugger(false);
  // Disable the Developer Menu
  settings.UseDeveloperSupport(false);
#endif

  // Get the AppWindow so we can configure its initial title and size
  auto appWindow{reactNativeWin32App.AppWindow()};
  appWindow.Title(L"GitApp");
  appWindow.Resize({1000, 1000});
  ApplyTitleBarTheme(appWindow);

  // Get the ReactViewOptions so we can set the initial RN component to load
  auto viewOptions{reactNativeWin32App.ReactViewOptions()};
  viewOptions.ComponentName(L"GitApp");

  // Start the app
  reactNativeWin32App.Start();
}
