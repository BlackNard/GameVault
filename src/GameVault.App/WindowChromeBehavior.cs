using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace GameVault.App;

/// <summary>Integrated chrome with a native maximize hit region for Windows 11 Snap Layouts.</summary>
internal static class WindowChromeBehavior
{
    private static readonly ConditionalWeakTable<Window, NativeCaption> Captions = new();

    public static void Apply(Window window) => Apply(window, null);

    public static void Apply(Window window, Button? maximizeButton)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.Dispatcher.VerifyAccess();
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 44,
            ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness,
            CornerRadius = new CornerRadius(0),
            GlassFrameThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });
        // Keep AllowsTransparency false. Only the maximize region needs native hit testing;
        // WindowChrome continues handling resize/drag and the other caption buttons stay WPF.
        if (Captions.TryGetValue(window, out var existing))
        {
            existing.Detach();
            Captions.Remove(window);
        }
        if (maximizeButton is not null)
        {
            var caption = new NativeCaption(window, maximizeButton);
            Captions.Add(window, caption); // Keep the unmanaged callback's delegate alive until teardown.
            caption.Attach();
        }
    }

    internal static Point ScreenPoint(nint packed) => new(
        unchecked((short)(packed.ToInt64() & 0xffff)),
        unchecked((short)((packed.ToInt64() >> 16) & 0xffff)));

    internal static bool IsMaximizeRegion(Point screenPoint, Rect buttonBounds, Rect windowBounds,
        Thickness resizeBorder, bool maximized, bool enabled)
    {
        // Use half-open native rectangle semantics: the adjacent minimize/close button owns its edge.
        if (!enabled || buttonBounds.IsEmpty || screenPoint.X < buttonBounds.Left || screenPoint.X >= buttonBounds.Right
            || screenPoint.Y < buttonBounds.Top || screenPoint.Y >= buttonBounds.Bottom) return false;
        if (maximized) return true;
        return screenPoint.X >= windowBounds.Left + resizeBorder.Left
            && screenPoint.X < windowBounds.Right - resizeBorder.Right
            && screenPoint.Y >= windowBounds.Top + resizeBorder.Top
            && screenPoint.Y < windowBounds.Bottom - resizeBorder.Bottom;
    }

    private sealed class NativeCaption(Window window, Button maximizeButton)
    {
        private const uint WmNcHitTest = 0x0084;
        private const uint WmNcDestroy = 0x0082;
        private const nuint SubclassId = 0x47564d58; // GameVault maximize caption.
        private nint handle;

        // SetWindowSubclass runs before the WPF HWND procedure, avoiding hook-order dependencies
        // with WindowChrome's WM_NCHITTEST handler (which otherwise returns HTCLIENT).
        public void Attach()
        {
            window.SourceInitialized += SourceInitialized;
            window.Closed += Closed;
            var current = new WindowInteropHelper(window).Handle;
            if (current != 0) Install(current);
        }

        private void SourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= SourceInitialized;
            Install(new WindowInteropHelper(window).Handle);
        }

        private void Install(nint hwnd)
        {
            if (handle != 0) return;
            // Assign once on the dispatcher, before registering its function pointer with Win32.
            registeredCallback = WndProc;
            if (!SetWindowSubclass(hwnd, registeredCallback, SubclassId, 0))
                throw new InvalidOperationException("Could not install the window caption hit-test handler.");
            handle = hwnd;
        }
        private SubclassProc? registeredCallback;

        public void Detach()
        {
            window.SourceInitialized -= SourceInitialized;
            window.Closed -= Closed;
            if (handle != 0 && registeredCallback is not null)
            {
                RemoveWindowSubclass(handle, registeredCallback, SubclassId);
                handle = 0;
            }
        }
        private void Closed(object? sender, EventArgs e)
        {
            Detach();
            Captions.Remove(window);
        }

        private nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
        {
            try
            {
                if (message == WmNcHitTest && IsOverMaximize(ScreenPoint(lParam))) return 9; // HTMAXBUTTON
                if (message == WmNcDestroy) Detach();
            }
            catch (Exception)
            {
                // This is an unmanaged callback boundary. If visual state is unavailable,
                // preserve the normal WPF/native chain instead of propagating an exception.
            }
            try
            {
                // Do NOT raise WPF Click for WM_NCLBUTTONDOWN/UP: default caption tracking
                // sends WM_SYSCOMMAND once. Forward move/leave so Windows owns Snap hover.
                return DefSubclassProc(hwnd, message, wParam, lParam);
            }
            catch (Exception)
            {
                // No managed exception may unwind through the native SUBCLASSPROC ABI.
                // Never call the chain twice: the message may already have performed an action.
                return 0;
            }
        }
        private bool IsOverMaximize(Point screenPoint)
        {
            if (!maximizeButton.IsLoaded || !maximizeButton.IsVisible || !maximizeButton.IsEnabled
                || !window.IsEnabled || !window.IsVisible
                || window.ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)) return false;
            try
            {
                var buttonBounds = new Rect(maximizeButton.PointToScreen(new Point()),
                    maximizeButton.PointToScreen(new Point(maximizeButton.ActualWidth, maximizeButton.ActualHeight)));
                var windowBounds = new Rect(window.PointToScreen(new Point()),
                    window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight)));
                var dpi = VisualTreeHelper.GetDpi(window);
                var border = WindowChrome.GetWindowChrome(window)?.ResizeBorderThickness ?? new Thickness();
                var physicalBorder = new Thickness(border.Left * dpi.DpiScaleX, border.Top * dpi.DpiScaleY,
                    border.Right * dpi.DpiScaleX, border.Bottom * dpi.DpiScaleY);
                return IsMaximizeRegion(screenPoint, buttonBounds, windowBounds, physicalBorder,
                    window.WindowState == WindowState.Maximized, true);
            }
            catch (InvalidOperationException)
            {
                // PresentationSource may disappear during HWND teardown; leave default hit testing intact.
                return false;
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint subclassId, nuint reference);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint subclassId, nuint reference);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint subclassId);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);
}
