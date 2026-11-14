using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace GameVault.App;

/// <summary>Integrated chrome with a native maximize hit region for Windows 11 Snap Layouts.</summary>
public static class WindowChromeBehavior
{
    private static readonly ConditionalWeakTable<Window, NativeCaption> Captions = new();
    private static readonly DependencyPropertyKey IsNativeMouseOverPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "IsNativeMouseOver", typeof(bool), typeof(WindowChromeBehavior), new PropertyMetadata(false));
    public static readonly DependencyProperty IsNativeMouseOverProperty = IsNativeMouseOverPropertyKey.DependencyProperty;
    public static bool GetIsNativeMouseOver(DependencyObject element) => (bool)element.GetValue(IsNativeMouseOverProperty);

    // HTMAXBUTTON changes input to WM_NC*. WPF ButtonBase only processes client input.
    // Consume the native press/release after forwarding; passing it to DefWindowProc as well
    // would execute a second maximize/restore via WM_SYSCOMMAND.
    internal static uint ClientButtonMessage(uint message, nuint hitTest) => hitTest != 9 ? 0u : message switch
    {
        0x00A1 or 0x00A3 => 0x0201u, // WM_NCLBUTTONDOWN/DBLCLK -> WM_LBUTTONDOWN
        0x00A2 => 0x0202u,           // WM_NCLBUTTONUP -> WM_LBUTTONUP
        _ => 0u
    };

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

    internal static int ResizeHitTest(Point point, Rect bounds, Thickness border, bool maximized)
    {
        if (maximized || bounds.IsEmpty || point.X < bounds.Left || point.X >= bounds.Right
            || point.Y < bounds.Top || point.Y >= bounds.Bottom) return 0;
        var left = point.X < bounds.Left + border.Left;
        var right = point.X >= bounds.Right - border.Right;
        if (point.Y < bounds.Top + border.Top) return left ? 14 : right ? 15 : 13;
        if (point.Y >= bounds.Bottom - border.Bottom) return left ? 17 : right ? 18 : 16;
        return left ? 10 : right ? 11 : 0;
    }

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
        private const uint WmNcMouseMove = 0x00A0;
        private const uint WmNcMouseLeave = 0x02A2;
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
            maximizeButton.SetValue(IsNativeMouseOverPropertyKey, false);
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
            var forwardingButtonInput = false;
            try
            {
                if (message is 0x0200 or 0x02A3 or 0x0215 or 0x001F) // Client move/leave, capture changed, cancel.
                    maximizeButton.SetValue(IsNativeMouseOverPropertyKey, false);
                if (message == WmNcHitTest)
                {
                    var screen = ScreenPoint(lParam);
                    if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
                        && TryWindowGeometry(out var bounds, out var border))
                    {
                        // WindowChrome's client opt-out precedes its resize logic. Restore
                        // the physical border before either client controls or HTMAXBUTTON.
                        var resize = ResizeHitTest(screen, bounds, border, window.WindowState == WindowState.Maximized);
                        if (resize != 0) return resize;
                    }
                    if (IsOverMaximize(screen)) return 9; // HTMAXBUTTON
                }
                if (message == WmNcMouseMove)
                {
                    var over = wParam == 9 && IsOverMaximize(ScreenPoint(lParam));
                    maximizeButton.SetValue(IsNativeMouseOverPropertyKey, over);
                    if (over)
                    {
                        var tracking = new TrackMouseEventOptions
                        {
                            Size = (uint)Marshal.SizeOf<TrackMouseEventOptions>(),
                            Flags = 0x00000002 | 0x00000010, // TME_LEAVE | TME_NONCLIENT
                            Window = hwnd
                        };
                        TrackMouseEvent(ref tracking);
                    }
                    // Keep the original nonclient move in the native chain for Snap Layouts.
                }
                if (message == WmNcMouseLeave)
                    maximizeButton.SetValue(IsNativeMouseOverPropertyKey, false);
                var clientMessage = ClientButtonMessage(message, wParam);
                if (clientMessage != 0)
                {
                    // Win32 coordinates are physical screen pixels; ScreenToClient handles
                    // negative monitor origins and per-monitor DPI without logical scaling.
                    var screen = ScreenPoint(lParam);
                    var point = new NativePoint { X = (int)screen.X, Y = (int)screen.Y };
                    if (!ScreenToClient(hwnd, ref point)) return 0;
                    var clientPoint = unchecked((nint)((point.Y & 0xffff) << 16 | (point.X & 0xffff)));
                    forwardingButtonInput = true;
                    maximizeButton.SetValue(IsNativeMouseOverPropertyKey, false);
                    if (clientMessage == 0x0201)
                        DefSubclassProc(hwnd, 0x0200, 0, clientPoint); // Establish WPF's mouse target first.
                    // ButtonBase owns capture, pressed visuals, drag-out cancellation and Click.
                    // Once captured, the real release arrives as WM_LBUTTONUP and stays untouched.
                    return DefSubclassProc(hwnd, clientMessage, clientMessage == 0x0201 ? (nuint)1 : 0, clientPoint);
                }
                if (message == WmNcDestroy) Detach();
            }
            catch (Exception)
            {
                // This is an unmanaged callback boundary. If visual state is unavailable,
                // preserve the normal WPF/native chain instead of propagating an exception.
                // Once client forwarding begins, never retry the native button action.
                if (forwardingButtonInput) return 0;
            }
            try
            {
                // Non-button messages, including native hover/Snap, drag, resize and system
                // commands, keep their normal route. No synthetic Click or system command.
                return DefSubclassProc(hwnd, message, wParam, lParam);
            }
            catch (Exception)
            {
                // No managed exception may unwind through the native SUBCLASSPROC ABI.
                // Never call the chain twice: the message may already have performed an action.
                return 0;
            }
        }
        private bool TryWindowGeometry(out Rect bounds, out Thickness physicalBorder)
        {
            bounds = Rect.Empty;
            physicalBorder = new Thickness();
            if (handle == 0 || !GetWindowRect(handle, out var rectangle)) return false;
            bounds = new Rect(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);
            var dpi = VisualTreeHelper.GetDpi(window);
            var border = WindowChrome.GetWindowChrome(window)?.ResizeBorderThickness ?? new Thickness();
            physicalBorder = new Thickness(border.Left * dpi.DpiScaleX, border.Top * dpi.DpiScaleY,
                border.Right * dpi.DpiScaleX, border.Bottom * dpi.DpiScaleY);
            return true;
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
                if (!TryWindowGeometry(out var windowBounds, out var physicalBorder)) return false;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventOptions
    {
        public uint Size;
        public uint Flags;
        public nint Window;
        public uint HoverTime;
    }
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TrackMouseEventOptions options);
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);

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
