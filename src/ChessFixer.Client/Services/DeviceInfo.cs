using Microsoft.JSInterop;

namespace ChessFixer.Client.Services;

/// <summary>What kind of device the app runs on, asked once from the browser (by the main layout, at startup).</summary>
public sealed class DeviceInfo(IJSRuntime js)
{
    /// <summary>
    /// True on a phone or tablet (a touchscreen is the main input). There, focusing a text field opens the on-screen
    /// keyboard over half the screen, so drawers and dialogs do not focus a field automatically.
    /// False until <see cref="InitializeAsync"/> has run.
    /// </summary>
    public bool IsTouch { get; private set; }

    /// <summary>Use as AutoFocus of the first field of a drawer or dialog: only with a mouse and keyboard.</summary>
    public bool AutoFocus => !IsTouch;

    public async Task InitializeAsync()
    {
        try
        {
            IsTouch = await js.InvokeAsync<bool>("chessFixer.isTouch");
        }
        catch (JSException)
        {
            IsTouch = false;
        }
    }
}
