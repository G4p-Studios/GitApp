using Windows.Storage.Pickers;
using WinRT.Interop;

namespace GitApp.Services;

public static partial class FolderPicker
{
    static partial void PickPlatform(ref Task<string?>? result)
    {
        result = PickWindowsAsync();
    }

    private static async Task<string?> PickWindowsAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };

            // The picker refuses to open without at least one filter, even
            // though it selects folders rather than files.
            picker.FileTypeFilter.Add("*");

            // A WinUI picker must be told which window owns it. Without this
            // it throws, and in a packaged app it would appear detached from
            // the window, which is disorienting with a screen reader.
            var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                as Microsoft.UI.Xaml.Window;

            if (window is not null)
            {
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window));
            }

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception)
        {
            // Cancelled, or the picker could not be shown. Either way there
            // is no folder, and the caller announces nothing rather than an
            // error the user did not cause.
            return null;
        }
    }
}
