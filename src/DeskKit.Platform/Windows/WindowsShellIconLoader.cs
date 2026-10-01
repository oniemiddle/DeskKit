using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DeskKit.Core.Abstractions;
using DeskKit.Platform.Interop;

namespace DeskKit.Platform.Windows;

/// <summary>
/// Extracts shell icons with raw GDI rather than <c>System.Drawing</c>, which is
/// Windows-only, deprecated for this purpose and would drag a second graphics
/// stack into the process.
/// <para>
/// The icon is drawn into a 32-bit top-down DIB section and copied out. GDI does
/// not tell us whether the source icon had an alpha channel, so an icon whose
/// every alpha byte came back zero is treated as fully opaque — otherwise legacy
/// icons would render as nothing at all. Pixels are premultiplied because that
/// is the format the renderer expects for bitmaps with an alpha channel.
/// </para>
/// </summary>
public sealed class WindowsShellIconLoader : IShellIconLoader
{
    private readonly ConcurrentDictionary<string, Bitmap?> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<Bitmap?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(path))
            return Task.FromResult<Bitmap?>(null);

        return Task.Run(() => _cache.GetOrAdd(path, Extract), cancellationToken);
    }

    private static Bitmap? Extract(string path)
    {
        var info = new NativeMethods.SHFILEINFO();
        var handle = NativeMethods.SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            Win32.SHGFI_ICON | Win32.SHGFI_LARGEICON);

        if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return null;

        try
        {
            return ConvertIcon(info.hIcon);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.hIcon);
        }
    }

    private static Bitmap? ConvertIcon(IntPtr hIcon)
    {
        var width = Math.Max(NativeMethods.GetSystemMetrics(Win32.SM_CXICON), 16);
        var height = Math.Max(NativeMethods.GetSystemMetrics(Win32.SM_CYICON), 16);

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var bitmap = IntPtr.Zero;
        var previous = IntPtr.Zero;

        try
        {
            var info = CreateBitmapInfo(width, height);
            bitmap = NativeMethods.CreateDIBSection(
                memoryDc, ref info, Win32.DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);

            if (bitmap == IntPtr.Zero)
                return null;

            previous = NativeMethods.SelectObject(memoryDc, bitmap);

            if (!NativeMethods.DrawIconEx(
                    memoryDc, 0, 0, hIcon, width, height, 0, IntPtr.Zero, Win32.DI_NORMAL))
            {
                return null;
            }

            var pixels = new byte[width * height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);

            NormalizeAlpha(pixels);
            Premultiply(pixels);

            return CreateBitmap(width, height, pixels);
        }
        finally
        {
            if (previous != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, previous);

            if (bitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(bitmap);

            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static NativeMethods.BITMAPINFO CreateBitmapInfo(int width, int height) =>
        new()
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = width,

                // A negative height asks for top-down rows, matching the order
                // the renderer expects.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Win32.BI_RGB,
            },
        };

    private static void NormalizeAlpha(byte[] pixels)
    {
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
                return;
        }

        // Nothing was transparent, so the icon came from a source without an
        // alpha channel. Treat it as opaque instead of invisible.
        for (var index = 3; index < pixels.Length; index += 4)
            pixels[index] = 255;
    }

    private static void Premultiply(byte[] pixels)
    {
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alpha = pixels[index + 3];
            if (alpha == 255)
                continue;

            if (alpha == 0)
            {
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = 0;
                continue;
            }

            pixels[index] = (byte)((pixels[index] * alpha) / 255);
            pixels[index + 1] = (byte)((pixels[index + 1] * alpha) / 255);
            pixels[index + 2] = (byte)((pixels[index + 2] * alpha) / 255);
        }
    }

    private static Bitmap CreateBitmap(int width, int height, byte[] pixels)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using var buffer = bitmap.Lock();
        var rowBytes = width * 4;

        for (var y = 0; y < height; y++)
        {
            var destination = buffer.Address + (y * buffer.RowBytes);
            Marshal.Copy(pixels, y * rowBytes, destination, rowBytes);
        }

        return bitmap;
    }
}
