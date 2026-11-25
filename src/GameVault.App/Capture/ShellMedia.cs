using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using GameVault.Infrastructure.Library;

namespace GameVault.App;

/// <summary>
/// Thumbnails and video lengths from Windows itself (the same ones File Explorer shows), so no codec or library is bundled. Work runs one item at a time on a
/// background STA thread; Windows keeps its own thumbnail cache on disk, and a small in-memory cache avoids asking twice.
/// </summary>
internal static class ShellMedia
{
    private static readonly BlockingCollection<Action> Work = new(new ConcurrentStack<Action>());      // newest first: what the player is looking at now
    private static readonly BoundedAsyncCache<BitmapSource> Thumbnails =
        new(48L * 1024 * 1024, 4, image => checked((long)image.PixelWidth * image.PixelHeight * 4));

    static ShellMedia()
    {
        var thread = new Thread(() => { foreach (var action in Work.GetConsumingEnumerable()) action(); }) { IsBackground = true, Name = "Capture thumbnails" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static Task<T> Run<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Work.Add(() =>
        {
            try { result.SetResult(action()); }
            catch (Exception error) { result.SetException(error); }
        });
        return result.Task;
    }

    /// <summary>A picture of the file at most 232 by 131 pixels, or null when Windows has none (the tile then shows a placeholder).</summary>
    public static async Task<BitmapSource?> ThumbnailAsync(string path, CancellationToken cancellationToken = default)
    {
        try { return await Thumbnails.GetAsync(Path.GetFullPath(path), () => Run(() => Extract(path)), cancellationToken); }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidCastException) { return null; }
    }

    public static async Task<TimeSpan?> DurationAsync(string path)
    {
        try { return await Run(() => ReadDuration(path)); }
        catch (Exception error) when (error is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or InvalidCastException) { return null; }
    }

    private static BitmapSource Extract(string path)
    {
        var factoryId = typeof(IShellItemImageFactory).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref factoryId, out var item);
        var handle = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(((IShellItemImageFactory)item).GetImage(new NativeSize { Width = 232, Height = 131 }, 0x8 /* thumbnail only: no generic file icon */, out handle));
            var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        finally
        {
            if (handle != IntPtr.Zero) DeleteObject(handle);
            Marshal.ReleaseComObject(item);
        }
    }

    private static TimeSpan? ReadDuration(string path)
    {
        var itemId = typeof(IShellItem2).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref itemId, out var item);
        try
        {
            var key = new PropertyKey { Format = new Guid("64440490-4C8B-11D1-8B70-080036B11A03"), Id = 3 };     // System.Media.Duration (100 ns units)
            var status = ((IShellItem2)item).GetUInt64(ref key, out var ticks);
            return status == 0 && ticks > 0 && ticks <= (ulong)TimeSpan.TicksPerDay * 7 ? TimeSpan.FromTicks((long)ticks) : null;
        }
        finally { Marshal.ReleaseComObject(item); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }

    // Only GetUInt64 is called; the earlier members keep the native method order (IShellItem, then IShellItem2).
    [ComImport, Guid("7e9fb0d3-919f-4307-ab2e-9b1860310c93"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem2
    {
        void BindToHandler(); void GetParent(); void GetDisplayName(); void GetAttributes(); void Compare();
        void GetPropertyStore(); void GetPropertyStoreWithCreateObject(); void GetPropertyStoreForKeys(); void GetPropertyDescriptionList(); void Update();
        void GetProperty(); void GetCLSID(); void GetFileTime(); void GetInt32(); void GetString(); void GetUInt32();
        [PreserveSig] int GetUInt64(ref PropertyKey key, out ulong value);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
