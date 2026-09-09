using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FluxDisplay.App.Helpers;

internal static class WindowCapture
{
    public static async Task CaptureHwndAsync(nint hwnd, string path)
    {
        if (hwnd == 0 || !GetWindowRect(hwnd, out var rect))
        {
            return;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 8 || height < 8)
        {
            return;
        }

        var hdcWindow = GetWindowDC(hwnd);
        if (hdcWindow == 0)
        {
            return;
        }

        var hdcMem = CreateCompatibleDC(hdcWindow);
        var hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
        var old = SelectObject(hdcMem, hBitmap);
        try
        {
            if (!PrintWindow(hwnd, hdcMem, 2))
            {
                BitBlt(hdcMem, 0, 0, width, height, hdcWindow, 0, 0, 0x00CC0020);
            }

            var pixels = new byte[width * height * 4];
            var info = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };
            if (GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixels, ref info, 0) == 0)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, pixels);
            await encoder.FlushAsync();
            stream.Seek(0);
            var size = (uint)stream.Size;
            var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(path, bytes);
        }
        finally
        {
            SelectObject(hdcMem, old);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(hwnd, hdcWindow);
        }
    }

    public static async Task CaptureMainAsync(Microsoft.UI.Xaml.Window window, string path)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        await CaptureHwndAsync(hwnd, path);
    }

    public static IReadOnlyList<(nint Hwnd, string Title, int Width, int Height)> ListProcessWindows()
    {
        var pid = GetCurrentProcessId();
        var found = new List<(nint, string, int, int)>();
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var windowPid);
            if (windowPid != pid || !IsWindowVisible(hwnd) || !GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width < 40 || height < 40)
            {
                return true;
            }

            var title = new char[256];
            _ = GetWindowText(hwnd, title, title.Length);
            found.Add((hwnd, new string(title).TrimEnd('\0'), width, height));
            return true;
        }, 0);
        return found;
    }

    public static async Task CaptureProcessWindowsAsync(string directory, string prefix)
    {
        Directory.CreateDirectory(directory);
        var index = 1;
        foreach (var window in ListProcessWindows())
        {
            var safe = string.Join("_", window.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = "window";
            }

            var name = $"{prefix}-{index:00}-{safe}-{window.Width}x{window.Height}.png";
            await CaptureHwndAsync(window.Hwnd, Path.Combine(directory, name));
            index++;
        }
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out Rect lpRect);

    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint hdc, nint hbm, uint start, uint cLines, byte[] lpvBits, ref BitmapInfoHeader lpbmi, uint usage);
}
