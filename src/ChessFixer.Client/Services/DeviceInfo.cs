using Microsoft.JSInterop;

namespace ChessFixer.Client.Services;

/// <summary>What kind of device the app runs on, asked once from the browser.</summary>
public sealed class DeviceInfo(IJSRuntime js)
{
    private bool? _isTouch;

    /// <summary>
    /// True on a phone or tablet (a touchscreen is the main input). There, focusing a text field opens the on-screen
    /// keyboard over half the screen, so fields are not focused automatically.
    /// </summary>
    public async ValueTask<bool> IsTouchAsync()
    {
        if (_isTouch is { } known) return known;
        try
        {
            _isTouch = await js.InvokeAsync<bool>("chessFixer.isTouch");
        }
        catch (JSException)
        {
            _isTouch = false;
        }
        return _isTouch.Value;
    }
}
