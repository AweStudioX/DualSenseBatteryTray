using System.Runtime.InteropServices;
using DrawingIcon = System.Drawing.Icon;

namespace DualSenseBatteryTray.App.Shell;

internal sealed class WindowSmallIconController(
    Func<DrawingIcon> createIcon,
    Action<nint, nint>? applyIcon = null,
    Action<DrawingIcon>? disposeIcon = null) : IDisposable
{
    private DrawingIcon? _icon;
    private readonly Action<nint, nint> _applyIcon = applyIcon ?? WindowIconNative.SetSmallIcon;
    private readonly Action<DrawingIcon> _disposeIcon = disposeIcon ?? (icon => icon.Dispose());

    public void Apply(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
            throw new ArgumentException("A created window handle is required.", nameof(windowHandle));
        if (_icon is not null)
            return;

        var next = createIcon();
        try
        {
            _applyIcon(windowHandle, next.Handle);
            _icon = next;
        }
        catch
        {
            _disposeIcon(next);
            throw;
        }
    }

    public void Dispose()
    {
        if (_icon is not null)
            _disposeIcon(_icon);
        _icon = null;
    }
}

internal static class WindowIconNative
{
    private const uint WmSetIcon = 0x0080;
    private static readonly nint IconSmall = nint.Zero;

    internal static void SetSmallIcon(nint windowHandle, nint iconHandle) =>
        _ = SendMessageW(windowHandle, WmSetIcon, IconSmall, iconHandle);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);
}
